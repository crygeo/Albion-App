using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Albion_App._3Builds;

public partial class EquipSlotV : UserControl
{
    public static readonly DependencyProperty SlotContextProperty =
        DependencyProperty.Register(
            nameof(SlotContext),
            typeof(SlotVm),
            typeof(EquipSlotV),
            new PropertyMetadata(null));

    public SlotVm? SlotContext
    {
        get => (SlotVm?)GetValue(SlotContextProperty);
        set => SetValue(SlotContextProperty, value);
    }

    public EquipSlotV()
    {
        InitializeComponent();
    }

    private void OnLeftClick(object sender, MouseButtonEventArgs e)
    {
        if (SlotContext?.PickCommand.CanExecute(null) == true)
            SlotContext.PickCommand.Execute(null);
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (SlotContext?.ClearCommand.CanExecute(null) == true)
            SlotContext.ClearCommand.Execute(null);
    }

    /// <summary>
    /// Rueda del ratón → incrementa o decrementa la cantidad en slots consumibles.
    /// Marca el evento como handled para que no burbujee al ScrollViewer padre.
    /// </summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SlotContext is not { HasQuantity: true, HasItem: true }) return;

        if (e.Delta > 0)
        {
            if (SlotContext.IncrementCommand.CanExecute(null))
                SlotContext.IncrementCommand.Execute(null);
        }
        else
        {
            if (SlotContext.DecrementCommand.CanExecute(null))
                SlotContext.DecrementCommand.Execute(null);
        }

        e.Handled = true; // no scroll el panel padre
    }
}
