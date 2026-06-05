namespace AlbionApp.Domain.Market;

/// <summary>
/// Calcula un precio de referencia confiable a partir de datos históricos del mercado.
///
/// El algoritmo resiste manipulación de precios, días sin actividad y outliers
/// mediante una ventana deslizante que selecciona el grupo de precios más coherente.
///
/// Algoritmo:
///   1. Filtrar entradas inválidas (avg_price ≤ 0 o item_count ≤ 0).
///   2. Ordenar por avg_price ascendente.
///   3. Encontrar la ventana deslizante de <see cref="WindowSize"/> elementos
///      con menor rango (max − min). Esto descarta outliers automáticamente.
///   4. Calcular el promedio ponderado por item_count dentro de esa ventana.
///   5. Retornar null si no hay datos válidos suficientes.
/// </summary>
public static class MarketPriceCalculator
{
    /// <summary>Tamaño de la ventana deslizante.</summary>
    private const int WindowSize = 3;

    /// <summary>
    /// Calcula el precio de mercado representativo.
    /// Retorna null si no hay datos válidos.
    /// </summary>
    public static decimal? CalculateMarketPrice(IEnumerable<PriceHistoryEntry> entries)
    {
        // 1. Filtrar inválidos
        var valid = entries
            .Where(static e => e.AvgPrice > 0 && e.ItemCount > 0)
            .ToList();

        if (valid.Count == 0)
            return null;

        // 2. Ordenar por precio
        valid.Sort(static (a, b) => a.AvgPrice.CompareTo(b.AvgPrice));

        // 3. Ventana más estable
        var window = FindBestWindow(valid);

        // 4. Promedio ponderado por volumen
        var totalVolume  = window.Sum(static e => (long)e.ItemCount);
        var weightedSum  = window.Sum(static e => (decimal)e.AvgPrice * e.ItemCount);

        return Math.Round(weightedSum / totalVolume);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve los <see cref="WindowSize"/> elementos consecutivos (por precio)
    /// cuyo rango (max − min) es mínimo.
    /// Si hay menos de <see cref="WindowSize"/> elementos, usa todos.
    /// </summary>
    private static List<PriceHistoryEntry> FindBestWindow(List<PriceHistoryEntry> sorted)
    {
        if (sorted.Count <= WindowSize)
            return sorted;

        var bestStart = 0;
        var bestRange = long.MaxValue;

        for (var i = 0; i <= sorted.Count - WindowSize; i++)
        {
            var range = sorted[i + WindowSize - 1].AvgPrice - sorted[i].AvgPrice;
            if (range < bestRange)
            {
                bestRange = range;
                bestStart = i;
            }
        }

        return sorted.GetRange(bestStart, WindowSize);
    }
}
