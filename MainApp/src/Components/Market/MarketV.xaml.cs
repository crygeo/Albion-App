using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Albion_App.Components.Market;

/// <summary>
/// Code-behind mínimo de MarketV.
/// Toda la lógica de presentación vive en <see cref="MarketVm"/>.
/// </summary>
public partial class MarketV : UserControl
{
    /// <summary>
    /// Se dispara cuando el usuario hace doble clic sobre un ítem de la lista.
    /// El llamador puede suscribirse para confirmar la selección sin que MarketV
    /// conozca el contexto en que está embebido.
    /// </summary>
    public event EventHandler? ItemDoubleClicked;

    public MarketV()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mueve el foco de teclado al ListBox de ítems para que el scroll con rueda
    /// funcione inmediatamente sin necesidad de hacer clic primero.
    /// </summary>
    public void FocusItemsList() => PART_ItemsList.Focus();

    private void OnItemsListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Solo confirmar si el clic aterrizó sobre un ListBoxItem real (no en área vacía).
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null and not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListBoxItem)
            ItemDoubleClicked?.Invoke(this, EventArgs.Empty);
    }
}
