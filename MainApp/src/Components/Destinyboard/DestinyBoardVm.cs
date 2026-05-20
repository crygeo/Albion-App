using System.Collections.ObjectModel;
using Albion_App.Components.Achievement;
using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using LibServices.PlayerState;

namespace Albion_App.Components.Destinyboard;

public partial class DestinyBoardVm : ObservableObject
{
    private readonly ProcessPlayerUseCase _processPlayerUseCase;
    private readonly AchievementVmFactory _achievementVmFactory;
    private readonly PlayerStateService  _playerStateService;
    
    private CancellationTokenSource? _cancellationTokenSource;

    
    [ObservableProperty]
    private ObservableCollection<AchievementVm> _listAchievement;
    public DestinyBoardVm(ProcessPlayerUseCase processPlayerUseCase, AchievementVmFactory achievementVmFactory,PlayerStateService playerStateService)
    {
        _processPlayerUseCase = processPlayerUseCase;
        _achievementVmFactory = achievementVmFactory;
        _playerStateService = playerStateService;
        _listAchievement = new ();

        LoadAchievement();
        _ = Build();
    }
    
    private Task Build()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        
        var list = _processPlayerUseCase.AchievementLevelsById;
        foreach (var playerAchievement in list)
        {
            ListAchievement.Add(_achievementVmFactory.Create(playerAchievement.Value, _cancellationTokenSource.Token));
        }
        return Task.CompletedTask;
    }
    private void LoadAchievement()
    {
        _processPlayerUseCase.SetLevels(_playerStateService.Achievements.Select(a => (a.Id, a.Level)).ToArray());
    }
}