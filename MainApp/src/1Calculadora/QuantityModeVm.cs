using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Albion_App.Models;
using Albion_App.ViewModel;
using AlbionApp.Application.UseCases.Crafting;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.Market;
using LibServices.AppConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using Utilidades.Dialogs;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App._1Calculadora;

/// <summary>
/// Manages state and logic for the Quantity crafting mode.
/// Reads shared input state from CraftingSharedStateVm and fires
/// RecalculationDone when a new result is ready.
/// </summary>
public sealed partial class QuantityModeVm : ObservableObject
{
    // ═══════════════════════════════════════════════════════════════════════════
    // DEPENDENCIAS
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly CraftingSharedStateVm       _sharedState;
    private readonly CalculateCraftingCostUseCase _calculateCrafting;
    private readonly CalculateByQuantityUseCase  _calculateByQuantity;
    private readonly IItemDataService            _itemDataService;
    private readonly AppConfigService            _appConfig;
    private readonly IPriceService               _priceService;

    // ═══════════════════════════════════════════════════════════════════════════
    // EVENTO PÚBLICO
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Fires at the end of ApplyQuantityResult — coordinador updates StickyBar.</summary>
    public event Action? RecalculationDone;

    // ═══════════════════════════════════════════════════════════════════════════
    // ESTADO — CARD 3: Cantidades e inventario
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
    // ESTADO — CARD 4: Resultados
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

    public string FocusReductionLabel  => $"(−{FocusReductionPercent}% DB)";

    /// <summary>UseFocus viene de SharedState para el computed HasFocusCost.</summary>
    public bool   HasFocusCost        => _sharedState.UseFocus && FocusPerCraft > 0;
    public string FocusPerCraftLabel  => FocusPerCraft  is int f ? f.ToString("N0") : "—";
    public string TotalFocusCostLabel => TotalFocusCost is int t ? t.ToString("N0") : "—";

    // ── Sobrantes ─────────────────────────────────────────────────────────────

    [ObservableProperty] private decimal _totalReturnedValue;

    // ── Fama ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFame))]
    private double _totalFameBase;

    [ObservableProperty] private double _totalFamePremium;

    public bool HasFame => TotalFameBase > 0;

    // ── Hideout badge ─────────────────────────────────────────────────────────

    public bool HasHideoutBadge => _sharedState.IsHideoutCity && HasResults;

    // ── Rentabilidad ─────────────────────────────────────────────────────────

    public decimal CraftedItemTotalValue =>
        (CraftItemRow?.UnitPrice ?? 0m) * QuantityToCraft;

    public decimal TotalInvestment => TotalCost + (JournalRow?.TotalEmptyValue ?? 0m);

    public decimal TotalGain =>
        CraftedItemTotalValue + TotalReturnedValue + (JournalRow?.TotalFullValue ?? 0m);

    public decimal ProfitLoss  => TotalGain - TotalInvestment;
    public bool    IsProfitable => ProfitLoss > 0;

    // ── Comparación con/sin foco ─────────────────────────────────────────────

    [ObservableProperty] private decimal _roi;
    [ObservableProperty] private decimal _margin;
    [ObservableProperty] private decimal _profitLossNoFocus;
    [ObservableProperty] private decimal _roiNoFocus;

    // ── Último resultado (expuesto al coordinador) ────────────────────────────

    private QuantityResult _lastResult = QuantityResult.Empty;
    internal QuantityResult LastResult => _lastResult;

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public QuantityModeVm(
        CraftingSharedStateVm        sharedState,
        CalculateCraftingCostUseCase calculateCrafting,
        CalculateByQuantityUseCase   calculateByQuantity,
        IItemDataService             itemDataService,
        AppConfigService             appConfig,
        IPriceService                priceService)
    {
        _sharedState         = sharedState;
        _calculateCrafting   = calculateCrafting;
        _calculateByQuantity = calculateByQuantity;
        _itemDataService     = itemDataService;
        _appConfig           = appConfig;
        _priceService        = priceService;

        // Suscribir a cambios del estado compartido
        _sharedState.InputChanged += OnSharedInputChanged;

        // Cuando materiales cambian, notificar HasResults/HasHideoutBadge
        MaterialResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasHideoutBadge));
        };

        // IngredientInventory: gestionar suscripciones de PropertyChanged
        IngredientInventory.CollectionChanged += OnIngredientCollectionChanged;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PARTIAL METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    partial void OnQuantityToCraftChanged(int value)
    {
        RebuildIngredientInventory();
        Recalculate();
    }

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
    // SHARED INPUT HANDLER
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnSharedInputChanged()
    {
        RebuildIngredientInventory();
        Recalculate();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INVENTARIO DE INGREDIENTES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reconstruye Card 3 preservando OwnedCount/UnitPrice del usuario.
    /// Al terminar dispara Recalculate.
    /// </summary>
    private void RebuildIngredientInventory()
    {
        var previousPrices = IngredientInventory
            .ToDictionary(i => i.ItemId, i => i.UnitPrice);

        IngredientInventory.Clear();

        if (_sharedState.CurrentRecipe is null) return;

        int qty = Math.Max(1, QuantityToCraft);

        var savedPrices = _appConfig.Calculator.IngredientPrices;

        foreach (var ingredient in _sharedState.CurrentRecipe.Ingredients)
        {
            var row = new IngredientInventoryM
            {
                Item          = ingredient.Item,
                RequiredCount = ingredient.Count * qty
            };

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
        if (_sharedState.SelectedItem is not null)
        {
            var prevCraftPrice = CraftItemRow?.UnitPrice
                ?? _appConfig.Calculator.CraftItemPrices.GetValueOrDefault(_sharedState.SelectedItem.ItemId);

            var ct          = _sharedState.IngredientCtsToken;
            var apiItemId   = _itemDataService.GetById(_sharedState.SelectedItem.ItemId)?.ApiItemId ?? _sharedState.SelectedItem.ItemId;
            var displayName = _sharedState.SelectedItem.DisplayName;

            var craftRow = new CraftItemRowM
            {
                Item      = _sharedState.BuildIngredientVm(_sharedState.SelectedItem.ItemId, ct),
                UnitPrice = prevCraftPrice
            };

            craftRow.OnAutoPrice       = () => AutoPriceAsync(apiItemId, p => craftRow.UnitPrice = p);
            craftRow.OnOpenPriceDialog = () => OpenPriceDialogAsync(apiItemId, displayName, p => craftRow.UnitPrice = p);

            craftRow.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CraftItemRowM.UnitPrice))
                    SaveCraftItemPrice(_sharedState.SelectedItem.ItemId, craftRow.UnitPrice);
            };

            CraftItemRow = craftRow;
        }

        RebuildJournalRow();
    }

    private void RebuildJournalRow()
    {
        if (_sharedState.SelectedItem is null || _sharedState.RawRecipes.Length == 0)
        {
            JournalRow = null;
            TotalFameBase = TotalFamePremium = 0;
            return;
        }

        var recipeIndex = Math.Min(_sharedState.RecipeIndex, _sharedState.RawRecipes.Length - 1);
        var rawRecipe   = _sharedState.RawRecipes[recipeIndex];

        if (rawRecipe.IsTransmutation)
        {
            JournalRow    = null;
            TotalFameBase = TotalFamePremium = 0;
            return;
        }

        var itemBase   = _itemDataService.GetById(_sharedState.SelectedItem.ItemId);
        var isRefining = itemBase?.RawAttributes
                             .TryGetValue("shopsubcategory1", out var sub) == true
                         && sub == "refinedresources";

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

        var journal = _itemDataService.GetJournalForItem(_sharedState.SelectedItem.ItemId);
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

        var ct = _sharedState.IngredientCtsToken;
        var journalRow = new JournalRowM
        {
            Journal        = journal,
            JournalVm      = _sharedState.BuildIngredientVm(journal.UniqueName, ct),
            BooksNeeded    = booksNeeded,
            BooksCompleted = booksCompleted,
            FullPrice      = prevFull,
            EmptyPrice     = prevEmpty
        };

        var journalApiId = _itemDataService.GetById(journal.UniqueName)?.ApiItemId ?? journal.UniqueName;
        var journalName  = journalRow.DisplayName;

        journalRow.OnAutoPrice = async () =>
        {
            var q = new PriceQuery(journalApiId, _sharedState.CurrentServer, _sharedState.MarketCities, _sharedState.MarketTimeScale);
            var r = await _priceService.GetPricesAsync(q);
            if (!r.Success || r.Prices.Count == 0) return;
            journalRow.FullPrice  = r.Prices[0].AvgPrice;
            journalRow.EmptyPrice = r.Prices[0].MinPrice;
        };

        journalRow.OnOpenPriceDialog = () => OpenPriceDialogAsync(
            journalApiId, journalName,
            p => journalRow.FullPrice = p);

        journalRow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(JournalRowM.FullPrice) or nameof(JournalRowM.EmptyPrice))
                SaveJournalPrices(journal.UniqueName, journalRow.FullPrice, journalRow.EmptyPrice);
        };

        JournalRow = journalRow;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CÁLCULO
    // ═══════════════════════════════════════════════════════════════════════════

    private void Recalculate()
    {
        MaterialResults.Clear();
        if (_sharedState.CurrentRecipe is null) return;

        var city = _sharedState.CurrentCityData;

        var request = BuildRequest(city);
        var result  = _calculateCrafting.Execute(request);
        ApplyResult(result);

        var qRequest = BuildQuantityRequest(city);
        var qResult  = _calculateByQuantity.Execute(qRequest);
        ApplyQuantityResult(qResult);
    }

    private CraftingCostRequest BuildRequest(CraftingCityData? city)
    {
        var itemBase = _sharedState.SelectedItem is null
            ? null
            : _itemDataService.GetById(_sharedState.SelectedItem.ItemId);

        var itemValueStr = itemBase?.RawAttributes.GetValueOrDefault("itemvalue");
        var itemValue    = int.TryParse(itemValueStr, out var iv) ? Math.Max(1, iv) : 1;

        var hideoutBonus = _sharedState.IsHideoutCity
            ? _sharedState.SelectedHideoutLevel.GeneralistBonus
            : 0m;

        return new CraftingCostRequest
        {
            Recipe                     = _sharedState.CurrentRecipe!,
            City                       = city,
            TransmutationSilverPerUnit = _sharedState.IsTransmutation ? _sharedState.TransmutationSilverPerUnit : null,
            Quantity                   = QuantityToCraft,
            CraftingCategory           = itemBase?.CraftingCategory,
            ItemValue                  = itemValue,
            UseFocus                   = _sharedState.UseFocus,
            JournalBonus               = _sharedState.SelectedJournalBonus?.Value ?? 0m,
            HideoutBonus               = hideoutBonus,
            HideoutSpecialistBonus     = _sharedState.IsHideoutCity && _sharedState.UseSpecialistBonus
                                             ? _sharedState.SelectedHideoutLevel.SpecialistBonus
                                             : 0m,
            AchievementBonuses         = _sharedState.RawItemBonuses,
            OwnedStock                 = IngredientInventory
                .Select(i => new IngredientStock(i.ItemId, 0, i.UnitPrice))
                .ToList()
        };
    }

    private QuantityRequest BuildQuantityRequest(CraftingCityData? city)
    {
        var itemBase = _sharedState.SelectedItem is null
            ? null
            : _itemDataService.GetById(_sharedState.SelectedItem.ItemId);

        var itemValueStr = itemBase?.RawAttributes.GetValueOrDefault("itemvalue");
        var itemValue    = int.TryParse(itemValueStr, out var iv) ? Math.Max(1, iv) : 1;

        var isRefining = itemBase?.RawAttributes
                             .TryGetValue("shopsubcategory1", out var sub) == true
                         && sub == "refinedresources";

        var hideoutBonus = _sharedState.IsHideoutCity
            ? _sharedState.SelectedHideoutLevel.GeneralistBonus
            : 0m;

        return new QuantityRequest(
            Recipe:                     _sharedState.CurrentRecipe!,
            Quantity:                   QuantityToCraft,
            City:                       city,
            CraftingCategory:           itemBase?.CraftingCategory,
            ItemValue:                  itemValue,
            UseFocus:                   _sharedState.UseFocus,
            JournalBonus:               _sharedState.SelectedJournalBonus?.Value ?? 0m,
            HideoutBonus:               hideoutBonus,
            HideoutSpecialistBonus:     _sharedState.IsHideoutCity && _sharedState.UseSpecialistBonus
                                            ? _sharedState.SelectedHideoutLevel.SpecialistBonus
                                            : 0m,
            AchievementBonuses:         _sharedState.RawItemBonuses,
            Stock:                      IngredientInventory
                                            .Select(i => new IngredientStock(i.ItemId, 0, i.UnitPrice))
                                            .ToList(),
            TransmutationSilverPerUnit: _sharedState.IsTransmutation ? _sharedState.TransmutationSilverPerUnit : null,
            SalePrice:                  CraftItemRow?.UnitPrice ?? 0m,
            IsRefining:                 isRefining,
            ItemTier:                   itemBase?.Tier,
            EnchantmentLevel:           itemBase?.EnchantmentLevel ?? 0);
    }

    private void ApplyResult(CraftingCostResult result)
    {
        EffectiveReturnRate   = result.ReturnRate;
        FocusPerCraft         = result.FocusPerCraft;
        TotalFocusCost        = result.TotalFocusCost;
        FocusReductionPercent = result.FocusReductionPercent;
        _sharedState.HideoutFocusSaving = result.FocusSpecialistSaving ?? 0;

        if (result.Lines.Count == 0) return;

        var ingredientByItemId = _sharedState.CurrentRecipe!.Ingredients
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
        _lastResult       = result;
        Roi               = result.Roi;
        Margin            = result.Margin;
        ProfitLossNoFocus = result.ProfitLossNoFocus;
        RoiNoFocus        = result.RoiNoFocus;
        RecalculationDone?.Invoke();
    }

    private void ClearCalculationState()
    {
        EffectiveReturnRate   = 0m;
        FocusPerCraft         = null;
        TotalFocusCost        = null;
        FocusReductionPercent = 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RESET (llamado cuando el ítem es limpiado)
    // ═══════════════════════════════════════════════════════════════════════════

    public void Reset()
    {
        IngredientInventory.Clear();
        CraftItemRow       = null;
        JournalRow         = null;
        MaterialResults.Clear();
        TotalCost          = 0;
        TotalReturnedValue = 0;
        TotalFameBase      = 0;
        TotalFamePremium   = 0;
        ClearCalculationState();
        Roi               = 0m;
        Margin            = 0m;
        ProfitLossNoFocus = 0m;
        RoiNoFocus        = 0m;
        _lastResult       = QuantityResult.Empty;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RESTORE TAB STATE
    // ═══════════════════════════════════════════════════════════════════════════

    public void RestoreTabState(CalculatorTabState state)
    {
        QuantityToCraft = state.Quantity;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HANDLERS DE COLECCIONES
    // ═══════════════════════════════════════════════════════════════════════════

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

    private void OnIngredientPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IngredientInventoryM.UnitPrice))
            Recalculate();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRECIO AUTOMÁTICO Y DIÁLOGO
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task AutoPriceAsync(string apiItemId, Action<decimal> applyPrice)
    {
        var query  = new PriceQuery(apiItemId, _sharedState.CurrentServer, _sharedState.MarketCities, _sharedState.MarketTimeScale);
        var result = await _priceService.GetPricesAsync(query);
        if (!result.Success || result.Prices.Count == 0) return;
        applyPrice(result.Prices[0].AvgPrice);
    }

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

        dialogVm.CloseDialog = () =>
            DialogService.Instance.CerrarSiEstaAbiertoYEsperar(dialog);

        var showTask = DialogService.Instance.MostrarDialogo(dialog);

        var result = await _priceService.GetCurrentPricesAsync(
            apiItemId, _sharedState.CurrentServer, _sharedState.MarketCities);

        dialogVm.IsLoading = false;

        if (!result.Success)
            dialogVm.Error = result.Error;
        else
            dialogVm.Prices = result.Prices;

        await showTask;

        if (dialogVm.SelectedPrice.HasValue)
            applyPrice(dialogVm.SelectedPrice.Value);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PERSISTENCIA DE PRECIOS
    // ═══════════════════════════════════════════════════════════════════════════

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
}
