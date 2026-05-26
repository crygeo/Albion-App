using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;

namespace Albion_App._4Groups;

/// <summary>
/// Diálogo de creación / edición de un grupo de composición.
/// Se cierra automáticamente cuando <see cref="GroupEditorVm"/> dispara
/// <c>Saved</c> o <c>Cancelled</c>.
/// </summary>
public partial class GroupEditorDialogV : UserControl, IDialog
{
    // ─── Contrato IDialog ─────────────────────────────────────────────────────

    public string DialogNameIdentifier { get; set; } = DialogDefaults.GroupEditor;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Grupo de composición";

    public IAsyncRelayCommand AceptarCommand { get; set; }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public GroupEditorDialogV(GroupEditorVm viewModel)
    {
        DataContext    = viewModel;
        AceptarCommand = new AsyncRelayCommand(CloseAsync);
        InitializeComponent();

        viewModel.Saved     += OnVmDone;
        viewModel.Cancelled += OnVmDone;
    }

    // ─── Cierre ───────────────────────────────────────────────────────────────

    private void OnVmDone() => _ = CloseAsync();

    private Task CloseAsync()
        => DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);

}
