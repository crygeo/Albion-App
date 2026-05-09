using Albion_App.Helpers;
using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Albion_App.Components.Market;

/// <summary>
/// ViewModel de la vista de Mercado.
///
/// <para><b>Responsabilidades estrictas (post-refactor)</b>:</para>
/// <list type="bullet">
///   <item>Mantener el estado UI: filtros (texto, categoría, nivel, encantamiento)
///         y resultados (<see cref="Items"/>, <see cref="ItemCount"/>).</item>
///   <item>Reaccionar a cambios de los filtros y disparar el caso de uso con debounce.</item>
///   <item>Mapear la respuesta del caso de uso a VMs vía la fábrica.</item>
/// </list>
///
/// <para><b>Lo que YA NO hace este VM</b> (movido a <see cref="ISearchItemsUseCase"/>):</para>
/// <list type="bullet">
///   <item>Parsear el query.</item>
///   <item>Consultar el índice de localización.</item>
///   <item>Aplicar filtros estructurales y ordenar.</item>
///   <item>Decidir entre catálogo completo o subset.</item>
/// </list>
///
/// <para><b>Beneficios</b>: el VM se reduce a "input → use case → UI". El pipeline de
/// búsqueda es testeable de forma aislada (sin WPF, sin debounce, sin Dispatcher) y
/// reutilizable desde otros ViewModels, una API o un cliente MAUI.</para>
///
/// <para><b>Invariante de diseño</b>: no conoce nada de WPF (excepto la ObservableCollection
/// y el Dispatcher para publicar resultados). No accede a múltiples servicios de datos —
/// el caso de uso encapsula esas dependencias.</para>
/// </summary>
public sealed partial class MarketVm : ObservableObject
{
    // ── Dependencias ──────────────────────────────────────────────────────────

    private readonly ISearchItemsUseCase _searchItems;
    private readonly ILocalizationService _localization;
    private readonly ItemBaseVmFactory _vmFactory;

    private CancellationTokenSource? _filterCts;

    // ── Árbol de categorías (estado UI) ───────────────────────────────────────

    /// <summary>
    /// Árbol de categorías (reactivo al idioma — los <c>CategoryVm</c> se actualizan
    /// internamente al disparar <c>LanguageChanged</c>; no requiere reconstrucción).
    /// </summary>
    [ObservableProperty] private IReadOnlyList<CategoryVm> _categoryTree;

    [ObservableProperty] private string _categoryLabel = "Tipo";

    [ObservableProperty] private CategoryVm? _selectedCategory;

    // ── Filtros ───────────────────────────────────────────────────────────────

    [ObservableProperty] private string _searchText = string.Empty;

    public IReadOnlyList<FilterOptionM> LevelOptions => FilterOptionM.LevelOptions;
    public IReadOnlyList<FilterOptionM> EnchantmentOptions => FilterOptionM.EnchantmentOptions;

    [ObservableProperty] private FilterOptionM _selectedLevel;

    [ObservableProperty] private FilterOptionM _selectedEnchantment;

    // ── Resultados ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resultados de la última búsqueda. Se usa <see cref="RangeObservableCollection{T}"/>
    /// para volcar los hits con UNA sola notificación Reset (vs ~600 notifications
    /// por Clear + 300 Add con la ObservableCollection estándar).
    /// </summary>
    public RangeObservableCollection<ItemBaseVm> Items { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private int _itemCount;

    public bool IsEmpty => ItemCount == 0;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    private ItemBaseVm? _selectedItem;

    /// <summary>Útil para habilitar/deshabilitar el botón Seleccionar en el diálogo padre.</summary>
    public bool HasSelectedItem => SelectedItem is not null;

    [ObservableProperty] private bool _isLoading;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="searchItems">Caso de uso que ejecuta el pipeline completo de búsqueda.</param>
    /// <param name="localization">Sólo para el idioma activo y construir el árbol de categorías.</param>
    /// <param name="categoryDataService">Provee el árbol de categorías raw del juego.</param>
    /// <param name="vmFactory">Fábrica de VMs de ítem (encapsula los servicios necesarios).</param>
    public MarketVm(
        ISearchItemsUseCase searchItems,
        ILocalizationService localization,
        ICategoryDataService categoryDataService,
        ItemBaseVmFactory vmFactory)
    {
        _searchItems = searchItems ?? throw new ArgumentNullException(nameof(searchItems));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _vmFactory = vmFactory ?? throw new ArgumentNullException(nameof(vmFactory));

        _selectedLevel = FilterOptionM.LevelOptions[0];
        _selectedEnchantment = FilterOptionM.EnchantmentOptions[0];

        _categoryTree = BuildCategoryTree(categoryDataService, localization);

    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectCategory(CategoryVm vm)
    {
        SelectedCategory = vm;
        CategoryLabel = vm.Path;
        TriggerSearch();
    }

    [RelayCommand]
    private void ClearCategory()
    {
        SelectedCategory = null;
        CategoryLabel = "Tipo";
        TriggerSearch();
    }

    // ── Reacciones a cambios de filtro ────────────────────────────────────────

    partial void OnSearchTextChanged(string value) => TriggerSearch();
    partial void OnSelectedLevelChanged(FilterOptionM value) => TriggerSearch();
    partial void OnSelectedEnchantmentChanged(FilterOptionM value) => TriggerSearch();

    // ── Coordinación de búsqueda ──────────────────────────────────────────────

    /// <summary>
    /// Cancela cualquier búsqueda anterior y dispara una nueva con debounce.
    /// El debounce está en este VM (no en el caso de uso) porque es una
    /// preocupación de UX, no de lógica de búsqueda.
    /// </summary>
    private void TriggerSearch()
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        _ = SearchAsync(_filterCts.Token);
    }

    private async Task SearchAsync(CancellationToken ct)
    {
        // Debounce: 250 ms para evitar búsquedas redundantes durante escritura rápida.
        try
        {
            await Task.Delay(DebounceMs, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested) return;

        IsLoading = true;
        try
        {
            // Snapshot del estado UI — evita acceder a propiedades observables desde el
            // background thread del caso de uso.
            var query = new SearchItemsQuery(
                RawText: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                TierFilter: SelectedLevel.Value,
                EnchantmentFilter: SelectedEnchantment.Value,
                MinCategoryValue: SelectedCategory?.MinValue,
                MaxCategoryValue: SelectedCategory?.MaxValue,
                LanguageCode: _localization.CurrentSupportedLanguage.Code,
                MaxResults: MaxDisplayedItems);

            var result = await _searchItems.ExecuteAsync(query, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested) return;

            // Materializar los VMs antes de tocar el hilo UI — fuera del Dispatcher.
            var vms = new List<ItemBaseVm>(result.Hits.Count);
            foreach (var hit in result.Hits)
                vms.Add(_vmFactory.Create(hit, ct));

            App.Current.Dispatcher.Invoke(() =>
            {
                // ReplaceAll: una sola notificación Reset en lugar de Clear + N×Add.
                // Combinado con la virtualización del WrapPanel del ListBox, esto
                // elimina el freeze al volcar 300 ítems.
                Items.ReplaceAll(vms);
                ItemCount = Items.Count;
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }

    // ── Façade para callers externos (compatibilidad) ─────────────────────────

    /// <summary>
    /// Construye un VM por <c>itemId</c>. Se mantiene como façade para no romper
    /// el contrato de <c>ItemSearchVM</c> y <c>CalculadoraSVM</c>; internamente
    /// delega en la fábrica.
    /// </summary>
    public ItemBaseVm BuildItemVm(string itemId, CancellationToken ct = default)
        => _vmFactory.CreateById(itemId, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<CategoryVm> BuildCategoryTree(
        ICategoryDataService categoryDataService,
        ILocalizationService localization)
        => categoryDataService
            .GetRawCategories()
            .Select(c => new CategoryVm(c, localization))
            .ToArray();

    // ── Constantes ────────────────────────────────────────────────────────────

    private const int MaxDisplayedItems = 300;
    private const int DebounceMs = 250;
}