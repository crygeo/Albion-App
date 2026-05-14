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
    /// Datos de los 100 niveles del template.
    /// <c>Levels[0]</c> = nivel 1, <c>Levels[99]</c> = nivel 100.
    /// </summary>
    public IReadOnlyList<AlbionAchievementLevel> Levels { get; init; } = [];
}
