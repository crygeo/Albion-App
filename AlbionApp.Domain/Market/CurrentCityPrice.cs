namespace AlbionApp.Domain.Market;

/// <summary>Precios actuales (no históricos) de un ítem en una ciudad.</summary>
public sealed record CurrentCityPrice(
    string   City,
    decimal  SellPriceMin,
    decimal  SellPriceMax,
    decimal  BuyPriceMin,
    decimal  BuyPriceMax,
    DateTime SellUpdated,
    DateTime BuyUpdated)
{
    public bool HasSellData => SellPriceMin > 0;
    public bool HasBuyData  => BuyPriceMax  > 0;
}

public sealed record CurrentPriceResult(
    string                        ApiItemId,
    bool                          Success,
    string?                       Error,
    IReadOnlyList<CurrentCityPrice> Prices)
{
    public static CurrentPriceResult Fail(string itemId, string error) =>
        new(itemId, false, error, []);
}
