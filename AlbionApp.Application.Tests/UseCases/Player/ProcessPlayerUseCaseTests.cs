using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Achievement;
using AlbionApp.Domain.Interfaces.Services;
using Xunit;

namespace AlbionApp.Application.Tests.UseCases.Player;

public sealed class ProcessPlayerUseCaseTests
{
    [Fact]
    public void Execute_replaces_player_levels_by_achievement_id()
    {
        var achievementDataService = new FakeAchievementDataService(
            new AlbionAchievement { OrdinalId = 10, Id = "CRAFT_REFINE_FIBER_T4" },
            new AlbionAchievement { OrdinalId = 20, Id = "COMBAT_SWORD_T5" });
        var useCase = new ProcessPlayerUseCase(achievementDataService);

        useCase.Execute(new Dictionary<int, int>
        {
            [10] = 12,
            [999] = 80
        });
        useCase.Execute(new Dictionary<int, int>
        {
            [20] = 35
        });

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["COMBAT_SWORD_T5"] = 35
            },
            useCase.AchievementLevelsById);
    }

    private sealed class FakeAchievementDataService : IAchievementDataService
    {
        public FakeAchievementDataService(params AlbionAchievement[] achievements)
        {
            ById = achievements.ToDictionary(achievement => achievement.Id);
            ByOrdinal = achievements.ToDictionary(achievement => achievement.OrdinalId);
        }

        public IReadOnlyDictionary<string, AlbionTemplate> Templates { get; }
            = new Dictionary<string, AlbionTemplate>();

        public IReadOnlyDictionary<string, AlbionAchievement> ById { get; }

        public IReadOnlyDictionary<int, AlbionAchievement> ByOrdinal { get; }

    }
}
