using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Application.UseCases.Crafting;

public sealed record QuantityRequest(
    IRecipe                        Recipe,
    int                            Quantity,
    CraftingCityData?              City,
    string?                        CraftingCategory,
    int                            ItemValue,
    bool                           UseFocus,
    decimal                        JournalBonus,
    decimal                        HideoutBonus,
    decimal                        HideoutSpecialistBonus,
    IReadOnlyList<AggregatedBonus> AchievementBonuses,
    IReadOnlyList<IngredientStock> Stock,
    decimal?                       TransmutationSilverPerUnit,
    decimal                        SalePrice,
    // Campos para cómputo de fama:
    bool                           IsRefining       = false,
    int?                           ItemTier         = null,
    int                            EnchantmentLevel = 0);

public sealed record QuantityResult
{
    public decimal ReturnRate { get; init; }
    public int? FocusPerCraft { get; init; }
    public int? TotalFocusCost { get; init; }
    public int FocusReductionPercent { get; init; }
    public int? FocusSpecialistSaving { get; init; }
    public IReadOnlyList<MaterialCostLine> Lines { get; init; } = [];
    public decimal TotalCost { get; init; }
    public decimal TransmutationSilverCost { get; init; }
    public decimal ProfitLoss { get; init; }
    public decimal Roi { get; init; }
    public decimal Margin { get; init; }
    public decimal ProfitLossNoFocus { get; init; }
    public decimal RoiNoFocus { get; init; }
    public double FameBase { get; init; }
    public double FamePremium { get; init; }

    public static readonly QuantityResult Empty = new();
}

public sealed class CalculateByQuantityUseCase
{
    private readonly IItemDataService _itemDataService;

    public CalculateByQuantityUseCase(IItemDataService itemDataService)
    {
        _itemDataService = itemDataService;
    }

    public QuantityResult Execute(QuantityRequest request)
    {
        var qty = Math.Max(1, request.Quantity);

        if (request.Recipe.IsTransmutation)
            return HandleTransmutation(request, qty);

        var focus = CraftingEngineHelpers.ComputeFocus(
            request.Recipe, request.UseFocus, qty,
            request.AchievementBonuses, request.HideoutSpecialistBonus);

        if (request.City is null)
            return new QuantityResult
            {
                FocusPerCraft         = focus.PerCraft,
                TotalFocusCost        = focus.Total,
                FocusReductionPercent = focus.ReductionPercent,
                FocusSpecialistSaving = focus.SpecialistSaving,
            };

        return HandleCraftingWithCity(request, qty, focus);
    }

    private static QuantityResult HandleTransmutation(QuantityRequest request, int qty)
    {
        var silverPerUnit = request.TransmutationSilverPerUnit ?? request.Recipe.Silver ?? 0m;
        var silverCost    = silverPerUnit * qty;
        var lines = CraftingKernel.Compute(new KernelRequest(
            Recipe:      request.Recipe,
            Quantity:    qty,
            ReturnRate:  0m,
            Stock:       request.Stock,
            BuyLocation: request.City?.Name ?? string.Empty));

        var txTotalCost = silverCost + lines.TotalCost;
        var saleValue   = request.SalePrice * qty;
        var returnedValue = lines.Lines.Sum(l => l.ReturnedValue);
        var txProfit    = saleValue + returnedValue - txTotalCost;
        var txRoi       = txTotalCost > 0 ? txProfit / txTotalCost : 0m;
        var txMargin    = saleValue > 0 ? txProfit / saleValue  : 0m;

        return new QuantityResult
        {
            TransmutationSilverCost = silverCost,
            Lines                   = lines.Lines,
            TotalCost               = txTotalCost,
            ProfitLoss              = txProfit,
            Roi                     = txRoi,
            Margin                  = txMargin,
            ProfitLossNoFocus       = txProfit,
            RoiNoFocus              = txRoi,
        };
    }

    private QuantityResult HandleCraftingWithCity(QuantityRequest request, int qty, FocusResult focus)
    {
        // ── Tasa de retorno ──────────────────────────────────────────────────
        var rrr = CraftingEngineHelpers.ComputeReturnRate(
            request.City, request.CraftingCategory, request.UseFocus,
            request.JournalBonus, request.HideoutBonus, request.AchievementBonuses);

        // ── Kernel primario (con foco/RRR actuales) ──────────────────────────
        var primaryKernel = CraftingKernel.Compute(new KernelRequest(
            Recipe:      request.Recipe,
            Quantity:    qty,
            ReturnRate:  rrr,
            Stock:       request.Stock,
            BuyLocation: request.City!.Name));

        // ── Kernel sin foco (para comparación) ──────────────────────────────
        var rrrNoFocus = CraftingEngineHelpers.ComputeReturnRate(
            request.City, request.CraftingCategory, useFocus: false,
            request.JournalBonus, request.HideoutBonus, request.AchievementBonuses);

        var noFocusKernel = CraftingKernel.Compute(new KernelRequest(
            Recipe:      request.Recipe,
            Quantity:    qty,
            ReturnRate:  rrrNoFocus,
            Stock:       request.Stock,
            BuyLocation: request.City.Name));

        // ── Financieros ──────────────────────────────────────────────────────
        var saleValue              = request.SalePrice * qty;
        var returnedValue          = primaryKernel.Lines.Sum(l => l.ReturnedValue);
        var returnedValueNoFocus   = noFocusKernel.Lines.Sum(l => l.ReturnedValue);

        var profitLoss        = saleValue + returnedValue          - primaryKernel.TotalCost;
        var profitLossNoFocus = saleValue + returnedValueNoFocus   - noFocusKernel.TotalCost;

        var roi        = primaryKernel.TotalCost > 0 ? profitLoss        / primaryKernel.TotalCost  : 0m;
        var roiNoFocus = noFocusKernel.TotalCost > 0 ? profitLossNoFocus / noFocusKernel.TotalCost  : 0m;
        var margin     = saleValue > 0 ? profitLoss / saleValue : 0m;

        // ── Fama ─────────────────────────────────────────────────────────────
        // Preservar el comportamiento de CalculadoraSvm.RebuildJournalRow:
        //   FameBase → CalculateRefiningFame(base) o CalculateCraftFame(base)
        //   FamePremium → CalculateCraftFame(premium: true) siempre (incluso para refining)

        // Ciclos reales de crafteo: si AmountCrafted > 1, la fama se reparte entre ítems producidos.
        // Ej: AmountCrafted=5, qty=12 → necesita 3 ciclos (ceil(12/5)).
        int cycles = request.Recipe.AmountCrafted > 1
            ? (int)Math.Ceiling((double)qty / request.Recipe.AmountCrafted)
            : qty;

        var artifactType = FameCalculator.ResolveArtifactType(request.Recipe.Ingredients, _itemDataService);
        double fameBase;
        if (request.IsRefining && request.ItemTier is int tier)
            fameBase = FameCalculator.CalculateRefiningFame(tier, request.EnchantmentLevel, premium: false);
        else
            fameBase = FameCalculator.CalculateCraftFame(request.Recipe.Ingredients, artifactType, premium: false);

        var famePremium = FameCalculator.CalculateCraftFame(request.Recipe.Ingredients, artifactType, premium: true);

        return new QuantityResult
        {
            ReturnRate            = rrr,
            FocusPerCraft         = focus.PerCraft,
            TotalFocusCost        = focus.Total,
            FocusReductionPercent = focus.ReductionPercent,
            FocusSpecialistSaving = focus.SpecialistSaving,
            Lines                 = primaryKernel.Lines,
            TotalCost             = primaryKernel.TotalCost,
            ProfitLoss            = profitLoss,
            Roi                   = roi,
            Margin                = margin,
            ProfitLossNoFocus     = profitLossNoFocus,
            RoiNoFocus            = roiNoFocus,
            FameBase              = fameBase    * cycles,
            FamePremium           = famePremium * cycles,
        };
    }
}
