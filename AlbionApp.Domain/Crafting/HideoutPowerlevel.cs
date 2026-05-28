namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Bonificaciones de crafteo de un hideout según su nivel de poder.
///
/// Fuente: powerlevels en hideouts.xml del cliente de Albion Online.
///   generalistcraftingbonus  → aplica a TODO crafteo en el hideout.
///   specialistcraftingbonus  → aplica cuando el hideout está especializado para el ítem.
///
/// Nivel 1 no otorga ningún bono de crafteo.
/// </summary>
public sealed record HideoutPowerlevel(
    int     Level,
    decimal GeneralistBonus,
    decimal SpecialistBonus)
{
    // ── Tipos de cluster que tienen bono de hideout ───────────────────────────

    private static readonly HashSet<string> HideoutClusterTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "TUNNEL_HIDEOUT",
            "TUNNEL_HIDEOUT_DEEP",
            "HIDEOUT",
            "OPENPVP_BLACK_1",
            "OPENPVP_BLACK_2",
            "OPENPVP_BLACK_3",
            "OPENPVP_BLACK_4",
            "OPENPVP_BLACK_5",
            "OPENPVP_BLACK_6",
        };

    // ── Tabla oficial de los 9 niveles ────────────────────────────────────────

    public static readonly IReadOnlyList<HideoutPowerlevel> All =
    [
        new(1, 0.00m,   0.0000m),
        new(2, 0.06m,   0.0375m),
        new(3, 0.11m,   0.0750m),
        new(4, 0.15m,   0.1125m),
        new(5, 0.18m,   0.1500m),
        new(6, 0.20m,   0.1875m),
        new(7, 0.22m,   0.2250m),
        new(8, 0.24m,   0.2625m),
        new(9, 0.26m,   0.3000m),
    ];

    /// <summary>Retorna el nivel correspondiente (1-based). Nivel fuera de rango → nivel 1 (sin bono).</summary>
    public static HideoutPowerlevel ForLevel(int level)
        => level >= 1 && level <= All.Count
            ? All[level - 1]
            : All[0];

    /// <summary>True si el tipo de cluster aplica para bono de hideout.</summary>
    public static bool IsHideoutType(string? clusterType)
        => clusterType is not null && HideoutClusterTypes.Contains(clusterType);
}
