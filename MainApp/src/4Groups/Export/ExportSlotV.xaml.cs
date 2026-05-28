using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Albion_App._4Groups.Export;

public partial class ExportSlotV : UserControl
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(BitmapImage),
            typeof(ExportSlotV),
            new PropertyMetadata(null));

    public BitmapImage? Source
    {
        get => (BitmapImage?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ExportSlotV() => InitializeComponent();
}
