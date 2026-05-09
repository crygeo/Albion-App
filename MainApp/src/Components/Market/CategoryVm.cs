using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App.Components.Market;

public sealed partial class CategoryVm
    : ObservableObject,
        ICategory,
        IDisposable
{
    private readonly Category _category;
    private readonly ILocalizationService _localization;
    private readonly CategoryVm? _selfNode; // nodo virtual prepended; null si es hoja o es self-node
    private readonly bool _isSelfNode;
    
    // ── Estado observable ────────────────────────────────────

    [ObservableProperty] private string _displayName;

    [ObservableProperty] private CategoryVm? _parent;

    public string FullPath => GetFullPath();
    public string Path => GetPath();

    // ── Constructor público ───────────────────────────────────

    public CategoryVm(
        Category category,
        ILocalizationService localization,
        CategoryVm? parent = null)
    {
        _category = category;
        _localization = localization;
        _displayName = ResolveDisplayName();
        _isSelfNode = false;
        
        _parent = parent;

        // Construir hijos tipados una sola vez.
        var typedChildren = _category.Children
            .Select(x => new CategoryVm(x, localization, this))
            .ToArray();

        // ICategory contract (covarianza IReadOnlyList<out T> permite asignación directa).
        Children = typedChildren;

        if (typedChildren.Length > 0)
        {
            // Nodo intermedio: prepend un "self-select" leaf con label "Todos".
            // WPF MenuItem con ItemsSource vacío NO abre submenú → el Command se dispara.
            _selfNode = new CategoryVm(this);
            MenuItems = typedChildren.Prepend(_selfNode).ToArray();
        }
        else
        {
            // Nodo hoja: MenuItems vacío → el propio MenuItem actúa como leaf.
            _selfNode = null;
            MenuItems = typedChildren;
        }

        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Constructor privado para el nodo virtual "seleccionar este nivel".
    /// No suscribe <see cref="ILocalizationService.LanguageChanged"/> porque su label
    /// es fijo ("Todos") y no necesita actualización de idioma.
    /// </summary>
    private CategoryVm(CategoryVm source)
    {
        _category     = source._category;
        _localization = source._localization;

        _displayName = "Todos";

        _parent     = source;
        _isSelfNode = true;

        _selfNode = null;

        Children  = [];
        MenuItems = [];
    }


    // ── ICategory ────────────────────────────────────────────

    public string Id => _category.Id;

    public string? LocalizationId => _category.LocalizationId;

    public int SortValue => _category.SortValue;

    /// <summary>
    /// Profundidad del nodo en el árbol de categorías, calculada por el parser.
    /// 0 = raíz, 1 = ShopSubCategory1, 2 = ShopSubCategory2, 3 = ShopSubCategory3.
    /// </summary>
    public int Depth => _category.Depth;

    /// <summary>
    /// Límite inferior del rango de filtrado: igual a <c>Category.AccumulatedValue</c>.
    /// Filtro: <c>item.CategoryValue &gt;= MinValue &amp;&amp; item.CategoryValue &lt;= MaxValue</c>.
    /// </summary>
    public int MinValue => _category.AccumulatedValue;

    /// <summary>
    /// Límite superior del rango de filtrado: igual a <c>Category.MaxDescendantValue</c>.
    /// Incluye todos los descendientes de este nodo.
    /// </summary>
    public int MaxValue => _category.MaxDescendantValue;

    /// <summary>ICategory contract — árbol de datos sin nodo virtual.</summary>
    public IReadOnlyList<ICategory> Children { get; }

    /// <summary>
    /// Ítems del submenú WPF.
    /// Para nodos intermedios, incluye un <em>self-select leaf</em> ("Todos") como
    /// primer elemento, permitiendo seleccionar el nivel actual sin bajar al hoja.
    /// Para nodos hoja, es una lista vacía (el MenuItem no abre submenú).
    /// </summary>
    public IReadOnlyList<CategoryVm> MenuItems { get; }

    // ── Helpers ──────────────────────────────────────────────

    private string ResolveDisplayName()
    {
        var name = _localization.GetText(_category.LocalizationId ?? _category.Id);

        return _isSelfNode
            ? $"{name}"
            : name;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => DisplayName = ResolveDisplayName();

    // ── Cleanup ──────────────────────────────────────────────

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;

        // El self-node no suscribió LanguageChanged, pero Dispose es idempotente.
        _selfNode?.Dispose();

        foreach (var child in Children.OfType<CategoryVm>())
            child.Dispose();
    }

    
    private string GetFullPath()
    {
        var parts = new List<string>();

        CategoryVm? current = _isSelfNode
            ? Parent
            : this;

        while (current is not null)
        {
            parts.Add(current.DisplayName);
            current = current.Parent;
        }

        parts.Reverse();

        return string.Join(" > ", parts);
    }
    // ── Path simple ─────────────────────────────────────────────

    private string GetPath()
    {
        // El self-node representa la categoría padre.
        return _isSelfNode
            ? Parent?.DisplayName ?? DisplayName
            : DisplayName;
    }
}