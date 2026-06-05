namespace AlbionApp.Domain.Market;

/// <summary>Precio histórico de un ítem en una ciudad para el periodo solicitado.</summary>
public sealed record CityPrice(
    string   City,
    decimal  AvgPrice,
    decimal  MinPrice,
    decimal  MaxPrice,
    int      Volume,
    DateTime LastUpdated);

/// <summary>Resultado de una consulta de precios.</summary>
public sealed record PriceQueryResult(
    string                   ApiItemId,
    bool                     Success,
    string?                  Error,
    IReadOnlyList<CityPrice> Prices)
{
    public static PriceQueryResult Fail(string itemId, string error) =>
        new(itemId, false, error, []);
}
