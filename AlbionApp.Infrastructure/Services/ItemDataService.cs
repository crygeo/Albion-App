using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
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

    private volatile FrozenDictionary<string, JournalItem> _journalByItemId
        = FrozenDictionary<string, JournalItem>.Empty;

    /// <summary>
    /// Índice posicional: int index (1-based, orden del XML, sin shopcategories,
    /// variantes @N incluidas en la secuencia) → ItemBase.
    /// Permite resolver el equipamiento live que llega de la red como int.
    /// </summary>
    private volatile FrozenDictionary<int, ItemBase> _byIndex
        = FrozenDictionary<int, ItemBase>.Empty;

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
    /// Retorna los primeros <paramref name="count"/> ítems en orden XML (por <see cref="ItemBase.Index"/>).
    /// Usado como vista por defecto cuando no hay query de texto.
    /// </summary>
    public IReadOnlyList<ItemBase> GetFirst(int count)
    {
        EnsureOn();
        var result = new List<ItemBase>(count);
        var index  = _byIndex;
        for (int i = 1; result.Count < count && i <= index.Count; i++)
        {
            if (index.TryGetValue(i, out var item))
                result.Add(item);
        }
        return result;
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
    /// Resuelve el índice posicional que manda la red (CharacterEquipmentChanged,
    /// HealthUpdate, etc.) al <see cref="ItemBase"/> correspondiente.
    /// Retorna <c>null</c> si el índice no existe (0, fuera de rango, slot vacío).
    /// </summary>
    public ItemBase? GetByIndex(int index)
    {
        if (index <= 0) return null;
        EnsureOn();
        return _byIndex.GetValueOrDefault(index);
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

    /// <summary>
    /// Retorna el libro de laborer que se llena al craftear el ítem indicado,
    /// o null si el ítem no aparece en ningún craftitemfame journal.
    /// Acepta IDs con encantamiento (@N) — los normaliza automáticamente.
    /// </summary>
    public JournalItem? GetJournalForItem(string itemId)
    {
        EnsureOn();
        var baseId = StripEnchantmentSuffix(itemId);
        return _journalByItemId.GetValueOrDefault(baseId);
    }

    // ── AlbionServiceBase ─────────────────────────────────────────────────────

    protected override Task OnStartAsync(CancellationToken ct)
        => Task.Run(() => ParseItems(ct), ct);

    protected override Task OnStopAsync(CancellationToken ct)
    {
        _items           = FrozenDictionary<string, ItemBase>.Empty;
        _byBaseId        = FrozenDictionary<string, ItemBase[]>.Empty;
        _recipes         = FrozenDictionary<string, IRecipe[]>.Empty;
        _byIndex         = FrozenDictionary<int, ItemBase>.Empty;
        _journalByItemId = FrozenDictionary<string, JournalItem>.Empty;
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
        // Índice posicional: 1-based, mismo orden que el XML.
        // Cada elemento base + sus variantes @N consumen posiciones consecutivas.
        var indexBuilder   = new Dictionary<int, ItemBase>(elements.Count * 2);

        int total   = elements.Count;
        int step    = Math.Max(1, total / 80);
        int itemIdx = 0; // se incrementa antes de asignar → primer ítem = 1

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var element    = elements[i];
            var uniqueName = Attr(element, "uniquename");
            if (string.IsNullOrWhiteSpace(uniqueName)) continue;

            // ── Ítem base ────────────────────────────────────────────────────
            itemIdx++;
            var baseItem = ParseElement(element, uniqueName, enchantmentLevel: 0, categoryIndex, itemIdx);
            if (baseItem is not null)
            {
                itemsBuilder[uniqueName]   = baseItem;
                recipesBuilder[uniqueName] = ParseRecipes(element, baseItem);
                indexBuilder[itemIdx]      = baseItem;
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
                    itemIdx++;
                    var enchItem = ParseEnchantmentElement(element, enchEl, enchItemId, uniqueName, lvl, categoryIndex, itemIdx);
                    if (enchItem is null) continue;

                    itemsBuilder[enchItemId]   = enchItem;
                    recipesBuilder[enchItemId] = ParseRecipes(enchEl, enchItem);
                    indexBuilder[itemIdx]      = enchItem;
                }
            }

            if (i % step == 0)
                SetProgress(10 + (int)(i / (double)total * 80));
        }

        SetProgress(90);
        _items   = itemsBuilder  .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _recipes = recipesBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byIndex = indexBuilder  .ToFrozenDictionary();

        // Índice BaseItemId → variantes (base + encantamientos @N).
        // Permite expandir resultados de búsqueda de texto sin escanear los ~20k ítems.
        _byBaseId = itemsBuilder.Values
            .GroupBy(i => i.BaseItemId, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        // Índice inverso: baseItemId → JournalItem
        // Construido desde los <journalitem> del mismo XDocument.
        _journalByItemId = ParseJournals(doc);

        SetProgress(100);
    }

    // ── Parseo de elementos ───────────────────────────────────────────────────

    private static ItemBase? ParseElement(
        XElement           element,
        string             uniqueName,
        int                enchantmentLevel,
        CategoryValueIndex categoryIndex,
        int                index)
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
            Index:                      index,
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
            SlotType:                   ParseSlotType(Attr(element, "slottype")),
            IsTwoHanded:                Attr(element, "twohanded") == "true",
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
        CategoryValueIndex categoryIndex,
        int                index)
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
            Index:                      index,
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
            SlotType:                   ParseSlotType(Attr(baseElement, "slottype")),
            IsTwoHanded:                Attr(baseElement, "twohanded") == "true",
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
                AmountCrafted:   ParseInt(Attr(req, "amountcrafted")) ?? 1,
                CraftingFocus:   ParseInt(Attr(req, "craftingfocus")),
                Silver:          ParseDecimal(Attr(req, "silver")),
                Ingredients:     ingredients,
                IsTransmutation: string.Equals(
                    Attr(req, "craftbuttonlocaoverride"),
                    "@CRAFTBUILDING_ITEM_DETAILS_BUTTON_TRANSMUTE",
                    StringComparison.OrdinalIgnoreCase)));
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

    private static SlotType ParseSlotType(string? raw) => raw switch
    {
        "head"     => SlotType.Head,
        "armor"    => SlotType.Armor,
        "shoes"    => SlotType.Shoes,
        "cape"     => SlotType.Cape,
        "bag"      => SlotType.Bag,
        "mainhand" => SlotType.MainHand,
        "offhand"  => SlotType.OffHand,
        "potion"   => SlotType.Potion,
        "food"     => SlotType.Food,
        "mount"    => SlotType.Mount,
        _          => SlotType.None,
    };

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
            ? r : null;

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var r)
            ? r : null;

    // ── Parseo de journals ────────────────────────────────────────────────────

    /// <summary>
    /// Recorre todos los &lt;journalitem&gt; del documento y construye el índice inverso
    /// baseItemId → JournalItem a partir de los &lt;validitem&gt; de cada journal.
    /// </summary>
    private static FrozenDictionary<string, JournalItem> ParseJournals(XDocument doc)
    {
        var index = new Dictionary<string, JournalItem>(512, StringComparer.OrdinalIgnoreCase);

        var journalElements = doc.Root?
            .Descendants("journalitem")
            ?? Enumerable.Empty<XElement>();

        foreach (var journalEl in journalElements)
        {
            var uniqueName = Attr(journalEl, "uniquename");
            if (string.IsNullOrWhiteSpace(uniqueName)) continue;

            var tier    = ParseInt(Attr(journalEl, "tier")) ?? 0;
            var maxFame = ParseDouble(Attr(journalEl, "maxfame")) ?? 0;
            if (maxFame <= 0) continue;

            var journal = new JournalItem(uniqueName, tier, maxFame);

            var validItems = journalEl
                .Descendants("craftitemfame")
                .SelectMany(e => e.Elements("validitem"))
                .Select(e => Attr(e, "id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>();

            foreach (var itemId in validItems)
                index.TryAdd(itemId, journal);
        }

        return index.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Elimina el sufijo de encantamiento @N de un ItemId.
    /// "T8_2H_GLAIVE@1" → "T8_2H_GLAIVE". Sin sufijo retorna el mismo string.
    /// </summary>
    private static string StripEnchantmentSuffix(string itemId)
    {
        var at = itemId.LastIndexOf('@');
        return at >= 0 ? itemId[..at] : itemId;
    }

    private static double? ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var r)
            ? r : null;

    // ── EnsureOn ──────────────────────────────────────────────────────────────

    private void EnsureOn()
    {
        if (State is not ServiceState.On)
            throw new InvalidOperationException(
                $"{ServiceName} no está activo (estado: {State}). Llama StartAsync primero.");
    }
}
