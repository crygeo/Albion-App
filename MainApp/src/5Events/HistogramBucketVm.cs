namespace Albion_App._5Events;

public sealed class HistogramBucketVm(string label, int count, int maxCount)
{
    // Debe coincidir con la altura de fila reservada para la barra en EventStatsV.xaml
    // (RowDefinition Height="140" de la fila central de cada columna del histograma).
    private const double MaxBarHeight = 140;

    public string Label     { get; } = label;
    public int    Count     { get; } = count;
    public double BarHeight { get; } = maxCount == 0 ? 0 : MaxBarHeight * count / maxCount;
}
