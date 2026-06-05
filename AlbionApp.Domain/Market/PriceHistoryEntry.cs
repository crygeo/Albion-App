namespace AlbionApp.Domain.Market;

/// <summary>Punto de datos de precio histórico de un ítem en una ciudad.</summary>
public sealed record PriceHistoryEntry(long AvgPrice, int ItemCount);
