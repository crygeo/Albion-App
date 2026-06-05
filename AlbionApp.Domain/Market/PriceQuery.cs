namespace AlbionApp.Domain.Market;

/// <summary>Parámetros de consulta de precios al servicio de mercado.</summary>
public sealed record PriceQuery(
    string                ApiItemId,
    AlbionServer          Server,
    IReadOnlyList<string> Cities,
    PriceTimeScale        TimeScale);
