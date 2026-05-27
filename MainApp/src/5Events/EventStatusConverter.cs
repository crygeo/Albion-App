using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LibEvents.Entities;

namespace Albion_App._5Events;

/// <summary>Convierte EventStatus en un color de acento para la UI.</summary>
[ValueConversion(typeof(EventStatus), typeof(Brush))]
public sealed class EventStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is EventStatus s ? StatusBrush(s) : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush StatusBrush(EventStatus s) => s switch
    {
        EventStatus.Draft       => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), // gray
        EventStatus.Preparation => new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0xD8)), // cyan
        EventStatus.Running     => new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)), // green
        EventStatus.Paused      => new SolidColorBrush(Color.FromRgb(0xFF, 0xCA, 0x28)), // amber
        EventStatus.Finished    => new SolidColorBrush(Color.FromRgb(0xCE, 0x93, 0xD8)), // purple
        EventStatus.Closed      => new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7)), // light green
        EventStatus.Cancelled   => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)), // red
        _                       => Brushes.Gray,
    };
}

/// <summary>Convierte EventStatus a su etiqueta legible en español.</summary>
[ValueConversion(typeof(EventStatus), typeof(string))]
public sealed class EventStatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is EventStatus s ? StatusLabel(s) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static string StatusLabel(EventStatus s) => s switch
    {
        EventStatus.Draft       => "Plantilla",
        EventStatus.Preparation => "Preparación",
        EventStatus.Running     => "En progreso",
        EventStatus.Paused      => "Pausado",
        EventStatus.Finished    => "Finalizado",
        EventStatus.Closed      => "Cerrado",
        EventStatus.Cancelled   => "Cancelado",
        _                       => s.ToString(),
    };
}
