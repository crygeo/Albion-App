namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Receta de crafteo inmutable.
///
/// Los ingredientes son <see cref="IIngredient"/> para permitir que VMs
/// enriquecidos (con imagen) sean usados por la misma receta en la UI sin
/// necesidad de convertir.
/// </summary>
public sealed record Recipe(
    int                        AmountCrafted,
    int?                       CraftingFocus,
    decimal?                   Silver,
    IReadOnlyList<IIngredient> Ingredients,
    bool                       IsTransmutation = false)
    : IRecipe;
