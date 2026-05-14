using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;

namespace Albion_App.Components.Destinyboard;

public partial class DestinyBoardDialogV : UserControl, IDialog
{
    public DestinyBoardDialogV(DestinyBoardVm viewModel)
    {
        DataContext = viewModel;
        AceptarCommand = new AsyncRelayCommand(CloseDialog);
        
        InitializeComponent();
    }

    public required string TextHeader { get; set; }
    public required string DialogNameIdentifier { get; set; }
    public required string DialogOpenIdentifier { get; set; }
    public IAsyncRelayCommand AceptarCommand { get; set; }

    private async Task CloseDialog()
    {
        await DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
    }
}