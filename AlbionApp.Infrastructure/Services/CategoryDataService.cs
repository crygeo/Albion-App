using System.Xml.Linq;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
using LibAlbionData;
using LibAlbionData.Core;
using LibServices;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Servicio que parsea las categorías de tienda de Albion Online directamente desde
/// el XDocument de items.bin y construye el árbol de navegación localizado.
///
/// Responsabilidades:
///   1. <c>OnStartAsync</c>: obtiene <see cref="GameDataPath.Items"/> de <see cref="AlbionData"/>,
///      parsea la sección <c>&lt;shopcategories&gt;</c> en background y almacena
///      el árbol de <see cref="Category"/> en <c>_rawCategories</c> (inmutables).
///   2. Reacción a <see cref="ILocalizationService.LanguageChanged"/>: reconstruye el
///      árbol localizado sin IO adicional — los datos ya están en memoria.
///
/// El árbol expuesto (<see cref="GetRootNodes"/>) es un <see cref="IReadOnlyList{ShopCategoryNode}"/>
/// listo para bindear en un HierarchicalDataTemplate de WPF.
///
/// Thread-safety:
///   <c>_rootNodes</c> es volatile — los lectores siempre ven el valor más reciente
///   tras una reconstrucción desde cualquier hilo.
///   <see cref="TreeChanged"/> puede dispararse desde hilos de thread pool.
///
/// No depende de LibAlbionData.Models.AlbionShopCategory — parsea directamente
/// en <see cref="Category"/> del dominio de la aplicación.
/// </summary>
public sealed class CategoryDataService(AlbionData albionData) : ServiceBase, ICategoryDataService
{
    // Categorías crudas del juego — inmutables tras OnStartAsync.
    private IReadOnlyList<Category> _rawCategories = [];


    // ── Evento con replay semántico ───────────────────────────────────────────

    private EventHandler? _treeChanged;

    /// <summary>
    /// Notifica que el árbol fue reconstruido (carga inicial o cambio de idioma).
    ///
    /// Implementa replay: si el árbol ya está disponible cuando alguien se suscribe,
    /// el handler se invoca inmediatamente para evitar race conditions entre la carga
    /// asíncrona y la suscripción tardía del ViewModel.
    /// </summary>
    public event EventHandler? TreeChanged
    {
        add
        {
            _treeChanged += value;
            if (_rawCategories.Count > 0)
                value?.Invoke(this, EventArgs.Empty);
        }
        remove => _treeChanged -= value;
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna las categorías crudas del juego (árbol de <see cref="Category"/>)
    /// sin localización — los nombres son IDs de localización, no texto legible.
    /// Lista vacía hasta que el servicio esté en estado On.
    /// Consumido por <c>ShopCategoryTreeProvider</c> para construir el árbol localizado.
    /// </summary>
    public IReadOnlyList<Category> GetRawCategories() => _rawCategories;

    // ── IAlbionService ────────────────────────────────────────────────────────

    public override string ServiceName => "CategoryDataService";

    
    // ── AlbionServiceBase: ciclo de vida ──────────────────────────────────────

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        SetProgress(0);

        var doc = albionData.GetXDocument(GameDataPath.Items);

        SetProgress(10);

        // Parseo en background: la sección shopcategories puede contener cientos de nodos.
        // Evitamos bloquear el hilo llamante (típicamente el hilo UI de WPF).
        _rawCategories = await Task
            .Run(() => ParseCategories(doc, ct), ct)
            .ConfigureAwait(false);


        SetProgress(100);
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        _rawCategories = [];
        return Task.CompletedTask;
    }

    // ── Parser XML → ItemCategory ─────────────────────────────────────────────

    /// <summary>
    /// Parsea la sección <c>&lt;shopcategories&gt;</c> del XDocument de items.bin
    /// y produce un árbol de <see cref="Category"/> ordenado por SortValue.
    ///
    /// Estructura del XML:
    /// <code>
    ///   &lt;items&gt;
    ///     &lt;shopcategories&gt;
    ///       &lt;shopcategory id="weapons" value="1000" displayname="@...SHOPCATEGORY_WEAPONS"&gt;
    ///         &lt;shopsubcategory id="bow" value="0" displayname="@...SHOPSUBCATEGORY_BOW"&gt;
    ///           &lt;shopsubcategory2 id="bow_bow" value="0" .../&gt;
    ///         &lt;/shopsubcategory&gt;
    ///       &lt;/shopcategory&gt;
    ///     &lt;/shopcategories&gt;
    ///     ...
    ///   &lt;/items&gt;
    /// </code>
    ///
    /// Estrategia de búsqueda robusta:
    ///   1. Primero busca <c>&lt;shopcategories&gt;</c> como contenedor explícito.
    ///   2. Fallback: busca <c>&lt;shopcategory&gt;</c> como hijos directos del root.
    ///   Las comparaciones son case-insensitive para robustez ante cambios del juego.
    /// </summary>
    private static IReadOnlyList<Category> ParseCategories(XDocument doc, CancellationToken ct)
    {
        var root = doc.Root;
        if (root is null) return [];

        // Estrategia 1: contenedor <shopcategories> explícito
        var container = root.Elements()
            .FirstOrDefault(e => IsNamed(e, "shopcategories"));

        IEnumerable<XElement> categoryElements;

        if (container is not null)
        {
            categoryElements = container.Elements()
                .Where(e => IsNamed(e, "shopcategory"));
        }
        else
        {
            // Estrategia 2: <shopcategory> directamente bajo root
            categoryElements = root.Elements()
                .Where(e => IsNamed(e, "shopcategory"));
        }

        var rootCategories = categoryElements
            .Select(el => { ct.ThrowIfCancellationRequested(); return ParseNode(el, depth: 0, parentAccumulated: 0); })
            .OrderBy(c => c.SortValue)
            .ToList()
            .AsReadOnly();

        return rootCategories;
    }

    /// <summary>
    /// Parsea un nodo de categoría recursivamente calculando los valores acumulados.
    ///
    /// Nomenclatura de elementos hijo por profundidad:
    ///   depth 0 → <c>shopsubcategory</c>   (sin sufijo numérico)
    ///   depth 1 → <c>shopsubcategory2</c>
    ///   depth N → <c>shopsubcategoryN+1</c>
    ///
    /// <see cref="Category.AccumulatedValue"/> = parentAccumulated + propio value del XML.
    /// <see cref="Category.MaxDescendantValue"/> = máximo AccumulatedValue entre todos
    /// los descendientes (o AccumulatedValue propio si es hoja).
    /// Ambos se usan para filtrado por rango sobre <see cref="ItemBase.CategoryValue"/>.
    /// </summary>
    private static Category ParseNode(XElement el, int depth, int parentAccumulated)
    {
        var id   = (string?)el.Attribute("id") ?? string.Empty;
        var sort = int.TryParse((string?)el.Attribute("value"), out var v) ? v : 0;

        // La clave TMX se construye desde el id según la profundidad del nodo.
        // depth 0 → @MARKETPLACEGUI_ROLLOUT_SHOPCATEGORY_{ID}
        // depth > 0 → @MARKETPLACEGUI_ROLLOUT_SHOPSUBCATEGORY_{ID}
        var prefix = depth == 0
            ? "@MARKETPLACEGUI_ROLLOUT_SHOPCATEGORY_"
            : "@MARKETPLACEGUI_ROLLOUT_SHOPSUBCATEGORY_";
        var locId = string.IsNullOrEmpty(id) ? null : $"{prefix}{id.ToUpperInvariant()}";

        var ownValue    = sort; // "value" en el XML sirve también como magnitud de categoría
        var accumulated = parentAccumulated + ownValue;

        var childTagName = depth == 0 ? "shopsubcategory" : $"shopsubcategory{depth + 1}";

        var children = el.Elements()
            .Where(c => IsNamed(c, childTagName))
            .Select(c => ParseNode(c, depth + 1, accumulated))
            .OrderBy(c => c.SortValue)
            .ToList()
            .AsReadOnly();

        var maxDescendant = children.Count > 0
            ? children.Max(c => c.MaxDescendantValue)
            : accumulated;

        return new Category(
            Id:                 id,
            LocalizationId:     locId,
            SortValue:          sort,
            Depth:              depth,
            Value:              ownValue,
            AccumulatedValue:   accumulated,
            MaxDescendantValue: maxDescendant,
            Children:           children);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsNamed(XElement el, string localName)
        => el.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);


}
