namespace AlbionApp.Domain.Achievement;

/// <summary>
/// Datos de un nivel dentro de un <see cref="AlbionTemplate"/>.
///
/// Cada nivel (1-100) tiene su propio umbral de Fama y coste de LP.
/// La estructura en el XML es:
/// <c>Fame;LP;MissionTargetMinTier;MissionTargetMaxTier;MissionItemMinTier;MissionItemMaxTier;UnlockTier</c>
/// </summary>
public sealed record AlbionAchievementLevel
{
    /// <summary>Fama acumulada necesaria para llegar a este nivel.</summary>
    public int Fame { get; init; }

    /// <summary>Puntos de aprendizaje (LP) que otorga este nivel.</summary>
    public int LP { get; init; }
}
