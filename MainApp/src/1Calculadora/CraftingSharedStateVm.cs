using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Albion_App.Infrastructure;
using Albion_App.Models;
using Albion_App.ViewModel;
using Albion_App.Components.Achievement;
using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;
using AlbionApp.Domain.Market;
using LibServices.AppConfig;
using LibServices.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App._1Calculadora;

/// <summary>
/// Holds all shared input state for the crafting calculator.
/// Consumed by QuantityModeVm (and future mode VMs) for calculation.
/// Fires InputChanged whenever any calculation-relevant input changes.
/// </summary>
public sealed partial class CraftingSharedStateVm : ObservableObject
{
    // ═══════════════════════════════════════════════════════════════════════════
    // DEPENDENCIAS
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly IItemSearchService         _itemSearch;
    private readonly IItemDataService           _itemDataService;
    private readonly ProcessPlayerUseCase       _processPlayer;
    private readonly ILocalizationService       _localization;
    private readonly ICraftingLocationService   _craftingLocations;
    private readonly AppConfigService           _appConfig;
    private readonly IPersistenceStore          _store;

    // ═══════════════════════════════════════════════════════════════════════════
    // EVENTOS PÚBLICOS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Fires whenever any calculation-relevant input changes.</summary>
    public event Action? InputChanged;

    /// <summary>Fires when LimpiarItem executes — coordinador and QuantityModeVm subscribe.</summary>
    public event Action? ItemCleared;

    // ═══════════════════════════════════════════════════════════════════════════
    // ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════════

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

    /// <summary>ClusterId de la ciudad a restaurar una vez que se carguen las ciudades.</summary>
    private string? _pendingCityClusterId;

    // ═══════════════════════════════════════════════════════════════════════════
    // CARD 1 — Ítem objetivo
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(HasSelectedItemWithoutRecipe))]
    [NotifyPropertyChangedFor(nameof(RecipeCarouselLabel))]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private ItemBaseVm? _selectedItem;

    /// <summary>Título de la pestaña en el workspace. Usado por WorkspaceVm.</summary>
    public string TabTitle => SelectedItem?.DisplayName ?? "Nueva pestaña";

    public bool HasSelectedItem => SelectedItem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItemBonuses))]
    private IReadOnlyList<BonusDisplayItem> _itemBonuses = [];

    public bool HasItemBonuses => ItemBonuses.Count > 0;

    // ═══════════════════════════════════════════════════════════════════════════
    // CARD 2 — Carrusel de recetas + parámetros de crafteo
    // ═══════════════════════════════════════════════════════════════════════════

    public ObservableCollection<RecipeVm> RecipeOptions { get; } = [];

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

    public bool      HasRecipes                 => RecipeOptions.Count > 0;
    public bool      HasMultipleRecipes         => RecipeOptions.Count > 1;
    public bool      HasSelectedItemWithoutRecipe => HasSelectedItem && !HasRecipes;
    public bool      HasPreviousRecipe          => HasMultipleRecipes;
    public bool      HasNextRecipe              => HasMultipleRecipes;
    public RecipeVm? CurrentRecipe              => RecipeOptions.Count > 0 ? RecipeOptions[RecipeIndex] : null;
    public bool      IsTransmutation            => CurrentRecipe?.IsTransmutation ?? false;
    public bool      IsCraftingRecipe           => !IsTransmutation;
    public RecipeVm? PreviousRecipeM            => RecipeOptions.Count > 1 ? RecipeOptions[RecipeIndex > 0 ? RecipeIndex - 1 : RecipeOptions.Count - 1] : null;
    public RecipeVm? NextRecipeM                => RecipeOptions.Count > 1 ? RecipeOptions[RecipeIndex < RecipeOptions.Count - 1 ? RecipeIndex + 1 : 0] : null;
    public string    RecipeCarouselLabel        => RecipeOptions.Count > 0 ? $"Receta ({RecipeIndex + 1}/{RecipeOptions.Count})" : "—";

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
    private HideoutPowerlevel _selectedHideoutLevel = HideoutPowerlevel.ForLevel(1);

    /// <summary>
    /// True = el hideout está especializado para el ítem craftado.
    /// </summary>
    [ObservableProperty]
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
    /// Escrito por QuantityModeVm después de cada cálculo.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideoutBonusLabel))]
    private int _hideoutFocusSaving;

    /// <summary>
    /// Bono generalista al Return Rate — SIEMPRE aplica cuando se craftea en hideout.
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
    /// </summary>
    [ObservableProperty]
    private decimal _transmutationSilverPerUnit;

    // ═══════════════════════════════════════════════════════════════════════════
    // PROPIEDADES ADICIONALES PARA QuantityModeVm
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Resolved CraftingCityData — QuantityModeVm lo usa en Recalculate.</summary>
    public CraftingCityData? CurrentCityData =>
        SelectedCityOption is null
            ? null
            : _craftingLocations.Cities.FirstOrDefault(c => c.ClusterId == SelectedCityOption.ClusterId);

    internal IRecipe[]                        RawRecipes        => _rawRecipes;
    internal IReadOnlyList<AggregatedBonus>   RawItemBonuses    => _rawItemBonuses;
    internal CancellationToken                IngredientCtsToken => _ingredientCts?.Token ?? CancellationToken.None;

    /// <summary>Helper que QuantityModeVm llama para construir ViewModels de ingredientes.</summary>
    public ItemBaseVm BuildIngredientVm(string itemId, CancellationToken ct)
        => _itemSearch.BuildItemVm(itemId, ct);

    // App config helpers para QuantityModeVm
    internal AlbionServer          CurrentServer    => AlbionServer.FromName(_appConfig.ServerName);
    internal IReadOnlyList<string> MarketCities     => _appConfig.MarketPrice.SelectedCities.ToList();
    internal PriceTimeScale        MarketTimeScale  => PriceTimeScale.FromLabel(_appConfig.MarketPrice.TimeScaleLabel);

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public CraftingSharedStateVm(
        IItemSearchService          itemSearch,
        IItemDataService            itemDataService,
        ProcessPlayerUseCase        processPlayer,
        ILocalizationService        localization,
        ICraftingLocationService    craftingLocations,
        AppConfigService            appConfig,
        IPersistenceStore           store)
    {
        _itemSearch        = itemSearch;
        _itemDataService   = itemDataService;
        _processPlayer     = processPlayer;
        _localization      = localization;
        _craftingLocations = craftingLocations;
        _appConfig         = appConfig;
        _store             = store;

        RecipeOptions.CollectionChanged += OnRecipeCollectionChanged;

        LoadSettings();
    }

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
        RecipeOptions.Clear();
        RecipeIndex = 0;
        HideoutFocusSaving = 0;
        ResetCiudades();
        LimpiarItemCommand.NotifyCanExecuteChanged();
        ItemCleared?.Invoke();
    }

    /// <summary>Navega a la receta anterior (wrap-around).</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleRecipes))]
    private void PreviousRecipe()
    {
        RecipeIndex = RecipeIndex > 0 ? RecipeIndex - 1 : RecipeOptions.Count - 1;
        InputChanged?.Invoke();
    }

    /// <summary>Navega a la receta siguiente (wrap-around).</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleRecipes))]
    private void NextRecipe()
    {
        RecipeIndex = RecipeIndex < RecipeOptions.Count - 1 ? RecipeIndex + 1 : 0;
        InputChanged?.Invoke();
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
        InputChanged?.Invoke();
    }

    /// <summary>Ciudad — per-tab, guarda en store solo si no estamos restaurando.</summary>
    partial void OnSelectedCityOptionChanged(CraftingCityOption? value)
    {
        if (!_suppressSave) _store.Set("city_cluster_id", value?.ClusterId);
        InputChanged?.Invoke();
    }

    /// <summary>Bono de diario — per-tab.</summary>
    partial void OnSelectedJournalBonusChanged(JournalBonusOptionM? value)
    {
        if (!_suppressSave) _store.Set("journal_bonus", value?.Value ?? 0m);
        InputChanged?.Invoke();
    }

    /// <summary>Nivel de hideout — per-tab.</summary>
    partial void OnSelectedHideoutLevelChanged(HideoutPowerlevel value)
    {
        if (!_suppressSave) _store.Set("hideout_level", value.Level);
        InputChanged?.Invoke();
    }

    /// <summary>Especialista — per-tab, no persiste al store global.</summary>
    partial void OnUseSpecialistBonusChanged(bool value) => InputChanged?.Invoke();

    /// <summary>Silver de transmutación — valida mínimo y dispara recálculo.</summary>
    partial void OnTransmutationSilverPerUnitChanged(decimal value)
    {
        // No puede bajar del valor base del XML.
        var minSilver = CurrentRecipe?.Silver ?? 0m;
        if (value < minSilver)
        {
            TransmutationSilverPerUnit = minSilver;
            return;
        }
        InputChanged?.Invoke();
    }

    /// <summary>Foco — per-tab.</summary>
    partial void OnUseFocusChanged(bool value)
    {
        InputChanged?.Invoke();
    }

    /// <summary>Premium — GLOBAL, persiste al store compartido.</summary>
    partial void OnUsePremiumChanged(bool value)
    {
        if (!_suppressSave) SaveSettings();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FLUJO DE SELECCIÓN DE ÍTEM
    // ═══════════════════════════════════════════════════════════════════════════

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
        await Task.CompletedTask;
    }

    internal void LoadRecipes(ItemBaseVm itemVm)
    {
        CancelIngredientImageLoads();
        RecipeOptions.Clear();
        RecipeIndex = 0;
        TransmutationSilverPerUnit = 0;

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

        // Disparar InputChanged para que QuantityModeVm reconstruya inventario y recalcule
        InputChanged?.Invoke();
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

        SelectedCityOption = (_pendingCityClusterId is not null
            ? AvailableCities.FirstOrDefault(c => c.ClusterId == _pendingCityClusterId)
            : null) ?? AvailableCities.FirstOrDefault();

        _pendingCityClusterId = null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ESTADO DE CIUDADES
    // ═══════════════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    internal void CancelIngredientImageLoads()
    {
        _ingredientCts?.Cancel();
        _ingredientCts?.Dispose();
        _ingredientCts = null;
    }

    // ── Persistencia de settings ──────────────────────────────────────────────

    private void LoadSettings()
    {
        LoadPersistedProperties(_store);

        SelectedJournalBonus = JournalBonusOptions[0];
        ResetCiudades();
    }

    /// <summary>
    /// Restaura el estado per-tab desde workspace.json (solo campos de SharedState).
    /// </summary>
    public void RestoreTabState(CalculatorTabState state)
    {
        _suppressSave = true;
        try
        {
            UseFocus           = state.UseFocus;
            UseSpecialistBonus = state.UseSpecialistBonus;
            SelectedHideoutLevel = HideoutPowerlevel.ForLevel(Math.Clamp(state.HideoutLevel, 1, 9));

            var journalOpt = JournalBonusOptions.FirstOrDefault(o => o.Value == state.JournalBonusValue)
                             ?? JournalBonusOptions[0];
            SelectedJournalBonus = journalOpt;

            _pendingCityClusterId = state.CityClusterId;
        }
        finally { _suppressSave = false; }
    }

    private void SaveSettings() => SavePersistedProperties(_store);
}
