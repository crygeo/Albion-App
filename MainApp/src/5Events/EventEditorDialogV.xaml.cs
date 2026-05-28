using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

/// <summary>
/// Diálogo de creación / edición de una plantilla de evento.
/// Implementa <see cref="IDialog"/> para integrarse con el sistema de diálogos Material Design.
///
/// La lógica de guardado vive en <see cref="EventEditorVm"/>; este code-behind solo gestiona
/// el ciclo de vida del diálogo: cuando el VM dispara <c>Saved</c> o <c>Cancelled</c>,
/// cierra el diálogo automáticamente.
/// </summary>
public partial class EventEditorDialogV : UserControl, IDialog
{
    // ─── Contrato IDialog ─────────────────────────────────────────────────────

    public string DialogNameIdentifier { get; set; } = DialogDefaults.EventEditor;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Plantilla de evento";

    public IAsyncRelayCommand AceptarCommand { get; set; }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public EventEditorDialogV(EventEditorVm viewModel)
    {
        DataContext    = viewModel;
        AceptarCommand = new AsyncRelayCommand(CloseAsync);
        InitializeComponent();

        // El diálogo se cierra solo cuando el VM confirma o cancela.
        viewModel.Saved     += OnVmDone;
        viewModel.Cancelled += OnVmDone;
    }

    // ─── Cierre ───────────────────────────────────────────────────────────────

    private void OnVmDone() => _ = CloseAsync();

    private Task CloseAsync()
        => DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
