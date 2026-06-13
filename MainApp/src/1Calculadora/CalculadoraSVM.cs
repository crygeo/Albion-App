using System.Collections.ObjectModel;
using System.Windows.Input;
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
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App._1Calculadora;

/// <summary>
/// Thin coordinator ViewModel for the crafting calculator.
/// Owns CraftingSharedStateVm, QuantityModeVm, and StickyResultsBarVm.
/// Exposes forwarding properties so the existing XAML (bound to this type) continues to work
/// without changes — these will be removed when XAML is updated (Phase 2.5).
/// </summary>
public sealed partial class CalculadoraSvm : ObservableObject, ISectionIcons
{
    // ═══════════════════════════════════════════════════════════════════════════
    // SUB-VMs
    // ═══════════════════════════════════════════════════════════════════════════

    public CraftingSharedStateVm SharedState  { get; }
    public QuantityModeVm        QuantityMode { get; }
    public StickyResultsBarVm    StickyBar    { get; }

    // ═══════════════════════════════════════════════════════════════════════════
    // ISectionIcons
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty] private string       _header = "Calculadora";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.Calculator;

    // ═══════════════════════════════════════════════════════════════════════════
    // EXPORT PNG
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// La vista se suscribe a este evento y renderiza el elemento visual a PNG.
    /// </summary>
    public event Action? ExportRequested;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportarPng() => ExportRequested?.Invoke();
    private bool CanExport() => QuantityMode.HasResults;

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public CalculadoraSvm(
        IItemSearchService           itemSearch,
        IItemDataService             itemDataService,
        ProcessPlayerUseCase         processPlayer,
        ILocalizationService         localization,
        ICraftingLocationService     craftingLocations,
        CalculateCraftingCostUseCase calculateCrafting,
        CalculateByQuantityUseCase   calculateByQuantity,
        AppConfigService             appConfig,
        IPersistenceStore            store,
        IPriceService                priceService)
    {
        SharedState  = new CraftingSharedStateVm(itemSearch, itemDataService, processPlayer,
                           localization, craftingLocations, appConfig, store);
        QuantityMode = new QuantityModeVm(SharedState, calculateCrafting, calculateByQuantity,
                           itemDataService, appConfig, priceService);
        StickyBar    = new StickyResultsBarVm();

        // QuantityMode notifica cuando terminó de recalcular → actualizar StickyBar
        QuantityMode.RecalculationDone +=
            () => StickyBar.UpdateFromQuantityResult(QuantityMode.LastResult, QuantityMode.QuantityToCraft);

        // Ítem limpiado → reset resultado + sticky bar
        SharedState.ItemCleared += () => { QuantityMode.Reset(); StickyBar.Reset(); };

        // HasResults changed → CanExecute de ExportarPng
        QuantityMode.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuantityModeVm.HasResults))
                ExportarPngCommand.NotifyCanExecuteChanged();
        };

        // Propagar todos los PropertyChanged de sub-VMs hacia el coordinador
        // para que el XAML que bindea a esta clase siga recibiendo notificaciones.
        SharedState.PropertyChanged  += (_, e) => OnPropertyChanged(e.PropertyName);
        QuantityMode.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DELEGACIONES AL WORKSPACE / PERSISTENCIA
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Título de la pestaña en el workspace.</summary>
    public string TabTitle => SharedState.TabTitle;

    public void RestoreTabState(CalculatorTabState state)
    {
        SharedState.RestoreTabState(state);
        QuantityMode.RestoreTabState(state);
    }

    internal Task LoadItemExternalAsync(ItemBaseVm itemVm)
        => SharedState.LoadItemExternalAsync(itemVm);

    // ═══════════════════════════════════════════════════════════════════════════
    // FORWARDING PROPERTIES — TEMPORALES (se eliminan en Task 2.5)
    // ═══════════════════════════════════════════════════════════════════════════

    // ── SharedState forwards ──────────────────────────────────────────────────

    public ItemBaseVm?        SelectedItem                 => SharedState.SelectedItem;
    public bool               HasSelectedItem              => SharedState.HasSelectedItem;
    public bool               HasSelectedItemWithoutRecipe => SharedState.HasSelectedItemWithoutRecipe;
    public IReadOnlyList<BonusDisplayItem> ItemBonuses     => SharedState.ItemBonuses;
    public bool               HasItemBonuses               => SharedState.HasItemBonuses;

    public ObservableCollection<RecipeVm> RecipeOptions    => SharedState.RecipeOptions;
    public int                RecipeIndex
    {
        get => SharedState.RecipeIndex;
        set => SharedState.RecipeIndex = value;
    }
    public RecipeVm?          CurrentRecipe                => SharedState.CurrentRecipe;
    public string             RecipeCarouselLabel          => SharedState.RecipeCarouselLabel;
    public bool               HasRecipes                   => SharedState.HasRecipes;
    public bool               HasMultipleRecipes           => SharedState.HasMultipleRecipes;
    public bool               HasPreviousRecipe            => SharedState.HasPreviousRecipe;
    public bool               HasNextRecipe                => SharedState.HasNextRecipe;
    public bool               IsTransmutation              => SharedState.IsTransmutation;
    public bool               IsCraftingRecipe             => SharedState.IsCraftingRecipe;
    public RecipeVm?          PreviousRecipeM              => SharedState.PreviousRecipeM;
    public RecipeVm?          NextRecipeM                  => SharedState.NextRecipeM;

    public IReadOnlyList<CraftingCityOption> AvailableCities => SharedState.AvailableCities;
    public CraftingCityOption? SelectedCityOption
    {
        get => SharedState.SelectedCityOption;
        set => SharedState.SelectedCityOption = value;
    }
    public bool               HasSelectedCity              => SharedState.HasSelectedCity;
    public bool               IsHideoutCity                => SharedState.IsHideoutCity;

    public static IReadOnlyList<HideoutPowerlevel> HideoutLevels => CraftingSharedStateVm.HideoutLevels;
    public HideoutPowerlevel  SelectedHideoutLevel
    {
        get => SharedState.SelectedHideoutLevel;
        set => SharedState.SelectedHideoutLevel = value;
    }
    public bool               UseSpecialistBonus
    {
        get => SharedState.UseSpecialistBonus;
        set => SharedState.UseSpecialistBonus = value;
    }
    public string             HideoutBonusLabel            => SharedState.HideoutBonusLabel;
    public int                HideoutFocusSaving           => SharedState.HideoutFocusSaving;

    public IReadOnlyList<JournalBonusOptionM> JournalBonusOptions => SharedState.JournalBonusOptions;
    public JournalBonusOptionM? SelectedJournalBonus
    {
        get => SharedState.SelectedJournalBonus;
        set => SharedState.SelectedJournalBonus = value;
    }
    public bool               UsePremium
    {
        get => SharedState.UsePremium;
        set => SharedState.UsePremium = value;
    }
    public bool               UseFocus
    {
        get => SharedState.UseFocus;
        set => SharedState.UseFocus = value;
    }
    public decimal            TransmutationSilverPerUnit
    {
        get => SharedState.TransmutationSilverPerUnit;
        set => SharedState.TransmutationSilverPerUnit = value;
    }

    // ── Commands (forwarded from SharedState) ─────────────────────────────────
    public ICommand BuscarItemCommand    => SharedState.BuscarItemCommand;
    public ICommand LimpiarItemCommand   => SharedState.LimpiarItemCommand;
    public ICommand PreviousRecipeCommand => SharedState.PreviousRecipeCommand;
    public ICommand NextRecipeCommand    => SharedState.NextRecipeCommand;
    public ICommand CargarRecursosCommand => SharedState.CargarRecursosCommand;

    // ── QuantityMode forwards ─────────────────────────────────────────────────

    public int QuantityToCraft
    {
        get => QuantityMode.QuantityToCraft;
        set => QuantityMode.QuantityToCraft = value;
    }
    public ObservableCollection<IngredientInventoryM> IngredientInventory => QuantityMode.IngredientInventory;
    public bool     HasIngredientInventory  => QuantityMode.HasIngredientInventory;
    public CraftItemRowM? CraftItemRow      => QuantityMode.CraftItemRow;
    public bool     HasCraftItemRow         => QuantityMode.HasCraftItemRow;
    public JournalRowM?   JournalRow        => QuantityMode.JournalRow;
    public bool     HasJournal              => QuantityMode.HasJournal;
    public ObservableCollection<MaterialResultM> MaterialResults => QuantityMode.MaterialResults;
    public bool     HasResults              => QuantityMode.HasResults;
    public decimal  TotalCost              => QuantityMode.TotalCost;
    public string   ReturnRateLabel        => QuantityMode.ReturnRateLabel;
    public bool     HasFocusCost           => QuantityMode.HasFocusCost;
    public string   FocusPerCraftLabel     => QuantityMode.FocusPerCraftLabel;
    public string   TotalFocusCostLabel    => QuantityMode.TotalFocusCostLabel;
    public string   FocusReductionLabel    => QuantityMode.FocusReductionLabel;
    public int      FocusReductionPercent  => QuantityMode.FocusReductionPercent;
    public decimal  TotalReturnedValue     => QuantityMode.TotalReturnedValue;
    public double   TotalFameBase          => QuantityMode.TotalFameBase;
    public double   TotalFamePremium       => QuantityMode.TotalFamePremium;
    public bool     HasFame                => QuantityMode.HasFame;
    public bool     HasHideoutBadge        => QuantityMode.HasHideoutBadge;
    public decimal  CraftedItemTotalValue  => QuantityMode.CraftedItemTotalValue;
    public decimal  TotalInvestment        => QuantityMode.TotalInvestment;
    public decimal  TotalGain              => QuantityMode.TotalGain;
    public decimal  ProfitLoss             => QuantityMode.ProfitLoss;
    public bool     IsProfitable           => QuantityMode.IsProfitable;
    public decimal  Roi                    => QuantityMode.Roi;
    public decimal  Margin                 => QuantityMode.Margin;
    public decimal  ProfitLossNoFocus      => QuantityMode.ProfitLossNoFocus;
    public decimal  RoiNoFocus             => QuantityMode.RoiNoFocus;
}
