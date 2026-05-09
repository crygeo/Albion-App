using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Representa un ingrediente dentro de una receta de crafteo.
/// Esta es la vista de dominio del ingrediente, independiente del modelo de datos de juego.
/// </summary>
/// <param name="ItemId">Identificador único del ítem ingrediente (ej. "T4_METALBAR").</param>
/// <param name="Count">Cantidad requerida por ciclo de crafteo.</param>
/// <param name="ParticipatesInReturn">
///     Indica si este ingrediente puede ser devuelto parcialmente tras el crafteo
///     (corresponde al atributo <c>maxreturnedresources > 0</c> en el XML del juego).
/// </param>
public sealed record CraftingIngredient(
    IItemBase Item,
    decimal Count,
    bool ParticipatesInReturn
);