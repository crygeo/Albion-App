using System.Windows;
using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;

namespace Albion_App._1Calculadora;

public partial class PriceSelectionDialogV : UserControl, IDialog
{
    public string DialogNameIdentifier { get; set; } = DialogDefaults.PriceSelection;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Precios de Mercado";

    public IAsyncRelayCommand AceptarCommand { get; set; }

    public PriceSelectionDialogV(PriceSelectionDialogVm viewModel)
    {
        DataContext    = viewModel;
        AceptarCommand = new AsyncRelayCommand(CloseAsync);
        InitializeComponent();

        // El VM llama a CloseDialog cuando el usuario doble-clica un precio
        viewModel.CloseDialog = () => _ = CloseAsync();
    }

    private Task CloseAsync()
        => DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);

    private void OnCancelClick(object sender, RoutedEventArgs e)
        => _ = CloseAsync();
}
