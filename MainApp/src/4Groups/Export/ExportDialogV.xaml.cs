using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;

namespace Albion_App._4Groups.Export;

public partial class ExportDialogV : UserControl, IDialog
{
    // ── IDialog ───────────────────────────────────────────────────────────────

    public string             DialogNameIdentifier { get; set; } = DialogDefaults.Export;
    public string             DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string             TextHeader           { get; set; } = "Exportar imagen";
    public IAsyncRelayCommand AceptarCommand       { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ExportDialogV(ExportDialogVm viewModel)
    {
        DataContext    = viewModel;
        AceptarCommand = new AsyncRelayCommand(CloseAsync);
        InitializeComponent();
    }

    // ── Cierre ────────────────────────────────────────────────────────────────

    private Task CloseAsync()
        => DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
