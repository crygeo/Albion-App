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
using CommunityToolkit.Mvvm.ComponentModel;
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

    // Bonuses raw del ítem: fuente para achievement focus y return rate.
    private IReadOnlyList<AggregatedBonus> _rawItemBonuses = [];

    // CancellationTokenSource para imágenes de ingredientes en vuelo.
    private CancellationTokenSource? _ingredientCts;

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
    private int _recipeIndex;

    public bool      HasRecipes    => RecipeOptions.Count > 0;
    public bool      HasMultipleRecipes          => RecipeOptions.Count > 1;
    public bool      HasSelectedItemWithoutRecipe => HasSelectedItem && !HasRecipes;
    public bool      HasPreviousRecipe            => HasMultipleRecipes;
    public bool      HasNextRecipe                => HasMultipleRecipes;
    public RecipeVm? CurrentRecipe  => RecipeOptions.Count > 0 ? RecipeOptions[RecipeIndex] : null;
    public RecipeVm? PreviousRecipeM => RecipeOptions.Count > 1 ? RecipeOptions[RecipeIndex > 0 ? RecipeIndex - 1 : RecipeOptions.Count - 1] : null;
    public RecipeVm? NextRecipeM => RecipeOptions.Count > 1 ? RecipeOptions[RecipeIndex < RecipeOptions.Count - 1 ? RecipeIndex + 1 : 0] : null;
    public string RecipeCarouselLabel => RecipeOptions.Count > 0 ? $"Receta ({RecipeIndex + 1}/{RecipeOptions.Count})" : "—";

    // ── Ciudad y parámetros ───────────────────────────────────────────────────

    [ObservableProperty] private IReadOnlyList<CraftingCityOption> _availableCities = [];
    [ObservableProperty] private CraftingCityOption?               _selectedCityOption;

    public IReadOnlyList<JournalBonusOptionM> JournalBonusOptions { get; } =
    [
        new(0.00m, "0% — Sin bono de diario"),
        new(0.10m, "10% — Diario de rendimiento"),
        new(0.20m, "20% — Diario premium"),
    ];

    [ObservableProperty] private JournalBonusOptionM? _selectedJournalBonus;
    [ObservableProperty] private bool _usePremium;
    [ObservableProperty] private bool _useFocus;

    // ═══════════════════════════════════════════════════════════════════════════
    // CARD 3 — Cantidades e inventario
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIngredientInventory))]
    private int _quantityToCraft = 1;

    public ObservableCollection<IngredientInventoryM> IngredientInventory { get; } = [];
    public bool HasIngredientInventory => IngredientInventory.Count > 0;

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

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public CalculadoraSvm(
        IItemSearchService            itemSearch,
        IItemDataService              itemDataService,
        ProcessPlayerUseCase          processPlayer,
        ILocalizationService          localization,
        ICraftingLocationService      craftingLocations,
        CalculateCraftingCostUseCase  calculateCrafting)
    {
        _itemSearch        = itemSearch;
        _itemDataService   = itemDataService;
        _processPlayer     = processPlayer;
        _localization      = localization;
        _craftingLocations = craftingLocations;
        _calculateCrafting = calculateCrafting;

        SelectedJournalBonus = JournalBonusOptions[0];
        ResetCiudades();

        // Suscripciones a colecciones: un handler por colección, nombre explícito.
        RecipeOptions.CollectionChanged      += OnRecipeCollectionChanged;
        IngredientInventory.CollectionChanged += OnIngredientCollectionChanged;
        MaterialResults.CollectionChanged    += (_, _) => OnPropertyChanged(nameof(HasResults));
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
        ItemBonuses = [];

        ClearCalculationState();
        ResetCiudades();

        RecipeOptions.Clear();
        RecipeIndex = 0;
        IngredientInventory.Clear();
        MaterialResults.Clear();
        TotalCost = 0;
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

    /// <summary>Cuando RecipeIndex cambia, actualiza CanExecute de las flechas.</summary>
    partial void OnRecipeIndexChanged(int value)
    {
        PreviousRecipeCommand.NotifyCanExecuteChanged();
        NextRecipeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Cuando cambia la cantidad objetivo, reconstruye el inventario y recalcula.</summary>
    partial void OnQuantityToCraftChanged(int value) => RebuildIngredientInventory();

    /// <summary>Cuando cambia la ciudad, recalcula.</summary>
    partial void OnSelectedCityOptionChanged(CraftingCityOption? value) => Recalculate();

    /// <summary>Cuando cambia el bono de diario, recalcula.</summary>
    partial void OnSelectedJournalBonusChanged(JournalBonusOptionM? value) => Recalculate();

    /// <summary>Cuando cambia el uso de foco, notifica HasFocusCost y recalcula.</summary>
    partial void OnUseFocusChanged(bool value)
    {
        OnPropertyChanged(nameof(HasFocusCost));
        Recalculate();
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

        var rawRecipes = _itemDataService.GetRecipes(itemVm.ItemId);
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
                Index         = i,
                Label         = "Receta de Crafteo",
                SubLabel      = raw.AmountCrafted > 1
                                    ? $"×{raw.AmountCrafted} producidos por ciclo"
                                    : "×1 producido por ciclo",
                Ingredients   = ingredients,
                AmountCrafted = raw.AmountCrafted,
                CraftingFocus = raw.CraftingFocus,
                Silver        = raw.Silver
            });
        }

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
                ClusterId: city.ClusterId,
                Name:      city.Name,
                Bonus:     city.GetBonusFor(craftingCategory)))
            .OrderByDescending(o => o.Bonus)
            .ThenBy(o => o.Name)
            .ToList();

        // Auto-selecciona la ciudad con mayor bonus para este ítem
        SelectedCityOption = AvailableCities.FirstOrDefault();
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
        var previousStock = IngredientInventory
            .ToDictionary(i => i.ItemId, i => (i.OwnedCount, i.UnitPrice));

        IngredientInventory.Clear();

        if (CurrentRecipe is null) return;

        int qty = Math.Max(1, QuantityToCraft);

        foreach (var ingredient in CurrentRecipe.Ingredients)
        {
            var row = new IngredientInventoryM
            {
                Item          = ingredient.Item,
                RequiredCount = ingredient.Count * qty
            };

            if (previousStock.TryGetValue(ingredient.ItemId, out var prev))
            {
                row.OwnedCount = prev.OwnedCount;
                row.UnitPrice  = prev.UnitPrice;
            }

            IngredientInventory.Add(row);
        }

        Recalculate();
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

        var request = BuildRequest(city);
        var result  = _calculateCrafting.Execute(request);

        ApplyResult(result);
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
            Recipe             = CurrentRecipe!,
            City               = city,
            Quantity           = QuantityToCraft,
            CraftingCategory   = itemBase?.CraftingCategory,
            ItemValue          = itemValue,
            UseFocus           = UseFocus,
            JournalBonus       = SelectedJournalBonus?.Value ?? 0m,
            AchievementBonuses = _rawItemBonuses,
            OwnedStock         = IngredientInventory
                .Select(i => new IngredientStock(i.ItemId, i.OwnedCount, i.UnitPrice))
                .ToList()
        };
    }

    private void ApplyResult(CraftingCostResult result)
    {
        // Foco (siempre disponible, independiente de ciudad)
        EffectiveReturnRate   = result.ReturnRate;
        FocusPerCraft         = result.FocusPerCraft;
        TotalFocusCost        = result.TotalFocusCost;
        FocusReductionPercent = result.FocusReductionPercent;

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
                Item        = ingredientVm.Item,
                NetQuantity = line.NetToBuy,
                BuyLocation = line.BuyLocation,
                UnitPrice   = line.UnitPrice
            });
        }

        TotalCost = result.TotalCost;
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
                ClusterId: city.ClusterId,
                Name:      city.Name,
                Bonus:     0m))
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

    /// <summary>Recalcula cuando el usuario edita OwnedCount o UnitPrice.</summary>
    private void OnIngredientPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IngredientInventoryM.OwnedCount)
                           or nameof(IngredientInventoryM.UnitPrice))
            Recalculate();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Delega la construcción del VM de ingrediente en el servicio de búsqueda.</summary>
    private ItemBaseVm BuildIngredientVm(string itemId, CancellationToken ct)
        => _itemSearch.BuildItemVm(itemId, ct);

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
    }
}
