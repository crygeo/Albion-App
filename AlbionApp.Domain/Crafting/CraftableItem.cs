namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Vista de dominio de un ítem del juego, enfocada exclusivamente en la información
/// necesaria para el motor de cálculo de crafteo.
/// Completamente desacoplada del modelo de datos de <c>LibAlbionData</c>.
/// </summary>
/// <param name="ItemId">Identificador único, incluye enchantment si aplica (ej. "T4_SWORD@1").</param>
/// <param name="DisplayName">Nombre localizado para mostrar en UI.</param>
/// <param name="CraftingCategory">
///     Categoría de crafteo (ej. "sword", "axe", "armor_plate").
///     Usada para determinar la bonificación de ciudad aplicable.
/// </param>
/// <param name="AmountCrafted">Cantidad de ítems producidos por cada ciclo de crafteo.</param>
/// <param name="CraftingFocusCost">Puntos de foco consumidos por ciclo de crafteo.</param>
/// <param name="Ingredients">Ingredientes requeridos por ciclo de crafteo.</param>
public sealed record CraftableItem(
    string ItemId,
    string DisplayName,
    string? CraftingCategory,
    int AmountCrafted,
    decimal CraftingFocusCost,
    IReadOnlyList<CraftingIngredient> Ingredients
)
{
    /// <summary>Verdadero si el ítem tiene receta de crafteo válida.</summary>
    public bool IsCraftable => Ingredients.Count > 0 && AmountCrafted > 0;

    /// <summary>
    /// Crea un placeholder para un ítem desconocido o no encontrado en el catálogo.
    /// Permite que el árbol de crafteo continúe sin lanzar excepciones.
    /// </summary>
    public static CraftableItem Unknown(string itemId) =>
        new(itemId, itemId, null, 1, 0, []);
}
