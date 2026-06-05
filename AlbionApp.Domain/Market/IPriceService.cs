namespace AlbionApp.Domain.Market;

public interface IPriceService
{
    /// <summary>Precios históricos (con promedio ponderado anti-outlier).</summary>
    Task<PriceQueryResult> GetPricesAsync(PriceQuery query, CancellationToken ct = default);

    /// <summary>Precios actuales de mercado (sell/buy) para el diálogo de selección.</summary>
    Task<CurrentPriceResult> GetCurrentPricesAsync(
        string                ApiItemId,
        AlbionServer          Server,
        IReadOnlyList<string> Cities,
        CancellationToken     ct = default);
}
