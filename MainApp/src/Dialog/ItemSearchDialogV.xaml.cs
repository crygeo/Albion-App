using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.Dialog;

/// <summary>
/// Diálogo de búsqueda de ítems. Implementa <see cref="IDialog{TEntity}"/> de Utilidades
/// para integrarse con el sistema MaterialDesign (DialogHost / DialogService).
///
/// Este code-behind es el pegamento entre el contrato de UI (<see cref="IDialog{T}"/>)
/// y la lógica pura (<see cref="ItemSearchVM"/>). No contiene lógica de negocio.
///
/// El diálogo retorna el <see cref="ItemBaseVm"/> completo (con imagen ya descargada)
/// para que el llamador no tenga que volver a descargarla.
///
/// Uso típico desde el llamador:
/// <code>
/// var vm = new ItemSearchVM(categoryProvider, catalog, imageService, localization);
/// var dialog = new ItemSearchDialogV(vm)
/// {
///     DialogOpenIdentifier = DialogDefaults.Main,
///     AceptarCommand       = new AsyncRelayCommand&lt;ItemBaseVm&gt;(OnItemSelected),
///     CancelarCommand      = new AsyncRelayCommand(OnCancelled)
/// };
/// await DialogService.Instance.MostrarDialogo(dialog);
/// </code>
/// </summary>
public partial class ItemSearchDialogV : UserControl, IDialog<ItemBaseVm>
{
    // ─── Contrato IDialog<ItemBaseVm> ─────────────────────────────────────────

    public string DialogNameIdentifier { get; set; } = DialogDefaults.ItemSearch;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Buscar Ítem";

    /// <summary>Llamado por el sistema de diálogos tras la confirmación del usuario.</summary>
    public required IAsyncRelayCommand<ItemBaseVm> AceptarCommand { get; set; }

    /// <summary>Llamado por el sistema de diálogos cuando el usuario cancela.</summary>
    public required IAsyncRelayCommand CancelarCommand { get; set; }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public ItemSearchDialogV(ItemSearchVM viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        viewModel.ConfirmRequested += OnConfirmRequested;
        viewModel.CancelRequested  += OnCancelRequested;

        // Doble clic sobre un ítem de MarketV equivale a pulsar "Seleccionar".
        MarketView.ItemDoubleClicked += (_, _) =>
        {
            if (viewModel.ConfirmCommand.CanExecute(null))
                viewModel.ConfirmCommand.Execute(null);
        };

        // Al abrir el diálogo mover el foco al ListBox para que el scroll con rueda
        // funcione de inmediato sin necesidad de hacer clic primero.
        Loaded += (_, _) => MarketView.FocusItemsList();
    }

    // ─── Manejo de resultado ──────────────────────────────────────────────────

    private async void OnConfirmRequested(object? sender, ItemBaseVm itemVm)
    {
        await AceptarCommand.ExecuteAsync(itemVm);
        await DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
    }

    private async void OnCancelRequested(object? sender, EventArgs e)
    {
        await CancelarCommand.ExecuteAsync(null);
        await DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
    }
}
