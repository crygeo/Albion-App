namespace AlbionApp.Application.UseCases.Player;

/// <summary>Contribución individual de un nodo al bono agregado.</summary>
public sealed record BonusContribution(string AchievementId, double Value);

/// <summary>
/// Bono acumulado a través de la cadena de padres de un achievement,
/// agrupado por <see cref="Id"/> (DescriptionTag del XML).
///
/// <see cref="Values"/> contiene una entrada por cada nodo que aportó
/// a este grupo. <see cref="Total"/> es la suma de todas las contribuciones.
/// </summary>
public sealed record AggregatedBonus
{
    /// <summary>Tipo de bono. Ej: "craftingfocuscostreduction", "itemcraftquality".</summary>
    public required string Type { get; init; }

    /// <summary>DescriptionTag del XML — clave de localización. Ej: "@DESTINYBOARD_...".</summary>
    public required string Id { get; init; }

    /// <summary>Contribuciones individuales, una por nodo visitado.</summary>
    public IReadOnlyList<BonusContribution> Values { get; init; } = [];

    /// <summary>Suma total de todas las contribuciones.</summary>
    public double Total => Values.Sum(v => v.Value);
}
