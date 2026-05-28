using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

/// <summary>
/// Diálogo de activación de un evento (asigna fecha/hora UTC y cambia su estado a Open).
/// Implementa <see cref="IDialog"/> para integrarse con el sistema de diálogos.
///
/// Se cierra automáticamente cuando <see cref="EventsVm"/> dispara
/// <c>ActivationConfirmed</c> o <c>ActivationCancelled</c>.
/// </summary>
public partial class ActivateEventDialogV : UserControl, IDialog
{
    // ─── Contrato IDialog ─────────────────────────────────────────────────────

    public string DialogNameIdentifier { get; set; } = DialogDefaults.ActivateEvent;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Activar evento";

    public IAsyncRelayCommand AceptarCommand { get; set; }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public ActivateEventDialogV(EventsVm viewModel)
    {
        DataContext    = viewModel;
        AceptarCommand = new AsyncRelayCommand(CloseAsync);
        InitializeComponent();

        viewModel.ActivationConfirmed += OnVmDone;
        viewModel.ActivationCancelled += OnVmDone;
    }

    // ─── Cierre ───────────────────────────────────────────────────────────────

    private void OnVmDone() => _ = CloseAsync();

    private Task CloseAsync()
        => DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
