using System.Windows;

namespace Albion_App;

/// <summary>
/// Code-behind de MainWindow.
/// Responsabilidad mínima: inicializar el componente.
/// El DataContext (MainVM) se asigna en App.xaml.cs antes de llamar a Show().
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
