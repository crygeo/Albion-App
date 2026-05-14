using System.Windows.Threading;
using Albion_App.Components.Destinyboard;
using Albion_App.Components.Item;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;

namespace Albion_App._2Player;

public partial class PlayerVm : ObservableObject, ISectionIcons
{
    [ObservableProperty] private string _header = "Player";
    [ObservableProperty] private PackIconKind _icon = PackIconKind.AccountCircle;

    [ObservableProperty] private DestinyBoardVm _destinyBoardVm;


    public PlayerVm(DestinyBoardVm destinyBoardVm)
    {
        _destinyBoardVm = destinyBoardVm;
    }


    // private DestinyBoardDialogV? DialogV;
    // [RelayCommand]
    // private async Task OpenDialog()
    // {
    //     DialogV = new DestinyBoardDialogV(DestinyBoardVm)
    //     {
    //
    //         TextHeader = "DestinyBoard",
    //         DialogOpenIdentifier = DialogDefaults.Main,
    //         DialogNameIdentifier = DialogDefaults.DestinyBoard,
    //     };
    //     await DialogService.Instance.MostrarDialogo(DialogV);
    // }

    [RelayCommand]
    private async Task OpenDialog()
    {
        await DialogService.Instance.MostrarDialogo<DestinyBoardDialogV>(
            DestinyBoardVm,
            "DestinyBoard",
            DialogDefaults.Main,
            DialogDefaults.DestinyBoard);
    }
}