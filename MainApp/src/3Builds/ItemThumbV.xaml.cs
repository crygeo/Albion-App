using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Albion_App.Components.Item;

namespace Albion_App._3Builds;

public partial class ItemThumbV : UserControl
{
    private Point _dragStart;

    public ItemThumbV()
    {
        InitializeComponent();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (DataContext is not ItemBaseVm vm) return;

        var pos  = e.GetPosition(null);
        var diff = _dragStart - pos;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject("albionItemDrop", vm);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
    }
}
