using AlbionApp.Application.UseCases.Player;
using AlbionApp.Infrastructure.Services;
using LibAlbionProtocol.Models;
using LibAlbionProtocol.PacketModels;
using LibAlbionRouting.Dispatching;
using LibAlbionRouting.Handlers;
using LibServices.PlayerState;

namespace Albion_App.Infrastructure.Handlers;

public sealed class AchievementHandler : IEventHandler
{
    private readonly ProcessPlayerUseCase   _processPlayer;
    private readonly AchievementDataService _achievementService;
    private readonly PlayerStateService     _playerState;

    public AchievementHandler(
        ProcessPlayerUseCase   processPlayer,
        AchievementDataService achievementService,
        PlayerStateService     playerState)
    {
        _processPlayer      = processPlayer;
        _achievementService = achievementService;
        _playerState        = playerState;
    }

    public void Register(IEventDispatcher dispatcher)
        => dispatcher.Subscribe<FullAchievementInfoModel>(
               EventCodes.FullAchievementInfo,
               OnFullAchievementInfo);

    private Task OnFullAchievementInfo(FullAchievementInfoModel model)
    {
        _processPlayer.Execute(
            model.GetAllEntries()
                 .Select(e => (e.Id, e.Level, e.IsAtMaxLevel)));

        var entries = model.AchievementIds
            .Where(e => _achievementService.ByOrdinal.ContainsKey(e.Id))
            .Select(e =>
            {
                var def = _achievementService.ByOrdinal[e.Id];
                return new SavedAchievement
                {
                    Id    = def.Id,
                    Level = e.IsAtMaxLevel ? def.MaxLevel : e.Level,
                };
            });

        _playerState.Apply(entries);
        return Task.CompletedTask;
    }
}
