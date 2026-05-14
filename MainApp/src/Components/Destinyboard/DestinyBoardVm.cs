using System.Collections.ObjectModel;
using Albion_App.Components.Achievement;
using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App.Components.Destinyboard;

public partial class DestinyBoardVm : ObservableObject
{
    private readonly ProcessPlayerUseCase _processPlayerUseCase;
    private readonly AchievementVmFactory _achievementVmFactory;
    
    private CancellationTokenSource? _cancellationTokenSource;

    
    [ObservableProperty]
    private ObservableCollection<AchievementVm> _listAchievement;
    public DestinyBoardVm(ProcessPlayerUseCase processPlayerUseCase, AchievementVmFactory achievementVmFactory)
    {
        _processPlayerUseCase = processPlayerUseCase;
        _achievementVmFactory = achievementVmFactory;
        _listAchievement = new ();
        
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
}