using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Application.UseCases.Player;

/// <summary>
/// Mantiene el snapshot de niveles del jugador separado del catálogo estático.
/// </summary>
public sealed class ProcessPlayerUseCase
{
    public event EventHandler? AchievementsUpdated;

    //Dependencias
    private readonly IAchievementDataService _achievementDataService;

    private IReadOnlyDictionary<string, PlayerAchievement> _achievementsById;
    private IReadOnlyDictionary<int, PlayerAchievement> _achievementsByIndex;
    public IReadOnlyDictionary<string, PlayerAchievement> AchievementLevelsById => _achievementsById;
    public IReadOnlyDictionary<int, PlayerAchievement> AchievementLevelsByIndex => _achievementsByIndex;

    public ProcessPlayerUseCase(IAchievementDataService achievementDataService)
    {
        _achievementDataService = achievementDataService ?? throw new ArgumentNullException(nameof(achievementDataService));

        var buil = BuildAchievements();

        _achievementsById = buil.Item1;
        _achievementsByIndex = buil.Item2;
    }


    /// <param name="entries">
    /// Tuplas (OrdinalId, Level, IsAtMaxLevel) que vienen del evento 151.
    /// La capa de Infrastructure mapea <c>PlayerAchievementEntry</c> a estas tuplas
    /// antes de llamar al use case, manteniendo la separación de capas.
    /// </param>
    public void Execute(IEnumerable<(int OrdinalId, int Level, bool IsAtMaxLevel)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var (ordinalId, level, isAtMaxLevel) in entries)
        {
            if (!_achievementsByIndex.TryGetValue(ordinalId, out var existing)) continue;

            existing.IsAtMaxLevel = isAtMaxLevel;
            existing.Level = isAtMaxLevel ? (short)existing.MaxLevel : (short)level;
        }

        AchievementsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private (IReadOnlyDictionary<string, PlayerAchievement>, IReadOnlyDictionary<int, PlayerAchievement>) BuildAchievements()
    {
        Dictionary<string, PlayerAchievement> achievementsId = [];
        Dictionary<int, PlayerAchievement> achievementsIndex = [];
        foreach (var (key, value) in _achievementDataService.ByOrdinal)
        {
            var PA = new PlayerAchievement
            {
                Index            = value.OrdinalId,
                Id               = value.Id,
                TitleLocalizationKey = value.TitleLocalizationKey,
                MaxLevel         = value.MaxLevel,
                Bonuses          = value.Bonuses,
                Level            = 0,
                IsAtMaxLevel     = false,
            };

            achievementsId[value.Id] = PA;
            achievementsIndex[value.OrdinalId] = PA;
        }

        return (achievementsId, achievementsIndex);
    }

    public PlayerAchievement? GetAchievement(string achievementId)
        => _achievementsById.GetValueOrDefault(achievementId);

    /// <summary>
    /// Calcula el bono acumulado para un achievement, sumando las contribuciones
    /// de todos los nodos en su cadena de padres que comparten el mismo tipo de bono.
    ///
    /// Ejemplo: CRAFT_REFINE_WOOD_T8 acumula el bonus de T4 + T5 + T6 + T7 + T8.
    /// </summary>
    public IReadOnlyList<AggregatedBonus> GetBonusesFor(string achievementId)
    {
        // key → (Type, contribuciones acumuladas)
        var groups  = new Dictionary<string, (string Type, List<BonusContribution> Contribs)>(
                          StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue   = new Queue<string>();
        queue.Enqueue(achievementId);

        while (queue.TryDequeue(out var currentId))
        {
            if (!visited.Add(currentId)) continue;
            if (!_achievementDataService.ById.TryGetValue(currentId, out var catalog)) continue;
            if (!_achievementsById.TryGetValue(currentId, out var player)) continue;

            foreach (var bonus in catalog.Bonuses)
            {
                var key = bonus.DescriptionTag ?? bonus.Type;

                if (!groups.TryGetValue(key, out var group))
                {
                    group = (bonus.Type, []);
                    groups[key] = group;
                }

                group.Contribs.Add(new BonusContribution(currentId, bonus.Calculate(player.Level)));
            }

            foreach (var parentId in catalog.Parents)
                queue.Enqueue(parentId);
        }

        return groups
            .Select(kvp => new AggregatedBonus
            {
                Type   = kvp.Value.Type,
                Id     = kvp.Key,
                Values = kvp.Value.Contribs,
            })
            .ToList();
    }

    /// <summary>
    /// Calcula todos los bonuses que aplican a un item específico.
    ///
    /// Escanea TODOS los achievements buscando bonuses cuyos <c>ItemPatterns</c>
    /// hagan match con el item Y cuyo <c>mintier..maxtier</c> cubra el tier del item.
    /// Agrupa por <c>DescriptionTag</c> igual que <see cref="GetBonusesFor"/>.
    /// </summary>
    public IReadOnlyList<AggregatedBonus> GetBonusesForItem(ItemBase item)
    {
        var groups = new Dictionary<string, (string Type, List<BonusContribution> Contribs)>(
                         StringComparer.OrdinalIgnoreCase);

        var tier   = item.Tier ?? 0;
        var itemId = item.ItemId.ToUpperInvariant();

        foreach (var (achievementId, bonus) in _achievementDataService.BonusLookup)
        {
            if (tier < bonus.MinTier || tier > bonus.MaxTier) continue;
            if (!bonus.ItemPatterns.Any(p => WildcardMatch(p, itemId))) continue;
            if (!_achievementsById.TryGetValue(achievementId, out var player)) continue;

            var key = bonus.DescriptionTag ?? bonus.Type;
            if (!groups.TryGetValue(key, out var group))
            {
                group = (bonus.Type, []);
                groups[key] = group;
            }

            group.Contribs.Add(new BonusContribution(achievementId, bonus.Calculate(player.Level)));
        }

        return groups
            .Select(kvp => new AggregatedBonus
            {
                Type   = kvp.Value.Type,
                Id     = kvp.Key,
                Values = kvp.Value.Contribs,
            })
            .ToList();
    }

    /// <summary>
    /// Compara un patrón tipo glob contra un string.
    /// Soporta <c>?</c> (un carácter cualquiera) y <c>*</c> (cualquier secuencia).
    /// Comparación case-insensitive — el caller debe pasar <paramref name="input"/> en mayúsculas.
    /// </summary>
    private static bool WildcardMatch(string pattern, string input)
    {
        int pi = 0, ii = 0;
        var p = pattern.ToUpperInvariant();

        while (pi < p.Length && ii < input.Length)
        {
            if (p[pi] == '*') return true;
            if (p[pi] == '?' || p[pi] == input[ii]) { pi++; ii++; }
            else return false;
        }

        while (pi < p.Length && p[pi] == '*') pi++;

        return pi == p.Length && ii == input.Length;
    }

    public void SetLevels((string, int)[] achievement)
    {
        foreach (var (key, level) in achievement)
        {
            if (AchievementLevelsById.TryGetValue(key, out var achievementObj))
            {
                achievementObj.Level = (short)level;
            }
        }
        AchievementsUpdated?.Invoke(this, EventArgs.Empty);
    }
}
