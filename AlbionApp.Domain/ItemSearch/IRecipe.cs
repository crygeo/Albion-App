namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Contrato de una receta de crafteo de Albion Online.
///
/// Un ítem puede tener cero, una o varias recetas (e.g., crafteo base y upgrade).
/// <see cref="ItemDataService"/> almacena el array en
/// <c>FrozenDictionary&lt;string, IItemRecipe[]&gt;</c> — array vacío cuando no
/// hay receta, nunca null.
///
/// Implementado por:
/// - <see cref="Recipe"/> (record inmutable del dominio)
/// - Future: <c>ItemRecipeVm</c> (wrapper WPF con estado de carga de imágenes)
/// </summary>
public interface IRecipe
{
    /// <summary>Unidades producidas por ciclo de crafteo.</summary>
    int AmountCrafted { get; }

    /// <summary>Coste en Crafting Focus (null si no aplica).</summary>
    int? CraftingFocus { get; }

    /// <summary>Coste en plata (null si no aplica).</summary>
    decimal? Silver { get; }

    /// <summary>
    /// Lista de ingredientes requeridos.
    /// Nunca null; vacío si la receta no tiene ingredientes (e.g. recetas especiales).
    /// </summary>
    IReadOnlyList<IIngredient> Ingredients { get; }

    /// <summary>
    /// True cuando la receta es una transmutación (craftbuttonlocaoverride = TRANSMUTE).
    /// Las recetas de transmutación solo cuestan plata — ciudad, bonus, premium y foco no aplican.
    /// </summary>
    bool IsTransmutation { get; }
}
