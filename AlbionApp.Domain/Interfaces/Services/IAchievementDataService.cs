using AlbionApp.Domain.Achievement;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Domain.Interfaces.Services;

/// <summary>
/// Contrato del catálogo de achievements de Albion Online.
///
/// Tres diccionarios preconstruidos (FrozenDictionary, lock-free):
/// <list type="bullet">
///   <item><see cref="Templates"/> — curvas de Fama/LP por nombre de template.</item>
///   <item><see cref="ById"/> — achievements indexados por ID string (ej. "CRAFT_REFINE_FIBER_T4").</item>
///   <item><see cref="ByOrdinal"/> — achievements indexados por ordinal numérico
///         (el mismo ID que manda el servidor en FullAchievementInfo evento 151).</item>
/// </list>
/// </summary>
public interface IAchievementDataService
{
    /// <summary>
    /// Templates de niveles indexados por nombre (ej. "REFINE_T4", "COMBAT_SPEC").
    /// Define la curva de Fama y LP de todos los achievements que los usan.
    /// </summary>
    IReadOnlyDictionary<string, AlbionTemplate> Templates { get; }

    /// <summary>
    /// Todos los achievements (achievement + templateachievement) indexados por ID string.
    /// Incluye tanto nodos raíz ("ADVENTURER_MASTER") como specs ("CRAFT_REFINE_FIBER_T4").
    /// </summary>
    IReadOnlyDictionary<string, AlbionAchievement> ById { get; }

    /// <summary>
    /// Todos los templateachievements indexados por su ID numérico (ordinal de lectura).
    /// Clave directa del evento 151 — <c>ByOrdinal[entry.Id]</c> da el achievement.
    /// </summary>
    IReadOnlyDictionary<int, AlbionAchievement> ByOrdinal { get; }

    /// <summary>
    /// Índice compuesto (spriteReward, tier) para localizar achievements de crafteo/refinación.
    /// Clave: <c>"{spriteReward.ToLower()}_{tier}"</c> — ej: <c>"planks_7"</c>.
    /// </summary>
    IReadOnlyDictionary<string, AlbionAchievement> BySpriteAndTier { get; }

    /// <summary>
    /// Lista plana de todos los (achievementId, bonus) que tienen ItemPatterns.
    /// Usada para el scan global en ProcessPlayerUseCase.GetBonusesForItem.
    /// </summary>
    IReadOnlyList<(string AchievementId, AchievementBonus Bonus)> BonusLookup { get; }

    AlbionAchievement? FindByItem(ItemBase item);

    // Los niveles del jugador se resuelven en ProcessPlayerUseCase para no mutar
    // el catálogo estático de achievements.
}
