namespace AlbionApp.Domain.Achievement;

/// <summary>
/// Template de achievement — define la curva de niveles (Fama/LP) que
/// comparten todos los achievements que lo usan.
///
/// XML: <c>&lt;template name="REFINE_T4"&gt;</c>
///
/// Ejemplos de templates: COMBAT_BASE, COMBAT_SPEC, CRAFT_BASE, CRAFT_SPEC,
/// REFINE_T4..T8, GATHER_T4..T8, FARM_BASE, etc.
/// </summary>
public sealed record AlbionTemplate
{
    /// <summary>Nombre del template (clave primaria). Ej: "REFINE_T4", "COMBAT_SPEC".</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Datos de los 100 niveles base del template.
    /// <c>Levels[0]</c> = nivel 1, <c>Levels[99]</c> = nivel 100.
    /// </summary>
    public IReadOnlyList<AlbionAchievementLevel> Levels { get; init; } = [];

    /// <summary>
    /// Niveles de élite que extienden el máximo más allá de 100.
    /// Solo presentes en templates de armas (COMBAT_SPEC, COMBAT_OFF_SPEC).
    /// XML: <c>&lt;elitelevels structure="Fame;SilverPerReSpec"&gt;</c>
    /// Para estos niveles, el campo <see cref="AlbionAchievementLevel.LP"/>
    /// representa el coste de SilverPerReSpec.
    /// </summary>
    public IReadOnlyList<AlbionAchievementLevel> EliteLevels { get; init; } = [];

    /// <summary>
    /// Nivel máximo alcanzable con este template.
    /// 100 para templates estándar; 100 + N para templates con élite (ej. 120).
    /// </summary>
    public int MaxLevel => Levels.Count + EliteLevels.Count;
}
