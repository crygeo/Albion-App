using Albion_App.Interfaces;
using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Interfaces;

namespace Albion_App.Components.Achievement;

public sealed class AchievementVmFactory : IDisplayVmFactory<PlayerAchievement, AchievementVm>
{
    private readonly ILocalizationService _localization;
    private readonly ProcessPlayerUseCase _processPlayer;
    private readonly IAlbionImageService  _imageService;

    public AchievementVmFactory(ILocalizationService localization,  ProcessPlayerUseCase processPlayer, IAlbionImageService  imageService )
    {
        _localization = localization;
        _processPlayer = processPlayer;
        _imageService = imageService;
    }

    public AchievementVm Create(PlayerAchievement hit, CancellationToken ct = default)
    {
        var vm = new AchievementVm(hit, _localization,  _processPlayer);
        _ = vm.LoadImageAsync(_imageService, ct);
        return vm;
    }
}