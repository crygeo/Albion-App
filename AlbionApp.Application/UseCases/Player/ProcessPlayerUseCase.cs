using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;

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


    public void Execute(
        IReadOnlyDictionary<int, int> levelsByOrdinal)
    {
        ArgumentNullException.ThrowIfNull(levelsByOrdinal);

        foreach (var (ordinalId, level) in levelsByOrdinal)
        {
            if (_achievementsByIndex.TryGetValue(ordinalId, out var existing))
            {
                existing.Level = (short)level;
            }
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
                Id = value.Id,
                NameLocalization = value.NameLocalization,
                Level = 0
            };
            
            achievementsId[value.Id] = PA;
            achievementsIndex[value.OrdinalId] = PA;
        }

        return (achievementsId, achievementsIndex);
    }

    public PlayerAchievement? GetAchievement(string achievementId)
    {
        return _achievementsById.GetValueOrDefault(achievementId);
    }
}