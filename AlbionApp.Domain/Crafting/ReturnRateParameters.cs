namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Parámetros que determinan la tasa de retorno de materiales al craftear.
/// Encapsula todas las variables del jugador y la estación que afectan al retorno.
/// </summary>
/// <param name="City">
///     Ciudad donde se craftea. Sólo relevante cuando
///     <paramref name="StationType"/> es <see cref="CraftingStationType.CityWorkshop"/>.
/// </param>
/// <param name="StationType">Tipo de estación (taller de ciudad o hideout).</param>
/// <param name="UseFocus">Indica si el jugador usa Focus al craftear.</param>
/// <param name="FocusReturnBonus">
///     Porcentaje adicional de retorno obtenido al usar Focus (valor entre 0 y 1).
///     En el juego típicamente ~0.435 para ítems estándar, aunque varía según tier.
///     Configurable para permitir correcciones sin recompilar.
/// </param>
/// <param name="HideoutLevel">
///     Nivel del Hideout (1–3). Sólo relevante cuando
///     <paramref name="StationType"/> es <see cref="CraftingStationType.Hideout"/>.
/// </param>
/// <param name="HideoutZoneType">
///     Tipo de zona del Hideout (Blue/Yellow/Red/Black). Sólo relevante en Hideout.
/// </param>
/// <param name="ExtraBonus">
///     Bonus adicional configurable: 0.0, 0.10 o 0.20 según configuración de la estación.
/// </param>
public sealed record ReturnRateParameters(
    CraftingCity City,
    CraftingStationType StationType,
    bool UseFocus,
    decimal FocusReturnBonus,
    int HideoutLevel,
    ZoneType HideoutZoneType,
    decimal ExtraBonus
)
{
    /// <summary>
    /// Valores por defecto: taller de ciudad sin foco ni bonos extra,
    /// útil como punto de partida para la UI.
    /// </summary>
    public static ReturnRateParameters Default { get; } = new(
        City: CraftingCity.None,
        StationType: CraftingStationType.CityWorkshop,
        UseFocus: false,
        FocusReturnBonus: 0.435m,
        HideoutLevel: 1,
        HideoutZoneType: ZoneType.Blue,
        ExtraBonus: 0m
    );
}
