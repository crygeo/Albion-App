using AlbionApp.Domain.Achievement;

namespace AlbionApp.Application.UseCases.Player;

public sealed class PlayerAchievement
{
    public int Index { get; init; }
    public string Id { get; init; } = null!;
    public string? TitleLocalizationKey { get; init; }

    /// <summary>Compatibilidad temporal: usar <see cref="TitleLocalizationKey"/>.</summary>
    [Obsolete("Use TitleLocalizationKey.")]
    public string? NameLocalization => TitleLocalizationKey;

    /// <summary>
    /// Nivel máximo, copiado del dominio en build time.
    /// 100 para la mayoría; 120 para armas (COMBAT_SPEC / COMBAT_OFF_SPEC).
    /// </summary>
    public int MaxLevel { get; init; } = 100;

    /// <summary>Nivel efectivo del jugador (ya resuelto: si IsAtMaxLevel → MaxLevel).</summary>
    public short Level { get; set; }

    /// <summary>True si el servidor reportó este achievement en el array de máximos (Key 1).</summary>
    public bool IsAtMaxLevel { get; set; }

    /// <summary>
    /// Bonos de rendimiento del achievement. Copiados del catálogo estático en build time.
    /// Usar <see cref="AchievementBonus.Calculate"/> con <see cref="Level"/> para el total actual.
    /// </summary>
    public IReadOnlyList<AchievementBonus> Bonuses { get; init; } = [];
}
