using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Albion_App.Infrastructure;
using Albion_App.Models;
using Albion_App.ViewModel;
using Albion_App.Components.Achievement;
using AlbionApp.Application.UseCases.Crafting;
using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;
using AlbionApp.Domain.Market;
using LibServices.AppConfig;
using LibServices.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App._1Calculadora;

/// <summary>
/// ViewModel de la Calculadora de Crafteo.
///
/// Responsabilidades:
///   • Mantener el estado observable de las 4 cards.
///   • Orquestar los flujos de usuario (buscar ítem, navegar recetas, cambiar parámetros).
///   • Construir el <see cref="CraftingCostRequest"/> y delegar en
///     <see cref="CalculateCraftingCostUseCase"/> toda la lógica de negocio.
///   • Mapear el <see cref="CraftingCostResult"/> al estado UI sin lógica de dominio.
///
/// Decisiones de diseño:
///   • Sin <c>OnPropertyChanged(nameof(...))</c> manuales — toda dependencia es
///     declarativa vía <c>[NotifyPropertyChangedFor]</c> o <c>partial void OnXChanged</c>.
///   • Los métodos de carga de recetas / inventario son side-effect free respecto
///     al cálculo: cada uno hace UNA cosa y llama explícitamente a los siguientes.
///   • <see cref="IItemSearchService"/> desacopla el VM de la View concreta del diálogo.
/// </summary>
public sealed partial class CalculadoraSvm : ObservableObject, ISectionIcons
{
    // ═══════════════════════════════════════════════════════════════════════════
    // DEPENDENCIAS
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly IItemSearchService            _itemSearch;
    private readonly IItemDataService              _itemDataService;
    private readonly ProcessPlayerUseCase          _processPlayer;
    private readonly ILocalizationService          _localization;
    private readonly ICraftingLocationService      _craftingLocations;
    private readonly CalculateCraftingCostUseCase  _calculateCrafting;
    private readonly CalculateByQuantityUseCase    _calculateByQuantity;
    private readonly AppConfigService              _appConfig;
    private readonly IPersistenceStore             _store;
    private readonly IPriceService                 _priceService;

    // Bonuses raw del ítem: fuente para achievement focus y return rate.
    private IReadOnlyList<AggregatedBonus> _rawItemBonuses = [];

    // CancellationTokenSource para imágenes de ingredientes en vuelo.
    private CancellationTokenSource? _ingredientCts;

    // Recetas raw del ítem seleccionado — para cálculo de fama e índice por RecipeIndex.
    private IRecipe[] _rawRecipes = [];

    /// <summary>
    /// Cuando es true, los OnXxxChanged no escriben al store de persistencia.
    /// Se activa durante la restauración del workspace para evitar que una pestaña
    /// sobreescriba los valores de otra.
    /// </summary>
    private bool _suppressSave;

    // ═══════════════════════════════════════════════════════════════════════════
    // NAVEGACIÓN (sidebar)
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty] private string       _header = "Calculadora";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.Calculator;

    // ═══════════════════════════════════════════════════════════════════════════
    // CARD 1 — Ítem objetivo
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(HasSelectedItemWithoutRecipe))]
    [NotifyPropertyChangedFor(nameof(RecipeCarouselLabel))]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private ItemBaseVm? _selectedItem;

    /// <summary>Título de la pestaña en el workspace. Usado por <see cref="WorkspaceVm"/>.</summary>
    public string TabTitle => SelectedItem?.DisplayName ?? "Nueva pestaña";

    public bool HasSelectedItem => SelectedItem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItemBonuses))]
    private IReadOnlyList<BonusDisplayItem> _itemBonuses = [];

    public bool HasItemBonuses => ItemBonuses.Count > 0;

    // ═══════════════════════════════════════════════════════════════════════════
    // CARD 2 — Carrusel de recetas + parámetros de crafteo
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Carrusel ──────────────────────────────────────────────────────────────

    public ObservableCollection<RecipeVm> RecipeOptions { get; } = [];

    /// <summary>
    /// Índice de la receta visible. CommunityToolkit genera notificaciones para
    /// todas las propiedades computadas que dependen de él.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentRecipe))]
    [NotifyPropertyChangedFor(nameof(PreviousRecipeM))]
    [NotifyPropertyChangedFor(nameof(NextRecipeM))]
    [NotifyPropertyChangedFor(nameof(HasPreviousRecipe))]
    [NotifyPropertyChangedFor(nameof(HasNextRecipe))]
    [NotifyPropertyChangedFor(nameof(RecipeCarouselLabel))]
    [NotifyPropertyChangedFor(nameof(IsTransmutation))]
    [NotifyPropertyChangedFor(nameof(IsCraftingRecipe))]
    private int _recipeIndex;

    public bool      HasRecipes    => RecipeOptions.Count > 0;
    public bool      HasMultipleRecipes          => RecipeOptions.Count > 1;
    public bool      HasSelectedItemWithoutRecipe => HasSelectedItem && !HasRecipes;
    public bool      HasPreviousRecipe            => HasMultipleRecipes;
    public bool      HasNextRecipe                => HasMultipleRecipes;
    public RecipeVm? CurrentRecipe    => RecipeOptions.Count > 0 ? RecipeOptions[RecipeIndex] : null;
    public bool      IsTransmutation  => CurrentRecipe?.IsTransmutation ?? false;
    public bool      IsCraftingRecipe => !IsTransmutation;
    public RecipeVm? PreviousRecipeM => RecipeOptions.Count > 1 ? RecipeOptions[RecipeIndex > 0 ? RecipeIndex - 1 : RecipeOptions.Count - 1] : null;
    public RecipeVm? NextRecipeM => RecipeOptions.Count > 1 ? RecipeOptions[RecipeIndex < RecipeOptions.Count - 1 ? RecipeIndex + 1 : 0] : null;
    public string RecipeCarouselLabel => RecipeOptions.Count > 0 ? $"Receta ({RecipeIndex + 1}/{RecipeOptions.Count})" : "—";

    // ── Ciudad y parámetros ───────────────────────────────────────────────────

    [ObservableProperty] private IReadOnlyList<CraftingCityOption> _availableCities = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCity))]
    [NotifyPropertyChangedFor(nameof(IsHideoutCity))]
    private CraftingCityOption? _selectedCityOption;

    public bool HasSelectedCity => SelectedCityOption is not null;

    /// <summary>True cuando la ciudad seleccionada es un hideout o black zone con bono.</summary>
    public bool IsHideoutCity =>
        HideoutPowerlevel.IsHideoutType(SelectedCityOption?.ClusterType);

    /// <summary>Niveles disponibles para el hideout (1–9).</summary>
    public static IReadOnlyList<HideoutPowerlevel> HideoutLevels => HideoutPowerlevel.All;

    /// <summary>Nivel de poder del hideout seleccionado (1–9).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideoutBonusLabel))]
    [NotifyPropertyChangedFor(nameof(HideoutFocusSaving))]
    [NotifyPropertyChangedFor(nameof(HasHideoutBadge))]
    private HideoutPowerlevel _selectedHideoutLevel = HideoutPowerlevel.ForLevel(1);

    /// <summary>
    /// True = el hideout está especializado para el ítem craftado.
    /// Activa la REDUCCIÓN DE FOCO adicional (specialistcraftingbonus).
    /// El bono al Return Rate (generalistcraftingbonus) siempre aplica.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideoutFocusSaving))]
    [NotifyPropertyChangedFor(nameof(HideoutBonusLabel))]
    [Persist(DefaultValue = false)]
    private bool _useSpecialistBonus;

    /// <summary>
    /// Etiqueta del badge de hideout en Card 4.
    /// Formato: "H L-9  26%  -3,000"
    /// </summary>
    public string HideoutBonusLabel
    {
        get
        {
            var level = SelectedHideoutLevel;
            var pct   = $"+{level.GeneralistBonus * 100:F0}%";
            if (UseSpecialistBonus && HideoutFocusSaving > 0)
                return $"H L-{level.Level}  {pct}  -{HideoutFocusSaving:N0} foco";
            return $"H L-{level.Level}  {pct}";
        }
    }


    /// <summary>
    /// Ahorro REAL de foco por craft gracias al bono especialista del hideout.
    /// Calculado por el UseCase: floor(base × 0.5^db) - floor(base × 0.5^(db+specialist)).
    /// 0 cuando no hay bono especialista activo.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideoutBonusLabel))]
    private int _hideoutFocusSaving;

    /// <summary>True cuando hay resultados y la ciudad es un hideout → mostrar badge.</summary>
    public bool HasHideoutBadge => IsHideoutCity && HasResults;

    /// <summary>
    /// Bono generalista al Return Rate — SIEMPRE aplica cuando se craftea en hideout.
    /// El especialista es ADICIONAL (reducción de foco) y se maneja por separado.
    /// </summary>
    private decimal CurrentHideoutBonus =>
        IsHideoutCity ? SelectedHideoutLevel.GeneralistBonus : 0m;

    public IReadOnlyList<JournalBonusOptionM> JournalBonusOptions { get; } =
    [
        new(0.00m, "0% — Sin bono de diario"),
        new(0.10m, "10% — Diario de rendimiento"),
        new(0.20m, "20% — Diario premium"),
    ];

    [ObservableProperty] private JournalBonusOptionM? _selectedJournalBonus;

    [ObservableProperty][Persist(DefaultValue = false)]
    private bool _usePremium;

    [ObservableProperty][Persist(DefaultValue = false)]
    private bool _useFocus;

    /// <summary>
    /// Plata por transmutación editable por el usuario.
    /// Pre-rellena con <see cref="RecipeVm.Silver"/> al cambiar de receta,
    /// pero el valor real lo da el juego, así que el usuario lo puede ajustar.
    /// </summary>
    [ObservableProperty]
    private decimal _transmutationSilverPerUnit;


    // ═══════════════════════════════════════════════════════════════════════════
    // CARD 3 — Cantidades e inventario
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIngredientInventory))]
    private int _quantityToCraft = 1;

    public ObservableCollection<IngredientInventoryM> IngredientInventory { get; } = [];
    public bool HasIngredientInventory => IngredientInventory.Count > 0;

    /// <summary>Fila del ítem a craftear (precio de venta).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCraftItemRow))]
    [NotifyPropertyChangedFor(nameof(ProfitLoss))]
    [NotifyPropertyChangedFor(nameof(IsProfitable))]
    [NotifyPropertyChangedFor(nameof(CraftedItemTotalValue))]
    private CraftItemRowM? _craftItemRow;

    public bool HasCraftItemRow => CraftItemRow is not null;

    /// <summary>Fila del libro de laborer (si el ítem genera fama para libro).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJournal))]
    private JournalRowM? _journalRow;

    public bool HasJournal => JournalRow is not null;

    // ═══════════════════════════════════════════════════════════════════════════
    // CARD 4 — Resultados
    // ═══════════════════════════════════════════════════════════════════════════

    public ObservableCollection<MaterialResultM> MaterialResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private decimal _totalCost;

    public bool HasResults => MaterialResults.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReturnRateLabel))]
    private decimal _effectiveReturnRate;

    public string ReturnRateLabel => $"{EffectiveReturnRate * 100:F1}%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFocusCost))]
    [NotifyPropertyChangedFor(nameof(FocusPerCraftLabel))]
    [NotifyPropertyChangedFor(nameof(HideoutFocusSaving))]
    [NotifyPropertyChangedFor(nameof(HideoutBonusLabel))]
    private int? _focusPerCraft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFocusCost))]
    [NotifyPropertyChangedFor(nameof(TotalFocusCostLabel))]
    private int? _totalFocusCost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusReductionLabel))]
    private int _focusReductionPercent;

    /// <summary>Etiqueta del ahorro por Destiny Board. Ej: "(−92% DB)".</summary>
    public string FocusReductionLabel => $"(−{FocusReductionPercent}% DB)";

    public bool   HasFocusCost       => UseFocus && FocusPerCraft > 0;
    public string FocusPerCraftLabel  => FocusPerCraft  is int f ? f.ToString("N0") : "—";
    public string TotalFocusCostLabel => TotalFocusCost is int t ? t.ToString("N0") : "—";

    // ── Sobrantes (materiales retornados) ─────────────────────────────────────

    [ObservableProperty] private decimal _totalReturnedValue;

    // ── Fama ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFame))]
    private double _totalFameBase;

    [ObservableProperty] private double _totalFamePremium;

    public bool HasFame => TotalFameBase > 0;

    // ── Rentabilidad ─────────────────────────────────────────────────────────

    /// <summary>Valor total de los ítems crafteados = precio × cantidad.</summary>
    public decimal CraftedItemTotalValue =>
        (CraftItemRow?.UnitPrice ?? 0m) * QuantityToCraft;

    /// <summary>
    /// Inversión total = capital inicial mínimo de materiales + libros vacíos.
    /// TotalCost ya refleja el mínimo a comprar (no el bruto).
    /// </summary>
    public decimal TotalInvestment => TotalCost + (JournalRow?.TotalEmptyValue ?? 0m);

    /// <summary>
    /// Lo que gano = venta de ítems + materiales retornados + libros llenos vendidos.
    /// </summary>
    public decimal TotalGain =>
        CraftedItemTotalValue + TotalReturnedValue + (JournalRow?.TotalFullValue ?? 0m);

    /// <summary>Ganancia neta = lo que gano − lo que invertí (en bruto).</summary>
    public decimal ProfitLoss => TotalGain - TotalInvestment;

    public bool IsProfitable => ProfitLoss > 0;

    // ── Comparación con/sin foco ─────────────────────────────────────────────

    [ObservableProperty] private decimal _roi;
    [ObservableProperty] private decimal _margin;
    [ObservableProperty] private decimal _profitLossNoFocus;
    [ObservableProperty] private decimal _roiNoFocus;

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public CalculadoraSvm(
        IItemSearchService            itemSearch,
        IItemDataService              itemDataService,
        ProcessPlayerUseCase          processPlayer,
        ILocalizationService          localization,
        ICraftingLocationService      craftingLocations,
        CalculateCraftingCostUseCase  calculateCrafting,
        CalculateByQuantityUseCase    calculateByQuantity,
        AppConfigService              appConfig,
        IPersistenceStore             store,
        IPriceService                 priceService)
    {
        _itemSearch          = itemSearch;
        _itemDataService     = itemDataService;
        _processPlayer       = processPlayer;
        _localization        = localization;
        _craftingLocations   = craftingLocations;
        _calculateCrafting   = calculateCrafting;
        _calculateByQuantity = calculateByQuantity;
        _appConfig           = appConfig;
        _store               = store;
        _priceService        = priceService;

        // Suscripciones a colecciones
        RecipeOptions.CollectionChanged      += OnRecipeCollectionChanged;
        IngredientInventory.CollectionChanged += OnIngredientCollectionChanged;
        MaterialResults.CollectionChanged    += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasHideoutBadge));
            ExportarPngCommand.NotifyCanExecuteChanged();
        };

        LoadSettings();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EXPORT PNG
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// La vista se suscribe a este evento y renderiza el elemento visual a PNG.
    /// Patrón: VM dispara la intención, code-behind ejecuta el render (requiere árbol visual).
    /// </summary>
    public event Action? ExportRequested;

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void ExportarPng() => ExportRequested?.Invoke();

    // ═══════════════════════════════════════════════════════════════════════════
    // COMANDOS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Abre el diálogo de búsqueda y carga el ítem seleccionado.</summary>
    [RelayCommand]
    private async Task BuscarItemAsync()
    {
        var itemVm = await _itemSearch.SearchAsync();
        if (itemVm is null) return;
        await LoadItemAsync(itemVm);
    }

    /// <summary>Limpia el ítem y resetea toda la calculadora.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void LimpiarItem()
    {
        CancelIngredientImageLoads();

        SelectedItem = null;
        _rawItemBonuses = [];
        _rawRecipes = [];
        ItemBonuses = [];

        ClearCalculationState();
        ResetCiudades();

        RecipeOptions.Clear();
        RecipeIndex = 0;
        IngredientInventory.Clear();
        MaterialResults.Clear();
        CraftItemRow = null;
        JournalRow = null;
        TotalCost = 0;
        TotalReturnedValue = 0;
        TotalFameBase = 0;
        TotalFamePremium = 0;
        Roi               = 0m;
        Margin            = 0m;
        ProfitLossNoFocus = 0m;
        RoiNoFocus        = 0m;
    }

    /// <summary>Navega a la receta anterior (wrap-around).</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleRecipes))]
    private void PreviousRecipe()
    {
        RecipeIndex = RecipeIndex > 0 ? RecipeIndex - 1 : RecipeOptions.Count - 1;
        RebuildIngredientInventory();
    }

    /// <summary>Navega a la receta siguiente (wrap-around).</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleRecipes))]
    private void NextRecipe()
    {
        RecipeIndex = RecipeIndex < RecipeOptions.Count - 1 ? RecipeIndex + 1 : 0;
        RebuildIngredientInventory();
    }

    /// <summary>Carga el inventario del jugador desde el protocolo del juego.</summary>
    [RelayCommand]
    private void CargarRecursos()
    {
        // TODO: integrar con LibAlbionProtocol → IPlayerInventoryService.
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PARTIAL METHODS — árbol de dependencias declarativo
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Cuando RecipeIndex cambia, actualiza CanExecute de las flechas y resetea el silver de transmutación.</summary>
    partial void OnRecipeIndexChanged(int value)
    {
        PreviousRecipeCommand.NotifyCanExecuteChanged();
        NextRecipeCommand.NotifyCanExecuteChanged();
        TransmutationSilverPerUnit = CurrentRecipe?.Silver ?? 0m;
    }

    /// <summary>Cuando cambia la cantidad objetivo, reconstruye el inventario y recalcula.</summary>
    partial void OnQuantityToCraftChanged(int value) => RebuildIngredientInventory();

    /// <summary>Ciudad — per-tab, guarda en store solo si no estamos restaurando.</summary>
    partial void OnSelectedCityOptionChanged(CraftingCityOption? value)
    {
        if (!_suppressSave) _store.Set("city_cluster_id", value?.ClusterId);
        Recalculate();
    }

    /// <summary>Bono de diario — per-tab.</summary>
    partial void OnSelectedJournalBonusChanged(JournalBonusOptionM? value)
    {
        if (!_suppressSave) _store.Set("journal_bonus", value?.Value ?? 0m);
        Recalculate();
    }

    /// <summary>Nivel de hideout — per-tab.</summary>
    partial void OnSelectedHideoutLevelChanged(HideoutPowerlevel value)
    {
        if (!_suppressSave) _store.Set("hideout_level", value.Level);
        Recalculate();
    }

    /// <summary>Especialista — per-tab, no persiste al store global.</summary>
    partial void OnUseSpecialistBonusChanged(bool value) => Recalculate();

    /// <summary>Foco — per-tab, no persiste al store global.</summary>
    partial void OnTransmutationSilverPerUnitChanged(decimal value)
    {
        // No puede bajar del valor base del XML.
        var minSilver = CurrentRecipe?.Silver ?? 0m;
        if (value < minSilver)
        {
            TransmutationSilverPerUnit = minSilver; // dispara el setter de nuevo con el valor correcto
            return;
        }
        Recalculate();
    }

    partial void OnUseFocusChanged(bool value)
    {
        OnPropertyChanged(nameof(HasFocusCost));
        Recalculate();
    }

    /// <summary>Premium — GLOBAL, persiste al store compartido.</summary>
    partial void OnUsePremiumChanged(bool value)
    {
        OnPropertyChanged(nameof(TotalFamePremium));
        if (!_suppressSave) SaveSettings(); // UsePremium → store global
    }

    /// <summary>Cuando cambia el precio del ítem crafteado, re-notifica rentabilidad.</summary>
    partial void OnCraftItemRowChanged(CraftItemRowM? value)
    {
        if (value is not null)
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CraftItemRowM.UnitPrice))
                {
                    OnPropertyChanged(nameof(CraftedItemTotalValue));
                    OnPropertyChanged(nameof(TotalGain));
                    OnPropertyChanged(nameof(ProfitLoss));
                    OnPropertyChanged(nameof(IsProfitable));
                }
            };
    }


    // ═══════════════════════════════════════════════════════════════════════════
    // FLUJO DE SELECCIÓN DE ÍTEM
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Carga el ítem seleccionado: recetas → bonuses → ciudades.
    /// Cada paso es independiente; el cálculo se dispara al final desde
    /// <see cref="RebuildIngredientInventory"/>.
    /// </summary>
    /// <summary>
    /// Carga un ítem directamente desde fuera (ej. restauración de workspace).
    /// </summary>
    internal Task LoadItemExternalAsync(ItemBaseVm itemVm) => LoadItemAsync(itemVm);

    private async Task LoadItemAsync(ItemBaseVm itemVm)
    {
        SelectedItem = itemVm;
        LoadRecipes(itemVm);
        LoadItemBonuses(itemVm);
        UpdateCitiesForItem(itemVm);
        LimpiarItemCommand.NotifyCanExecuteChanged();
        await Task.CompletedTask; // punto de extensión para carga async futura
    }

    private void LoadRecipes(ItemBaseVm itemVm)
    {
        CancelIngredientImageLoads();
        RecipeOptions.Clear();
        RecipeIndex = 0;
        TransmutationSilverPerUnit = 0; // reset siempre — OnRecipeIndexChanged no se dispara si RecipeIndex ya era 0

        var rawRecipes = _itemDataService.GetRecipes(itemVm.ItemId);
        _rawRecipes = rawRecipes;
        if (rawRecipes.Length == 0) return;

        _ingredientCts = new CancellationTokenSource();
        var ct = _ingredientCts.Token;

        for (int i = 0; i < rawRecipes.Length; i++)
        {
            var raw         = rawRecipes[i];
            var ingredients = raw.Ingredients
                .Select(ing => new IngredientVm
                {
                    Item                 = BuildIngredientVm(ing.ItemId, ct),
                    Count                = ing.Count,
                    ParticipatesInReturn = ing.ParticipatesInReturn
                })
                .ToArray();

            RecipeOptions.Add(new RecipeVm
            {
                Index           = i,
                Label           = raw.IsTransmutation ? "Transmutación" : "Receta de Crafteo",
                SubLabel        = raw.AmountCrafted > 1
                                      ? $"×{raw.AmountCrafted} producidos por ciclo"
                                      : "×1 producido por ciclo",
                Ingredients     = ingredients,
                AmountCrafted   = raw.AmountCrafted,
                CraftingFocus   = raw.CraftingFocus,
                Silver          = raw.Silver,
                IsTransmutation = raw.IsTransmutation
            });
        }

        // Pre-rellenar silver con la receta 0 (la activa al cargar).
        TransmutationSilverPerUnit = CurrentRecipe?.Silver ?? 0m;

        // Reconstruir inventario al terminar (→ Recalculate al final)
        RebuildIngredientInventory();
    }

    private void LoadItemBonuses(ItemBaseVm itemVm)
    {
        var itemBase = _itemDataService.GetById(itemVm.ItemId);
        if (itemBase is null)
        {
            _rawItemBonuses = [];
            ItemBonuses = [];
            return;
        }

        _rawItemBonuses = _processPlayer.GetBonusesForItem(itemBase);

        // b.Total está en unidades internas del Destiny Board.
        // Multiplicar × 100 convierte al valor visual del juego (ej: 150 → 15,000).
        ItemBonuses = _rawItemBonuses
            .Where(b => b.Type == "craftingfocuscostreduction")
            .Select(b => new BonusDisplayItem(
                Label: _localization.GetText(b.Id),
                Total: b.Total * 100))
            .ToList();
    }

    private void UpdateCitiesForItem(ItemBaseVm itemVm)
    {
        var craftingCategory = _itemDataService.GetById(itemVm.ItemId)?.CraftingCategory;

        AvailableCities = _craftingLocations.Cities
            .Select(city => new CraftingCityOption(
                ClusterId:   city.ClusterId,
                Name:        city.Name,
                ClusterType: city.ClusterType,
                Bonus:       city.GetBonusFor(craftingCategory)))
            .OrderByDescending(o => o.Bonus)
            .ThenBy(o => o.Name)
            .ToList();

        // Restaurar ciudad guardada si existe, si no la de mayor bonus
        SelectedCityOption = (_pendingCityClusterId is not null
            ? AvailableCities.FirstOrDefault(c => c.ClusterId == _pendingCityClusterId)
            : null) ?? AvailableCities.FirstOrDefault();

        _pendingCityClusterId = null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INVENTARIO DE INGREDIENTES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reconstruye Card 3 preservando OwnedCount/UnitPrice del usuario.
    /// Al terminar dispara <see cref="Recalculate"/>.
    /// </summary>
    private void RebuildIngredientInventory()
    {
        var previousPrices = IngredientInventory
            .ToDictionary(i => i.ItemId, i => i.UnitPrice);

        IngredientInventory.Clear();

        if (CurrentRecipe is null) return;

        int qty = Math.Max(1, QuantityToCraft);

        var savedPrices = _appConfig.Calculator.IngredientPrices;

        foreach (var ingredient in CurrentRecipe.Ingredients)
        {
            var row = new IngredientInventoryM
            {
                Item          = ingredient.Item,
                RequiredCount = ingredient.Count * qty
            };

            // Prioridad: precio editado en esta sesión > precio guardado en disco
            if (previousPrices.TryGetValue(ingredient.ItemId, out var sessionPrice))
                row.UnitPrice = sessionPrice;
            else if (savedPrices.TryGetValue(ingredient.ItemId, out var persistedPrice))
                row.UnitPrice = persistedPrice;

            var ingApiId  = _itemDataService.GetById(ingredient.ItemId)?.ApiItemId ?? ingredient.ItemId;
            var ingName   = ingredient.Item.DisplayName;

            row.OnAutoPrice       = () => AutoPriceAsync(ingApiId, p => row.UnitPrice = p);
            row.OnOpenPriceDialog = () => OpenPriceDialogAsync(ingApiId, ingName, p => row.UnitPrice = p);

            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IngredientInventoryM.UnitPrice))
                    SaveIngredientPrice(row.ItemId, row.UnitPrice);
            };

            IngredientInventory.Add(row);
        }

        // CraftItemRow: precio de sesión → precio guardado → 0
        if (SelectedItem is not null)
        {
            var prevCraftPrice = CraftItemRow?.UnitPrice
                ?? _appConfig.Calculator.CraftItemPrices.GetValueOrDefault(SelectedItem.ItemId);

            var ct          = _ingredientCts?.Token ?? CancellationToken.None;
            var apiItemId   = _itemDataService.GetById(SelectedItem.ItemId)?.ApiItemId ?? SelectedItem.ItemId;
            var displayName = SelectedItem.DisplayName;

            var craftRow = new CraftItemRowM
            {
                Item      = BuildIngredientVm(SelectedItem.ItemId, ct),
                UnitPrice = prevCraftPrice
            };

            craftRow.OnAutoPrice       = () => AutoPriceAsync(apiItemId, p => craftRow.UnitPrice = p);
            craftRow.OnOpenPriceDialog = () => OpenPriceDialogAsync(apiItemId, displayName, p => craftRow.UnitPrice = p);

            craftRow.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CraftItemRowM.UnitPrice))
                    SaveCraftItemPrice(SelectedItem.ItemId, craftRow.UnitPrice);
            };

            CraftItemRow = craftRow;
        }

        // JournalRow: calcular libro y fama base
        RebuildJournalRow();

        Recalculate();
    }

    private void RebuildJournalRow()
    {
        if (SelectedItem is null || _rawRecipes.Length == 0)
        {
            JournalRow = null;
            TotalFameBase = TotalFamePremium = 0;
            return;
        }

        var recipeIndex = Math.Min(RecipeIndex, _rawRecipes.Length - 1);
        var rawRecipe   = _rawRecipes[recipeIndex];

        // Transmutación: sin fama, sin libro.
        if (rawRecipe.IsTransmutation)
        {
            JournalRow    = null;
            TotalFameBase = TotalFamePremium = 0;
            return;
        }

        var itemBase    = _itemDataService.GetById(SelectedItem.ItemId);
        var isRefining  = itemBase?.RawAttributes
                              .TryGetValue("shopsubcategory1", out var sub) == true
                          && sub == "refinedresources";

        // ── Fama: se calcula siempre, tenga o no libro ───────────────────────
        double fameBase;
        if (isRefining && itemBase?.Tier is int tier)
        {
            fameBase = FameCalculator.CalculateRefiningFame(tier, itemBase.EnchantmentLevel, premium: false);
        }
        else
        {
            var artifactType = FameCalculator.ResolveArtifactType(rawRecipe.Ingredients, _itemDataService);
            fameBase = FameCalculator.CalculateCraftFame(rawRecipe.Ingredients, artifactType, premium: false);
        }

        int cycles = rawRecipe.AmountCrafted > 1
            ? (int)Math.Ceiling((double)QuantityToCraft / rawRecipe.AmountCrafted)
            : QuantityToCraft;

        TotalFameBase    = fameBase * cycles;
        TotalFamePremium = FameCalculator.CalculateCraftFame(
            rawRecipe.Ingredients,
            FameCalculator.ResolveArtifactType(rawRecipe.Ingredients, _itemDataService),
            premium: true) * cycles;

        // ── Libro: solo si el ítem tiene journal asociado ────────────────────
        var journal = _itemDataService.GetJournalForItem(SelectedItem.ItemId);
        if (journal is null)
        {
            JournalRow = null;
            return;
        }

        double totalFame   = fameBase * cycles;
        int booksNeeded    = (int)Math.Ceiling(totalFame / journal.MaxFame);
        int booksCompleted = (int)Math.Floor(totalFame / journal.MaxFame);

        var savedJournal = _appConfig.Calculator.JournalPrices.GetValueOrDefault(journal.UniqueName);
        var prevFull  = JournalRow?.FullPrice  ?? savedJournal?.FullPrice  ?? 0m;
        var prevEmpty = JournalRow?.EmptyPrice ?? savedJournal?.EmptyPrice ?? 0m;

        var ct = _ingredientCts?.Token ?? CancellationToken.None;
        var journalRow = new JournalRowM
        {
            Journal        = journal,
            JournalVm      = BuildIngredientVm(journal.UniqueName, ct),
            BooksNeeded    = booksNeeded,
            BooksCompleted = booksCompleted,
            FullPrice      = prevFull,
            EmptyPrice     = prevEmpty
        };

        var journalApiId = _itemDataService.GetById(journal.UniqueName)?.ApiItemId ?? journal.UniqueName;
        var journalName  = journalRow.DisplayName;

        // Auto precio: Sell → FullPrice (vendes el libro lleno), Buy → EmptyPrice (compras el libro vacío)
        journalRow.OnAutoPrice = async () =>
        {
            var q = new PriceQuery(journalApiId, CurrentServer, SelectedCities, SelectedTimeScale);
            var r = await _priceService.GetPricesAsync(q);
            if (!r.Success || r.Prices.Count == 0) return;
            journalRow.FullPrice  = r.Prices[0].AvgPrice;
            journalRow.EmptyPrice = r.Prices[0].MinPrice;
        };

        journalRow.OnOpenPriceDialog = () => OpenPriceDialogAsync(
            journalApiId, journalName,
            p => journalRow.FullPrice = p);   // doble clic en Venta → precio lleno

        journalRow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(JournalRowM.FullPrice) or nameof(JournalRowM.EmptyPrice))
                SaveJournalPrices(journal.UniqueName, journalRow.FullPrice, journalRow.EmptyPrice);
        };

        JournalRow = journalRow;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CÁLCULO (orquestación pura — sin lógica de negocio)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Construye el request, delega en el UseCase y aplica el resultado al estado UI.
    /// No contiene lógica de dominio.
    /// </summary>
    private void Recalculate()
    {
        MaterialResults.Clear();
        if (CurrentRecipe is null) return;

        var city = SelectedCityOption is null
            ? null
            : _craftingLocations.Cities.FirstOrDefault(c => c.ClusterId == SelectedCityOption.ClusterId);

        // Llamada existente — mantiene todo el comportamiento actual
        var request = BuildRequest(city);
        var result  = _calculateCrafting.Execute(request);
        ApplyResult(result);

        // Nueva llamada — obtiene campos financieros adicionales
        var qRequest = BuildQuantityRequest(city);
        var qResult  = _calculateByQuantity.Execute(qRequest);
        ApplyQuantityResult(qResult);
    }

    private CraftingCostRequest BuildRequest(CraftingCityData? city)
    {
        var itemBase = SelectedItem is null
            ? null
            : _itemDataService.GetById(SelectedItem.ItemId);

        // ItemValue normaliza el craftingfocus del XML al valor visible en pantalla.
        // Fórmula: focusBase = craftingFocus / itemValue (ej: 4021 / 128 = 31 para T7 planks).
        var itemValueStr = itemBase?.RawAttributes.GetValueOrDefault("itemvalue");
        var itemValue    = int.TryParse(itemValueStr, out var iv) ? Math.Max(1, iv) : 1;

        return new CraftingCostRequest
        {
            Recipe                     = CurrentRecipe!,
            City                       = city,
            TransmutationSilverPerUnit = IsTransmutation ? TransmutationSilverPerUnit : null,
            Quantity           = QuantityToCraft,
            CraftingCategory   = itemBase?.CraftingCategory,
            ItemValue          = itemValue,
            UseFocus           = UseFocus,
            JournalBonus       = SelectedJournalBonus?.Value ?? 0m,
            HideoutBonus           = CurrentHideoutBonus,
            HideoutSpecialistBonus = IsHideoutCity && UseSpecialistBonus
                                         ? SelectedHideoutLevel.SpecialistBonus
                                         : 0m,
            AchievementBonuses = _rawItemBonuses,
            OwnedStock         = IngredientInventory
                .Select(i => new IngredientStock(i.ItemId, 0, i.UnitPrice))
                .ToList()
        };
    }

    private QuantityRequest BuildQuantityRequest(CraftingCityData? city)
    {
        var itemBase = SelectedItem is null
            ? null
            : _itemDataService.GetById(SelectedItem.ItemId);

        var itemValueStr = itemBase?.RawAttributes.GetValueOrDefault("itemvalue");
        var itemValue    = int.TryParse(itemValueStr, out var iv) ? Math.Max(1, iv) : 1;

        var isRefining = itemBase?.RawAttributes
                             .TryGetValue("shopsubcategory1", out var sub) == true
                         && sub == "refinedresources";

        return new QuantityRequest(
            Recipe:                     CurrentRecipe!,
            Quantity:                   QuantityToCraft,
            City:                       city,
            CraftingCategory:           itemBase?.CraftingCategory,
            ItemValue:                  itemValue,
            UseFocus:                   UseFocus,
            JournalBonus:               SelectedJournalBonus?.Value ?? 0m,
            HideoutBonus:               CurrentHideoutBonus,
            HideoutSpecialistBonus:     IsHideoutCity && UseSpecialistBonus
                                            ? SelectedHideoutLevel.SpecialistBonus
                                            : 0m,
            AchievementBonuses:         _rawItemBonuses,
            Stock:                      IngredientInventory
                                            .Select(i => new IngredientStock(i.ItemId, 0, i.UnitPrice))
                                            .ToList(),
            TransmutationSilverPerUnit: IsTransmutation ? TransmutationSilverPerUnit : null,
            SalePrice:                  CraftItemRow?.UnitPrice ?? 0m,
            IsRefining:                 isRefining,
            ItemTier:                   itemBase?.Tier,
            EnchantmentLevel:           itemBase?.EnchantmentLevel ?? 0);
    }

    private void ApplyResult(CraftingCostResult result)
    {
        // Foco (siempre disponible, independiente de ciudad)
        EffectiveReturnRate   = result.ReturnRate;
        FocusPerCraft         = result.FocusPerCraft;
        TotalFocusCost        = result.TotalFocusCost;
        FocusReductionPercent = result.FocusReductionPercent;
        HideoutFocusSaving    = result.FocusSpecialistSaving ?? 0;

        if (result.Lines.Count == 0) return;

        // Mapear líneas del UseCase → MaterialResultM con imagen (ItemBaseVm)
        var ingredientByItemId = CurrentRecipe!.Ingredients
            .ToDictionary(i => i.ItemId, StringComparer.OrdinalIgnoreCase);

        foreach (var line in result.Lines)
        {
            if (!ingredientByItemId.TryGetValue(line.ItemId, out var ingredientVm))
                continue;

            MaterialResults.Add(new MaterialResultM
            {
                Item             = ingredientVm.Item,
                GrossQuantity    = line.GrossQuantity,
                NetQuantity      = line.NetToBuy,
                ReturnedQuantity = line.ReturnedQuantity,
                BuyLocation      = line.BuyLocation,
                UnitPrice        = line.UnitPrice
            });
        }

        TotalCost          = result.TotalCost;
        TotalReturnedValue = result.Lines.Sum(l => l.ReturnedValue);

        OnPropertyChanged(nameof(CraftedItemTotalValue));
        OnPropertyChanged(nameof(TotalInvestment));
        OnPropertyChanged(nameof(TotalGain));
        OnPropertyChanged(nameof(ProfitLoss));
        OnPropertyChanged(nameof(IsProfitable));
    }

    private void ApplyQuantityResult(QuantityResult result)
    {
        Roi               = result.Roi;
        Margin            = result.Margin;
        ProfitLossNoFocus = result.ProfitLossNoFocus;
        RoiNoFocus        = result.RoiNoFocus;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ESTADO DE CIUDADES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Carga todas las ciudades con bonus = 0 (sin ítem seleccionado).</summary>
    private void ResetCiudades()
    {
        var previousId = SelectedCityOption?.ClusterId;

        AvailableCities = _craftingLocations.Cities
            .Select(city => new CraftingCityOption(
                ClusterId:   city.ClusterId,
                Name:        city.Name,
                ClusterType: city.ClusterType,
                Bonus:       0m))
            .OrderBy(o => o.Name)
            .ToList();

        SelectedCityOption = AvailableCities
            .FirstOrDefault(c => c.ClusterId == previousId)
            ?? AvailableCities.FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HANDLERS DE COLECCIONES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Notifica todas las propiedades computadas que dependen de <see cref="RecipeOptions"/>.
    /// No tiene side effects de cálculo ni de inventario.
    /// </summary>
    private void OnRecipeCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRecipes));
        OnPropertyChanged(nameof(HasMultipleRecipes));
        OnPropertyChanged(nameof(HasSelectedItemWithoutRecipe));
        OnPropertyChanged(nameof(CurrentRecipe));
        OnPropertyChanged(nameof(PreviousRecipeM));
        OnPropertyChanged(nameof(NextRecipeM));
        OnPropertyChanged(nameof(RecipeCarouselLabel));
        OnPropertyChanged(nameof(HasPreviousRecipe));
        OnPropertyChanged(nameof(HasNextRecipe));
        PreviousRecipeCommand.NotifyCanExecuteChanged();
        NextRecipeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Gestiona PropertyChanged de ingredientes (OwnedCount / UnitPrice)
    /// y la notificación de <see cref="HasIngredientInventory"/>.
    /// </summary>
    private void OnIngredientCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasIngredientInventory));

        if (e.NewItems is not null)
            foreach (IngredientInventoryM item in e.NewItems)
                item.PropertyChanged += OnIngredientPropertyChanged;

        if (e.OldItems is not null)
            foreach (IngredientInventoryM item in e.OldItems)
                item.PropertyChanged -= OnIngredientPropertyChanged;
    }

    /// <summary>Recalcula cuando el usuario edita UnitPrice de un ingrediente.</summary>
    private void OnIngredientPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IngredientInventoryM.UnitPrice))
            Recalculate();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Delega la construcción del VM de ingrediente en el servicio de búsqueda.</summary>
    private ItemBaseVm BuildIngredientVm(string itemId, CancellationToken ct)
        => _itemSearch.BuildItemVm(itemId, ct);

    // ── Precio automático y diálogo ───────────────────────────────────────────

    private AlbionServer          CurrentServer    => AlbionServer.FromName(_appConfig.ServerName);
    private IReadOnlyList<string> SelectedCities   => _appConfig.MarketPrice.SelectedCities.ToList();
    private PriceTimeScale        SelectedTimeScale => PriceTimeScale.FromLabel(_appConfig.MarketPrice.TimeScaleLabel);

    /// <summary>
    /// Busca el precio promedio histórico (anti-outlier) y lo aplica al campo de precio.
    /// </summary>
    private async Task AutoPriceAsync(string apiItemId, Action<decimal> applyPrice)
    {
        var query  = new PriceQuery(apiItemId, CurrentServer, SelectedCities, SelectedTimeScale);
        var result = await _priceService.GetPricesAsync(query);
        if (!result.Success || result.Prices.Count == 0) return;
        applyPrice(result.Prices[0].AvgPrice);
    }

    /// <summary>
    /// Abre el diálogo de selección de precios con los precios actuales de mercado.
    /// Double-click en Venta o Compra aplica el precio al campo correspondiente.
    /// </summary>
    private async Task OpenPriceDialogAsync(
        string         apiItemId,
        string         displayName,
        Action<decimal> applyPrice)
    {
        var dialogVm = new PriceSelectionDialogVm
        {
            ItemDisplayName = displayName,
            IsLoading       = true,
        };
        var dialog = new PriceSelectionDialogV(dialogVm);

        // Conectar cierre del diálogo
        dialogVm.CloseDialog = () =>
            DialogService.Instance.CerrarSiEstaAbiertoYEsperar(dialog);

        // Abrir el diálogo y cargar datos en paralelo
        var showTask = DialogService.Instance.MostrarDialogo(dialog);

        var result = await _priceService.GetCurrentPricesAsync(
            apiItemId, CurrentServer, SelectedCities);

        dialogVm.IsLoading = false;

        if (!result.Success)
            dialogVm.Error = result.Error;
        else
            dialogVm.Prices = result.Prices;

        await showTask;

        if (dialogVm.SelectedPrice.HasValue)
            applyPrice(dialogVm.SelectedPrice.Value);
    }

    // ── Persistencia de settings ──────────────────────────────────────────────

    private void LoadSettings()
    {
        // Solo cargamos lo GLOBAL: UsePremium (cuenta de jugador, igual para todas las pestañas).
        // El resto (UseFocus, UseSpecialistBonus, HideoutLevel, JournalBonus, CityClusterId)
        // son per-tab y se restauran vía RestoreTabState desde workspace.json.
        LoadPersistedProperties(_store);

        // Valores por defecto para una pestaña nueva (sin estado guardado en workspace.json)
        SelectedJournalBonus = JournalBonusOptions[0];
        ResetCiudades();
    }

    /// <summary>
    /// Restaura el estado per-tab desde workspace.json.
    /// Usa _suppressSave para que los OnXxxChanged no escriban al store compartido
    /// mientras se restauran los valores — evita que una pestaña sobreescriba a otra.
    /// </summary>
    public void RestoreTabState(Models.CalculatorTabState state)
    {
        _suppressSave = true;
        try
        {
            QuantityToCraft    = state.Quantity;
            UseFocus           = state.UseFocus;
            UseSpecialistBonus = state.UseSpecialistBonus;
            SelectedHideoutLevel = HideoutPowerlevel.ForLevel(Math.Clamp(state.HideoutLevel, 1, 9));

            var journalOpt = JournalBonusOptions.FirstOrDefault(o => o.Value == state.JournalBonusValue)
                             ?? JournalBonusOptions[0];
            SelectedJournalBonus = journalOpt;

            _pendingCityClusterId = state.CityClusterId;
        }
        finally
        {
            _suppressSave = false;
        }
    }

    /// <summary>
    /// Persiste las propiedades simples marcadas con [Persist].
    /// Las propiedades complejas se guardan directamente en sus OnXxxChanged.
    /// </summary>
    private void SaveSettings() => SavePersistedProperties(_store);

    private void SaveIngredientPrice(string itemId, decimal price)
    {
        _appConfig.Calculator.IngredientPrices[itemId] = price;
        _appConfig.SaveCalculator();
    }

    private void SaveCraftItemPrice(string itemId, decimal price)
    {
        _appConfig.Calculator.CraftItemPrices[itemId] = price;
        _appConfig.SaveCalculator();
    }

    private void SaveJournalPrices(string journalName, decimal full, decimal empty)
    {
        _appConfig.Calculator.JournalPrices[journalName] = new JournalPricePair
        {
            FullPrice  = full,
            EmptyPrice = empty
        };
        _appConfig.SaveCalculator();
    }

    /// <summary>ClusterId de la ciudad a restaurar una vez que se carguen las ciudades.</summary>
    private string? _pendingCityClusterId;

    private void CancelIngredientImageLoads()
    {
        _ingredientCts?.Cancel();
        _ingredientCts?.Dispose();
        _ingredientCts = null;
    }

    private void ClearCalculationState()
    {
        EffectiveReturnRate   = 0m;
        FocusPerCraft         = null;
        TotalFocusCost        = null;
        FocusReductionPercent = 0;
        HideoutFocusSaving    = 0;
    }
}
