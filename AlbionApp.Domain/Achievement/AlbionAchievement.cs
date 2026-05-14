namespace AlbionApp.Domain.Achievement;

/// <summary>
/// Achievement o templateachievement del árbol de especialización de Albion Online.
///
/// Cubre tanto los nodos raíz (<c>&lt;achievement&gt;</c>) como los nodos de
/// especialización (<c>&lt;templateachievement&gt;</c>).
///
/// Identificación dual:
///   • <see cref="OrdinalId"/> — ID numérico que usa el servidor en el evento 151.
///     Es la posición del elemento en el XML ignorando los <c>&lt;template&gt;</c>.
///     Solo se asigna a <c>templateachievement</c>; los <c>achievement</c> puros tienen -1.
///   • <see cref="Id"/> — ID string del XML (ej. "CRAFT_REFINE_FIBER_T4").
/// </summary>
public sealed record AlbionAchievement
{
    /// <summary>
    /// ID numérico asignado por orden de lectura (ignorando templates).
    /// Coincide con el ID que manda el servidor en FullAchievementInfo (evento 151).
    /// -1 para nodos <c>&lt;achievement&gt;</c> que no son templateachievement.
    /// </summary>
    public int OrdinalId { get; init; } = -1;

    /// <summary>ID string del XML. Ej: "CRAFT_REFINE_FIBER_T4", "ADVENTURER_MASTER".</summary>
    public required string Id { get; init; }

    /// <summary> TITLE string del XML, para obtener el nombre  </summary>
    public required string NameLocalization { get; init; }

    /// <summary>
    /// Nombre del template que define la curva de Fama/LP.
    /// Null para nodos <c>&lt;achievement&gt;</c> puros.
    /// </summary>
    public string? UseTemplate { get; init; }

    /// <summary>
    /// IDs de los achievements vecinos en el árbol de especialización.
    /// Incluye tanto el padre inmediato como el hijo siguiente (conexión bidireccional).
    /// </summary>
    public IReadOnlyList<string> Parents { get; init; } = [];

    // Los niveles del jugador no viven aquí: este record representa catálogo estático.
    // ProcessPlayerUseCase mantiene el snapshot player-specific como id string -> nivel.
}