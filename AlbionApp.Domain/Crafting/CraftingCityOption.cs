namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Opción de ciudad para el ComboBox de la calculadora.
/// Se reconstruye cada vez que el jugador selecciona un ítem distinto,
/// con el bonus ya calculado para la categoría de crafteo de ese ítem.
///
/// Formato de display: "Lymhurst  +15%"
/// </summary>
public sealed record CraftingCityOption(
    string ClusterId,
    string Name,
    string ClusterType,
    decimal Bonus)
{
    /// <summary>
    /// Texto listo para el ComboBox: "Lymhurst  +15%" o "Martlock  +0%".
    /// </summary>
    public string DisplayText => $"{Name}  +{Bonus * 100:F0}%";
}
