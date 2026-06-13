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
