using System.Windows;
using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

public partial class EventHistoryDialogV : UserControl, IDialogBase, IDialogLifecycle
{
    private readonly EventHistoryVm _vm;

    public string DialogNameIdentifier { get; set; } = DialogDefaults.EventHistory;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Historial de Eventos";

    public EventHistoryDialogV(EventHistoryVm vm)
    {
        _vm         = vm;
        DataContext = vm;
        InitializeComponent();
    }

    public void OnOpened()  => _ = _vm.LoadAsync();
    public void OnClosed()  { }

    private void OnClose(object sender, RoutedEventArgs e)
        => _ = DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
