namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Datos de una ciudad de crafteo: nombre y modificadores disponibles.
/// Producido por ICraftingLocationService al arranque, inmutable.
/// </summary>
public sealed record CraftingCityData(
    string ClusterId,
    string Name,
    decimal RefiningBonus,
    decimal CraftingBonus,
    IReadOnlyList<CraftingModifier> Modifiers)
{
    /// <summary>
    /// Retorna el bonus específico para una categoría de crafteo (ej: "leather_shoes").
    /// 0 si la ciudad no tiene modificador para esa categoría.
    /// </summary>
    public decimal GetBonusFor(string? craftingCategory)
    {
        if (string.IsNullOrWhiteSpace(craftingCategory))
            return 0m;

        return Modifiers
            .FirstOrDefault(m => string.Equals(m.Name, craftingCategory, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? 0m;
    }
}

/// <summary>Bonus de crafteo para una categoría específica de ítem.</summary>
public sealed record CraftingModifier(string Name, decimal Value);
