namespace AlbionApp.Domain.Achievement;

/// <summary>
/// Bono de rendimiento definido en <c>&lt;baserewards&gt;&lt;bonus&gt;</c> del XML.
///
/// Ejemplo XML:
///   <c>&lt;bonus type="craftingfocuscostreduction" bonus="0.3" mintier="4" maxtier="8"/&gt;</c>
///
/// Fórmula: <c>bonusTotal = niveles * (BonusValue * 100)</c>
/// </summary>
public sealed record AchievementBonus
{
    /// <summary>Tipo de bono. Ej: "craftingfocuscostreduction".</summary>
    public required string Type { get; init; }

    /// <summary>Valor del bono por nivel, en escala decimal. Ej: 0.3, 2.5.</summary>
    public double BonusValue { get; init; }

    /// <summary>Tier mínimo al que aplica. Ej: 4.</summary>
    public int MinTier { get; init; }

    /// <summary>Tier máximo al que aplica. Ej: 8.</summary>
    public int MaxTier { get; init; }

    /// <summary>Clave de localización para la descripción. Ej: "@DESTINYBOARD_...".</summary>
    public string? DescriptionTag { get; init; }

    /// <summary>
    /// Patrones de ítem a los que aplica este bonus. Ej: "T?_CLOTH*", "T?_OFF_SHIELD_AVALON".
    /// <c>T?</c> = cualquier tier · <c>*</c> = cualquier sufijo de encantamiento.
    /// </summary>
    public IReadOnlyList<string> ItemPatterns { get; init; } = [];

    /// <summary>
    /// Calcula el bono acumulado para un número de niveles dado.
    /// </summary>
    /// <summary>
    /// Resultado en puntos de porcentaje (p.ej. 2.15 = 2.15% de reduccion).
    /// Para convertir a fraccion decimal dividir entre 100.
    /// </summary>
    public double Calculate(int levels) => levels * BonusValue;
}
