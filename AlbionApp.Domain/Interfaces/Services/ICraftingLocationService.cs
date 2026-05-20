using AlbionApp.Domain.Crafting;

namespace AlbionApp.Domain.Interfaces.Services;

/// <summary>
/// Catálogo de ciudades de crafteo con sus bonificaciones.
/// Disponible tras el arranque del servicio; todas las lecturas son O(1).
/// </summary>
public interface ICraftingLocationService
{
    /// <summary>
    /// Todas las ciudades de crafteo del juego con sus modificadores.
    /// </summary>
    IReadOnlyList<CraftingCityData> Cities { get; }
}
