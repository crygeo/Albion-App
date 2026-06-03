using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Albion_App.Components.Item;
using Albion_App.Helpers;
using AlbionApp.Application.Interfaces;
using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
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
    private readonly IClipboardService _clipboardService;

    private CancellationTokenSource? _filterCts;

    /// <summary>
    /// Filtro de slot activo. Cuando está establecido, la búsqueda solo devuelve
    /// ítems cuyo <c>SlotType</c> coincida ("head", "mainhand", "potion", …).
    /// Se limpia al cerrar el diálogo de búsqueda.
    /// </summary>
    private SlotType? _slotTypeFilter;

    /// <summary>
    /// Establece (o limpia con <c>null</c>) el filtro de slot y relanza la búsqueda.
    /// Llamado por <see cref="ItemSearchDialogService"/> antes y después de mostrar el diálogo.
    /// </summary>
    public void SetSlotTypeFilter(SlotType? filter)
    {
        _slotTypeFilter = filter;
        TriggerSearch();
    }

    /// <summary>
    /// Aplica filtros de nivel/encantamiento por defecto antes de abrir el diálogo de búsqueda.
    /// Retorna los valores anteriores para restaurarlos al cerrar el diálogo.
    /// </summary>
    public (FilterOptionM PrevLevel, FilterOptionM PrevEnchantment) ApplyDefaultFilters(
        int? tier, int? enchantment)
    {
        var prev = (SelectedLevel, SelectedEnchantment);

        if (tier.HasValue)
        {
            var opt = LevelOptions.FirstOrDefault(o => o.Value == tier);
            if (opt is not null) SelectedLevel = opt;
        }

        if (enchantment.HasValue)
        {
            var opt = EnchantmentOptions.FirstOrDefault(o => o.Value == enchantment);
            if (opt is not null) SelectedEnchantment = opt;
        }

        return prev;
    }

    /// <summary>
    /// Restaura los filtros de nivel/encantamiento previos al cerrar el diálogo de búsqueda.
    /// </summary>
    public void RestoreDefaultFilters(FilterOptionM prevLevel, FilterOptionM prevEnchantment)
    {
        SelectedLevel       = prevLevel;
        SelectedEnchantment = prevEnchantment;
    }

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
    /// <param name="clipboardService">Servicio para copiar la imagen del item al portapapeles.</param>
    public MarketVm(
        ISearchItemsUseCase searchItems,
        ILocalizationService localization,
        ICategoryDataService categoryDataService,
        ItemBaseVmFactory vmFactory,
        IClipboardService clipboardService)
    {
        _searchItems = searchItems ?? throw new ArgumentNullException(nameof(searchItems));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _vmFactory = vmFactory ?? throw new ArgumentNullException(nameof(vmFactory));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));

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

    [RelayCommand]
    private void CopyItemImage(ItemBaseVm item)
    {
        if (item.ImageBytes is not { Length: > 0 })
            return;

        _clipboardService.SetImage(item.ImageBytes);
    }
    
    [RelayCommand]
    private void CopyItemName(ItemBaseVm item)
    {
        if (string.IsNullOrWhiteSpace(item.DisplayName))
            return;

        _clipboardService.SetText(item.DisplayName);
    }

    [RelayCommand]
    private void CopyItemId(ItemBaseVm item)
    {
        if (string.IsNullOrWhiteSpace(item.ItemId))
            return;

        _clipboardService.SetText(item.ItemId);
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
                MaxResults: MaxDisplayedItems,
                SlotTypeFilter: _slotTypeFilter);

            // Stream: el use case prepara y ordena en background, luego emite cada
            // hit a medida que se proyecta. Acumulamos en lotes pequeños y volcamos
            // al UI para que el primer batch sea visible casi inmediatamente y el
            // resto fluya sin bloquear el Dispatcher.
            var batch = new List<ItemBaseVm>(BatchFlushSize);
            var firstFlush = true;
            var totalAdded = 0;

            await foreach (var hit in _searchItems.StreamAsync(query, ct).ConfigureAwait(false))
            {
                // VM creation puede correr en background — no toca dependencias UI.
                // La carga de imagen es fire-and-forget bajo el mismo CT.
                batch.Add(_vmFactory.Create(hit, ct));

                if (batch.Count >= BatchFlushSize)
                {
                    totalAdded += batch.Count;
                    await FlushBatchAsync(batch, firstFlush, totalAdded, ct).ConfigureAwait(false);
                    firstFlush = false;
                    batch = new List<ItemBaseVm>(BatchFlushSize);
                }
            }

            if (ct.IsCancellationRequested) return;

            // Flush del último lote (puede estar vacío si MaxResults % BatchFlushSize == 0).
            if (batch.Count > 0)
            {
                totalAdded += batch.Count;
                await FlushBatchAsync(batch, firstFlush, totalAdded, ct).ConfigureAwait(false);
            }
            else if (firstFlush)
            {
                // Stream sin resultados: limpiar resultados anteriores en la UI.
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    Items.ReplaceAll([]);
                    ItemCount = 0;
                }).Task.ConfigureAwait(false);
            }
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

    /// <summary>
    /// Vuelca un lote de VMs al UI thread.
    /// <list type="bullet">
    ///   <item>Primer flush → <see cref="RangeObservableCollection{T}.ReplaceAll"/>:
    ///         una sola notificación Reset que reemplaza los resultados anteriores.</item>
    ///   <item>Flushes posteriores → <c>Add</c> por ítem: el panel virtualizado los
    ///         materializa incrementalmente sin reconstruir contenedores existentes
    ///         (un Reset aquí forzaría un rebuild de los visibles cada batch).</item>
    /// </list>
    /// </summary>
    private async Task FlushBatchAsync(
        List<ItemBaseVm> batch,
        bool isFirstFlush,
        int total,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        await App.Current.Dispatcher.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;

            if (isFirstFlush)
            {
                Items.ReplaceAll(batch);
            }
            else
            {
                foreach (var vm in batch)
                    Items.Add(vm);
            }

            ItemCount = total;
        }).Task.ConfigureAwait(false);
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
    private const int DebounceMs = 150;

    /// <summary>
    /// Tamaño de lote para el streaming. Trade-off:
    /// <list type="bullet">
    ///   <item>Muy chico (5-10): primer paint instantáneo pero más round-trips
    ///         al Dispatcher → overhead acumulado.</item>
    ///   <item>Muy grande (100+): menos round-trips pero el primer paint tarda
    ///         más y se nota un mini-freeze por batch.</item>
    ///   <item>30 es el sweet-spot empírico: cabe en una pantalla típica de
    ///         tiles 64×64, así que el primer batch ya cubre lo visible.</item>
    /// </list>
    /// </summary>
    private const int BatchFlushSize = 5;
}