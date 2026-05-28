using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Achievement;
using AlbionApp.Domain.Interfaces.Services;
using Xunit;

namespace AlbionApp.Application.Tests.UseCases.Player;

public sealed class ProcessPlayerUseCaseTests
{
    [Fact]
    public void Execute_sets_level_from_raw_value_when_not_at_max()
    {
        var achievementDataService = new FakeAchievementDataService(
            new AlbionAchievement { OrdinalId = 10, Id = "CRAFT_REFINE_FIBER_T4", TitleLocalizationKey = "@DESTINYBOARD_TITLE_CRAFT_REFINE_FIBER_T4" },
            new AlbionAchievement { OrdinalId = 20, Id = "COMBAT_SWORD_T5",       TitleLocalizationKey = "@DESTINYBOARD_TITLE_COMBAT_SWORD_T5" });
        var useCase = new ProcessPlayerUseCase(achievementDataService);

        useCase.Execute([(10, 12, false), (999, 80, false)]);
        useCase.Execute([(20, 35, false)]);

        Assert.Equal(12, useCase.AchievementLevelsByIndex[10].Level);
        Assert.Equal(35, useCase.AchievementLevelsByIndex[20].Level);
        Assert.False(useCase.AchievementLevelsByIndex[20].IsAtMaxLevel);
    }

    [Fact]
    public void Execute_uses_max_level_when_is_at_max()
    {
        var achievementDataService = new FakeAchievementDataService(
            new AlbionAchievement { OrdinalId = 5, Id = "COMBAT_SWORD_T8", TitleLocalizationKey = "@DESTINYBOARD_TITLE_COMBAT_SWORD_T8", UseTemplate = "COMBAT_SPEC" });
        var useCase = new ProcessPlayerUseCase(achievementDataService);

        // Level crudo del servidor es 0, pero IsAtMaxLevel = true → debe usar MaxLevel del dominio.
        // MaxLevel = 100 (Levels vacío + EliteLevels vacío en el fake).
        useCase.Execute([(5, 0, true)]);

        var entry = useCase.AchievementLevelsByIndex[5];
        Assert.True(entry.IsAtMaxLevel);
        Assert.Equal(entry.MaxLevel, entry.Level);
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

        public IReadOnlyDictionary<string, AlbionAchievement> BySpriteAndTier { get; }
            = new Dictionary<string, AlbionAchievement>();

        public IReadOnlyList<(string AchievementId, AchievementBonus Bonus)> BonusLookup { get; }
            = [];

        public AlbionAchievement? FindByItem(AlbionApp.Domain.ItemSearch.ItemBase item)
            => null;
    }
}
