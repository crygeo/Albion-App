using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;

namespace Albion_App._0Config;

public partial class DiscordConfigDialogV : UserControl, IDialog
{
    public string DialogNameIdentifier { get; set; } = DialogDefaults.DiscordConfig;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Discord Bot";

    public IAsyncRelayCommand AceptarCommand { get; set; }

    public DiscordConfigDialogV(DiscordConfigVm viewModel)
    {
        DataContext    = viewModel;
        AceptarCommand = new AsyncRelayCommand(CloseAsync);
        InitializeComponent();

        viewModel.Saved     += OnVmDone;
        viewModel.Cancelled += OnVmDone;
    }

    private void OnVmDone() => _ = CloseAsync();

    private Task CloseAsync()
        => DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
