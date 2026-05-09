using System.Windows;

namespace Albion_App.Helpers;

/// <summary>
/// Helper de binding estándar WPF para pasar el DataContext del ViewModel
/// dentro de contextos donde el árbol visual lo pierde (MenuItem en HierarchicalDataTemplate,
/// DataGrid columns, ContextMenu, etc.).
///
/// Uso en XAML:
/// <code>
///   &lt;UserControl.Resources&gt;
///     &lt;helpers:BindingProxy x:Key="Proxy" Data="{Binding}" /&gt;
///   &lt;/UserControl.Resources&gt;
///   ...
///   &lt;Setter Property="Command"
///           Value="{Binding Source={StaticResource Proxy}, Path=Data.MiCommand}" /&gt;
/// </code>
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(object),
            typeof(BindingProxy),
            new UIPropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
