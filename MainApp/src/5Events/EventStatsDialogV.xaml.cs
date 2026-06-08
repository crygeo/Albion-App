using System.Windows;
using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

public partial class EventStatsDialogV : UserControl, IDialogBase
{
    public string DialogNameIdentifier { get; set; } = DialogDefaults.EventStats;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Estadísticas de eventos";

    public EventStatsDialogV(EventStatsVm vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e)
        => _ = DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
