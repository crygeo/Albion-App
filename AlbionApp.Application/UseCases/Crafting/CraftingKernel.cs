using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Application.UseCases.Crafting;

internal sealed record KernelRequest(
    IRecipe                        Recipe,
    int                            Quantity,
    decimal                        ReturnRate,
    IReadOnlyList<IngredientStock> Stock,
    string                         BuyLocation);

internal sealed record KernelResult(
    IReadOnlyList<MaterialCostLine> Lines,
    decimal                         TotalCost);

internal static class CraftingKernel
{
    internal static KernelResult Compute(KernelRequest r)
    {
        var lines     = BuildLines(r);
        var totalCost = lines.Sum(l => l.TotalCost);
        return new KernelResult(lines, totalCost);
    }

    private static IReadOnlyList<MaterialCostLine> BuildLines(KernelRequest r)
    {
        var stockIndex = r.Stock
            .ToDictionary(s => s.ItemId, StringComparer.OrdinalIgnoreCase);

        var lines = new List<MaterialCostLine>(r.Recipe.Ingredients.Count);

        foreach (var ingredient in r.Recipe.Ingredients)
        {
            stockIndex.TryGetValue(ingredient.ItemId, out var stock);

            int perCycle = ingredient.Count;
            int gross    = perCycle * r.Quantity;

            decimal netFactor = 1m - r.ReturnRate;
            int minInitial;
            int lastBuffer;

            if (!ingredient.ParticipatesInReturn)
            {
                minInitial = gross;
                lastBuffer = 0;
            }
            else if (r.Quantity == 1)
            {
                minInitial = perCycle;
                lastBuffer = (int)Math.Floor(perCycle * r.ReturnRate);
            }
            else
            {
                int netConsumed = (int)Math.Ceiling(gross    * netFactor);
                lastBuffer      = (int)Math.Ceiling(perCycle * netFactor);
                minInitial      = netConsumed + lastBuffer;
            }

            int returnedTotal = ingredient.ParticipatesInReturn ? lastBuffer : 0;

            lines.Add(new MaterialCostLine(
                ItemId:           ingredient.ItemId,
                GrossQuantity:    gross,
                NetToBuy:         minInitial,
                ReturnedQuantity: ingredient.ParticipatesInReturn ? returnedTotal : 0,
                BuyLocation:      r.BuyLocation,
                UnitPrice:        stock?.UnitPrice ?? 0m));
        }

        return lines;
    }
}

internal sealed record FocusResult(
    int? PerCraft,
    int? Total,
    int  ReductionPercent,
    int? SpecialistSaving = null)
{
    public static readonly FocusResult None = new(null, null, 0);
}

internal static class CraftingEngineHelpers
{
    private const string FocusReductionBonusType   = "craftingfocuscostreduction";
    private const string ReturnRateAchievementType = ReturnRateCalculator.AchievementReturnBonusType;

    internal static decimal SumBonus(IReadOnlyList<AggregatedBonus> bonuses, string bonusType)
        => (decimal)bonuses.Where(b => b.Type == bonusType).Sum(b => b.Total) / 100m;

    internal static decimal ComputeReturnRate(
        CraftingCityData               city,
        string?                        craftingCategory,
        bool                           useFocus,
        decimal                        journalBonus,
        decimal                        hideoutBonus,
        IReadOnlyList<AggregatedBonus> achievementBonuses)
    {
        var achievementBonus = useFocus
            ? SumBonus(achievementBonuses, ReturnRateAchievementType)
            : 0m;

        return ReturnRateCalculator.Calculate(
            city,
            craftingCategory,
            useFocus,
            achievementBonus,
            journalBonus,
            hideoutBonus);
    }

    internal static FocusResult ComputeFocus(
        IRecipe                        recipe,
        bool                           useFocus,
        int                            qty,
        IReadOnlyList<AggregatedBonus> achievementBonuses,
        decimal                        hideoutSpecialistBonus)
    {
        if (!useFocus || recipe.CraftingFocus is not > 0)
            return FocusResult.None;

        var dbExponent     = (double)SumBonus(achievementBonuses, FocusReductionBonusType);
        var focusFactor    = Math.Pow(0.5, dbExponent);
        var amountPerCraft = Math.Max(1, recipe.AmountCrafted);
        var baseFocus      = (double)recipe.CraftingFocus!.Value;
        var effective      = baseFocus * focusFactor;

        var focusPerCraft = Math.Max(1, (int)Math.Floor(effective));
        var totalFocus    = Math.Max(1, (int)Math.Ceiling(effective * qty / amountPerCraft));

        int? specialistSaving = null;
        if (hideoutSpecialistBonus > 0)
        {
            var specialistExponent = dbExponent + (double)hideoutSpecialistBonus;
            var specialistFactor   = Math.Pow(0.5, specialistExponent);
            var specialistPerCraft = Math.Max(1, (int)Math.Floor(baseFocus * specialistFactor));

            specialistSaving = focusPerCraft - specialistPerCraft;
            focusPerCraft    = specialistPerCraft;
            totalFocus       = Math.Max(1, (int)Math.Ceiling(
                                   baseFocus * specialistFactor * qty / amountPerCraft));
        }

        return new FocusResult(
            PerCraft:         focusPerCraft,
            Total:            totalFocus,
            ReductionPercent: (int)Math.Round((1.0 - focusFactor) * 100),
            SpecialistSaving: specialistSaving);
    }
}
