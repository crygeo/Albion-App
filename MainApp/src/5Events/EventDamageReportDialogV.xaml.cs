using System.Windows;
using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

public partial class EventDamageReportDialogV : UserControl, IDialogBase
{
    public string DialogNameIdentifier { get; set; } = DialogDefaults.DamageReport;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Reporte de daño";

    public EventDamageReportDialogV(EventDamageReportVm vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e)
        => _ = DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
