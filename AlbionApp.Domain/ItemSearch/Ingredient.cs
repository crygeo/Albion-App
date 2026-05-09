namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Ingrediente inmutable de una receta de crafteo.
///
/// Record para garantizar igualdad por valor — dos ingredientes con el mismo
/// ítem, count y flag de retorno son idénticos conceptualmente.
/// </summary>
public sealed record Ingredient(
    string ItemId,
    int       Count,
    bool      ParticipatesInReturn)
    : IIngredient;
