using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
using LibAlbionData;
using LibAlbionData.Core;
using LibServiceLifecycle;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Servicio que parsea y expone el catálogo completo de ítems de Albion Online.
///
/// Lee el XDocument directamente de <see cref="AlbionData"/> y lo parsea por sí mismo.
/// No depende de AlbionItemLoader ni de ningún modelo intermedio — <see cref="ItemBase"/>
/// es el único modelo de ítem en la aplicación.
///
/// Dos índices en FrozenDictionary (escritura única, lecturas millones de veces sin lock):
///   <c>_items</c>   — todos los ítems indexados por ItemId (incluye variantes @N).
///   <c>_recipes</c> — recetas por ItemId. Array vacío para ítems sin receta (nunca null).
///
/// Parseo de variantes de encantamiento — dos mecanismos en el juego:
///   1. Nodo hijo: elemento base (T4_MAIN_SWORD) con <c>&lt;enchantments&gt;&lt;enchantment enchantmentlevel="1"/&gt;</c>.
///      Cada variante se indexa como ítem independiente con sufijo @N (T4_MAIN_SWORD@1, @2…).
///   2. Atributo inline: elemento con <c>enchantmentlevel="N"</c> directo y uniquename con _LEVEL{N}
///      (ej. T8_PLANKS_LEVEL1). Se parsea como ítem normal con EnchantmentLevel leído del atributo
///      y BaseItemId derivado eliminando el sufijo _LEVEL{N}.
/// </summary>
public sealed class ItemDataService : ServiceBase, IItemDataService
{
    private readonly AlbionData _albionData;

    private volatile FrozenDictionary<string, ItemBase>    _items
        = FrozenDictionary<string, ItemBase>.Empty;

    private volatile FrozenDictionary<string, ItemBase[]> _byBaseId
        = FrozenDictionary<string, ItemBase[]>.Empty;

    private volatile FrozenDictionary<string, IRecipe[]> _recipes
        = FrozenDictionary<string, IRecipe[]>.Empty;

    public override string ServiceName => "ItemDataService";

    public ItemDataService(AlbionData albionData)
        => _albionData = albionData;

    // ── API pública (State == On) ─────────────────────────────────────────────

    public IReadOnlyList<ItemBase> GetAll()
    {
        EnsureOn();
        return _items.Values;
    }

    /// <summary>
    /// Retorna los primeros <paramref name="count"/> ítems del catálogo.
    /// Usado como vista por defecto cuando no hay query de texto — evita
    /// iterar el catálogo completo (~20 k ítems) para mostrar solo 300.
    ///
    /// <para>El orden interno de <see cref="FrozenDictionary"/> no está garantizado;
    /// el caller es responsable de ordenar el resultado si lo requiere.</para>
    /// </summary>
    public IReadOnlyList<ItemBase> GetFirst(int count)
    {
        EnsureOn();
        // Take es lazy sobre IEnumerable<ItemBase> — solo recorre 'count' entradas.
        return _items.Values.Take(count).ToList();
    }

    public ItemBase? GetById(string itemId)
    {
        EnsureOn();
        return _items.GetValueOrDefault(itemId);
    }

    public bool TryGetById(string itemId, [NotNullWhen(true)] out ItemBase? item)
    {
        EnsureOn();
        return _items.TryGetValue(itemId, out item);
    }

    /// <summary>
    /// Retorna los ítems correspondientes a los IDs dados, en un solo batch lookup.
    ///
    /// <para>Diseñado para el pipeline de búsqueda indexada:</para>
    /// <code>
    ///   ILocalizationService.GetTexts(...) → itemIds[]
    ///   ItemDataService.GetItemsByIds(itemIds) → ItemBase[]
    /// </code>
    ///
    /// <para>Comportamiento:</para>
    /// <list type="bullet">
    ///   <item>Los IDs no encontrados en el catálogo se ignoran silenciosamente.</item>
    ///   <item>El orden de retorno NO garantiza coincidir con el orden de entrada.</item>
    ///   <item>Sin LINQ — loop foreach con TryGetValue directo al FrozenDictionary.</item>
    ///   <item>Capacidad preallocada = Count del input para evitar resize en el caso común.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<ItemBase> GetItemsByIds(IReadOnlyList<string> itemIds)
    {
        EnsureOn();

        var result = new List<ItemBase>(itemIds.Count);
        var store  = _items;

        foreach (var id in itemIds)
        {
            if (store.TryGetValue(id, out var item))
                result.Add(item);
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ItemBase> GetItemsByBaseIds(IReadOnlyList<string> baseItemIds)
    {
        EnsureOn();

        var index  = _byBaseId;
        var result = new List<ItemBase>(baseItemIds.Count * 4); // heurística: ~4 variantes por base

        foreach (var baseId in baseItemIds)
        {
            if (index.TryGetValue(baseId, out var variants))
                result.AddRange(variants);
        }

        return result;
    }

    /// <summary>
    /// Recetas del ítem. Nunca null:
    /// array vacío si el ítem no tiene recetas o el id no existe.
    /// </summary>
    public IRecipe[] GetRecipes(string itemId)
    {
        EnsureOn();
        return _recipes.GetValueOrDefault(itemId) ?? [];
    }

    // ── AlbionServiceBase ─────────────────────────────────────────────────────

    protected override Task OnStartAsync(CancellationToken ct)
        => Task.Run(() => ParseItems(ct), ct);

    protected override Task OnStopAsync(CancellationToken ct)
    {
        _items    = FrozenDictionary<string, ItemBase>.Empty;
        _byBaseId = FrozenDictionary<string, ItemBase[]>.Empty;
        _recipes  = FrozenDictionary<string, IRecipe[]>.Empty;
        return Task.CompletedTask;
    }

    // ── Parser principal ──────────────────────────────────────────────────────

    private void ParseItems(CancellationToken ct)
    {
        SetProgress(0);

        var doc = _albionData.GetXDocument(GameDataPath.Items);

        // ── Fase 1: índice de categorías ──────────────────────────────────────
        // Se parsea la sección <shopcategories> del mismo XDocument (ya en memoria).
        // El índice traduce los cuatro IDs de categoría de cada ítem a un único int.
        var categoryIndex = CategoryValueIndex.Build(doc);

        SetProgress(5);

        // ── Fase 2: recolectar elementos de ítem ──────────────────────────────
        // El root contiene <shopcategories> — lo excluimos; ya fue procesado.
        var elements = doc.Root?
            .Elements()
            .Where(e => !string.Equals(e.Name.LocalName, "shopcategories",
                                       StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        SetProgress(10);

        var itemsBuilder   = new Dictionary<string, ItemBase>(elements.Count * 2,
                                 StringComparer.OrdinalIgnoreCase);
        var recipesBuilder = new Dictionary<string, IRecipe[]>(elements.Count * 2,
                                 StringComparer.OrdinalIgnoreCase);

        int total = elements.Count;
        int step  = Math.Max(1, total / 80);

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var element    = elements[i];
            var uniqueName = Attr(element, "uniquename");
            if (string.IsNullOrWhiteSpace(uniqueName)) continue;

            // ── Ítem base ────────────────────────────────────────────────────
            var baseItem = ParseElement(element, uniqueName, enchantmentLevel: 0, categoryIndex);
            if (baseItem is not null)
            {
                itemsBuilder[uniqueName]   = baseItem;
                recipesBuilder[uniqueName] = ParseRecipes(element, baseItem);
            }

            // ── Variantes de encantamiento ───────────────────────────────────
            var enchantmentsNode = element.Element("enchantments");
            if (enchantmentsNode is not null)
            {
                foreach (var enchEl in enchantmentsNode.Elements("enchantment"))
                {
                    var lvl = ParseInt(Attr(enchEl, "enchantmentlevel")) ?? 0;
                    if (lvl <= 0) continue;

                    var enchItemId = $"{uniqueName}@{lvl}";
                    var enchItem   = ParseEnchantmentElement(element, enchEl, enchItemId, uniqueName, lvl, categoryIndex);
                    if (enchItem is null) continue;

                    itemsBuilder[enchItemId]   = enchItem;
                    recipesBuilder[enchItemId] = ParseRecipes(enchEl, enchItem);
                }
            }

            if (i % step == 0)
                SetProgress(10 + (int)(i / (double)total * 80));
        }

        SetProgress(90);
        _items   = itemsBuilder  .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _recipes = recipesBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Índice BaseItemId → variantes (base + encantamientos @N).
        // Permite expandir resultados de búsqueda de texto sin escanear los ~20k ítems.
        _byBaseId = itemsBuilder.Values
            .GroupBy(i => i.BaseItemId, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        SetProgress(100);
    }

    // ── Parseo de elementos ───────────────────────────────────────────────────

    private static ItemBase? ParseElement(
        XElement           element,
        string             uniqueName,
        int                enchantmentLevel,
        CategoryValueIndex categoryIndex)
    {
        var attrs = ReadAttributes(element);

        // Algunos ítems base (ej. T8_PLANKS_LEVEL1) codifican el encantamiento como atributo
        // directo en el elemento raíz en lugar de usar nodos <enchantments> hijos.
        // Leemos el atributo cuando el caller pasa 0 (el default para ítems no encantados).
        var actualLevel = enchantmentLevel > 0
            ? enchantmentLevel
            : ParseInt(Attr(element, "enchantmentlevel")) ?? 0;

        // BaseItemId: para variantes _LEVEL{N}, apunta al ítem base sin sufijo.
        // Ejemplo: T8_PLANKS_LEVEL1 → BaseItemId = "T8_PLANKS"
        var baseItemId = DeriveBaseItemId(uniqueName, actualLevel);

        var categoryValue = categoryIndex.Resolve(
            Attr(element, "shopcategory"),
            Attr(element, "shopsubcategory1"),
            Attr(element, "shopsubcategory2"),
            Attr(element, "shopsubcategory3"));

        return new ItemBase(
            ItemId:                     uniqueName,
            BaseItemId:                 baseItemId,
            NameLocalizationKey:        BuildNameKey(element, uniqueName),
            DescriptionLocalizationKey: Attr(element, "descriptionlocatag"),
            Tier:                       ParseInt(Attr(element, "tier")),
            EnchantmentLevel:           actualLevel,
            CategoryValue:              categoryValue,
            CraftingCategory:           Attr(element, "craftingcategory"),
            NodeType:                   element.Name.LocalName,
            IsCraftable:                HasCraftingIngredients(element),
            RawAttributes:              attrs);
    }

    /// <summary>
    /// Deriva el BaseItemId eliminando el sufijo <c>_LEVEL{N}</c> cuando existe.
    /// Si el uniqueName no termina con ese sufijo, retorna el mismo string (sin alloc).
    /// </summary>
    private static string DeriveBaseItemId(string uniqueName, int enchantmentLevel)
    {
        if (enchantmentLevel <= 0) return uniqueName;

        var suffix = $"_LEVEL{enchantmentLevel}";
        return uniqueName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? uniqueName[..^suffix.Length]
            : uniqueName;
    }

    private static ItemBase? ParseEnchantmentElement(
        XElement           baseElement,
        XElement           enchElement,
        string             itemId,
        string             baseUniqueName,
        int                enchantmentLevel,
        CategoryValueIndex categoryIndex)
    {
        // Mergeamos los atributos: base primero, enchantment sobreescribe.
        var merged = ReadAttributes(baseElement)
            .Concat(ReadAttributes(enchElement))
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        // Las variantes encantadas heredan la categoría del ítem base.
        var categoryValue = categoryIndex.Resolve(
            Attr(baseElement, "shopcategory"),
            Attr(baseElement, "shopsubcategory1"),
            Attr(baseElement, "shopsubcategory2"),
            Attr(baseElement, "shopsubcategory3"));

        return new ItemBase(
            ItemId:                     itemId,
            BaseItemId:                 baseUniqueName,
            NameLocalizationKey:        BuildNameKey(baseElement, baseUniqueName),
            DescriptionLocalizationKey: Attr(baseElement, "descriptionlocatag"),
            Tier:                       ParseInt(Attr(baseElement, "tier")),
            EnchantmentLevel:           enchantmentLevel,
            CategoryValue:              categoryValue,
            CraftingCategory:           Attr(baseElement, "craftingcategory"),
            NodeType:                   baseElement.Name.LocalName,
            IsCraftable:                HasCraftingIngredients(enchElement),
            RawAttributes:              merged);
    }

    // ── Parseo de recetas ─────────────────────────────────────────────────────

    /// <summary>
    /// Parsea TODAS las recetas de un elemento (puede tener múltiples
    /// <c>&lt;craftingrequirements&gt;</c>). Si no tiene, retorna array vacío.
    /// </summary>
    private static IRecipe[] ParseRecipes(XElement element, ItemBase ownerItem)
    {
        var reqElements = element.Elements("craftingrequirements").ToList();
        if (reqElements.Count == 0) return [];

        var recipes = new List<IRecipe>(reqElements.Count);

        foreach (var req in reqElements)
        {
            var ingredients = req.Elements("craftresource")
                .Select(r => ParseIngredient(r))
                .Where(i => i is not null)
                .Cast<IIngredient>()
                .ToList()
                .AsReadOnly();

            if (ingredients.Count == 0) continue;

            recipes.Add(new Recipe(
                AmountCrafted: ParseInt(Attr(req, "amountcrafted")) ?? 1,
                CraftingFocus: ParseInt(Attr(req, "craftingfocus")),
                Silver:        ParseDecimal(Attr(req, "silver")),
                Ingredients:   ingredients));
        }

        return recipes.Count > 0 ? [.. recipes] : [];
    }

    private static Ingredient? ParseIngredient(XElement element)
    {
        var ingredientId = Attr(element, "uniquename");
        if (string.IsNullOrWhiteSpace(ingredientId)) return null;

        var count = ParseInt(Attr(element, "count")) ?? 1;

        // Regla de retorno de materiales (Resource Return Rate):
        //   • Atributo ausente     → el ingrediente SÍ participa en el retorno (valor por defecto).
        //     Aplica a todos los ingredientes normales de refinado y crafteo.
        //   • maxreturnamount="0"  → el ingrediente NO participa (opt-out explícito).
        //     Aplica a ítems especiales: capas de facción, ítems de evento, tokens, etc.
        var maxReturnRaw  = Attr(element, "maxreturnamount");
        var participates  = maxReturnRaw is null
                         || !maxReturnRaw.Equals("0", StringComparison.Ordinal);

        return new Ingredient(
            ItemId:               ingredientId,
            Count:                count,
            ParticipatesInReturn: participates);
    }

    // ── Helpers de parseo ─────────────────────────────────────────────────────

    private static string? BuildNameKey(XElement element, string uniqueName)
    {
        // El juego usa descvariable0 como clave de localización del nombre.
        // Fallback al patrón estándar @ITEMS_{UNIQUENAME}.
        var raw = Attr(element, "descvariable0");
        return !string.IsNullOrWhiteSpace(raw) ? raw : $"@ITEMS_{uniqueName}";
    }

    private static bool HasCraftingIngredients(XElement element)
        => element.Elements("craftingrequirements")
                  .Any(r => r.Elements("craftresource").Any());

    private static IReadOnlyDictionary<string, string> ReadAttributes(XElement element)
        => element.Attributes()
                  .ToDictionary(a => a.Name.LocalName, a => a.Value,
                                StringComparer.OrdinalIgnoreCase);

    private static string? Attr(XElement? element, string name)
        => (string?)element?.Attribute(name);

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
            ? r : null;

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var r)
            ? r : null;

    private void EnsureOn()
    {
        if (State is not ServiceState.On)
            throw new InvalidOperationException(
                $"{ServiceName} no está activo (estado: {State}). Llama StartAsync primero.");
    }
}
