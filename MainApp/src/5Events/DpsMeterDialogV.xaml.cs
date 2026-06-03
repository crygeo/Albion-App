using System.Windows;
using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

public partial class DpsMeterDialogV : UserControl, IDialogBase
{
    public string DialogNameIdentifier { get; set; } = DialogDefaults.DpsMeter;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "DPS Meter";

    public DpsMeterDialogV(DpsMeterVm vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e)
        => _ = DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
