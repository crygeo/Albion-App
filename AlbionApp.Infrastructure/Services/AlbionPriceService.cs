using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AlbionApp.Domain.Market;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Consulta precios históricos a la API de Albion Online Data Project.
///
/// Endpoint: GET {server}/api/v2/stats/history/{itemId}.json
///   ?time-scale={1|6|24}
///   &date={startDate:yyyy-MM-dd}
///   &end_date={endDate:yyyy-MM-dd}
///   &locations={ciudad1,ciudad2,...}
///
/// Rate limit: 180 req/min · 300 req/5 min
/// </summary>
public sealed class AlbionPriceService : IPriceService
{
    private static readonly HttpClient _http = new();

    public async Task<PriceQueryResult> GetPricesAsync(
        PriceQuery     query,
        CancellationToken ct = default)
    {
        try
        {
            var url = BuildUrl(query);
            var raw = await _http.GetFromJsonAsync<List<ApiHistoryEntry>>(url, ct)
                      ?? [];

            if (raw.Count == 0)
                return PriceQueryResult.Fail(query.ApiItemId, "Sin datos para el periodo solicitado.");

            var prices = raw
                .Where(e => e.Data is { Count: > 0 })
                .GroupBy(e => e.Location, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var points  = g.SelectMany(e => e.Data!).ToList();
                    var entries = points.Select(p => new PriceHistoryEntry(p.AvgPrice, p.ItemCount));

                    var refPrice = MarketPriceCalculator.CalculateMarketPrice(entries);
                    if (refPrice is null) return null;  // ciudad sin datos válidos

                    var valid      = points.Where(p => p.AvgPrice > 0 && p.ItemCount > 0).ToList();
                    var totalVol   = valid.Sum(p => p.ItemCount);
                    var lastPoint  = valid.MaxBy(p => p.Timestamp);

                    return new CityPrice(
                        City:        g.Key,
                        AvgPrice:    refPrice.Value,
                        MinPrice:    valid.Min(p => (decimal)p.AvgPrice),
                        MaxPrice:    valid.Max(p => (decimal)p.AvgPrice),
                        Volume:      totalVol,
                        LastUpdated: lastPoint?.Timestamp ?? DateTime.MinValue);
                })
                .Where(c => c is not null)
                .Cast<CityPrice>()
                .OrderByDescending(c => c.Volume)
                .ToList();

            return new PriceQueryResult(query.ApiItemId, true, null, prices);
        }
        catch (Exception ex)
        {
            return PriceQueryResult.Fail(query.ApiItemId, ex.Message);
        }
    }

    // ── Precios actuales ──────────────────────────────────────────────────────

    public async Task<CurrentPriceResult> GetCurrentPricesAsync(
        string                apiItemId,
        AlbionServer          server,
        IReadOnlyList<string> cities,
        CancellationToken     ct = default)
    {
        try
        {
            var citiesParam = Uri.EscapeDataString(string.Join(",", cities));
            var url = $"{server.BaseUrl}/api/v2/stats/prices/{Uri.EscapeDataString(apiItemId)}.json" +
                      $"?locations={citiesParam}";

            var raw = await _http.GetFromJsonAsync<List<ApiPriceEntry>>(url, ct) ?? [];

            var prices = raw
                .Where(e => e.Location is not null)
                .GroupBy(e => e.Location!, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    // Tomar calidad 1 si existe, sino la primera disponible
                    var entry = g.FirstOrDefault(e => e.Quality == 1) ?? g.First();
                    return new CurrentCityPrice(
                        City:         entry.Location!,
                        SellPriceMin: entry.SellPriceMin,
                        SellPriceMax: entry.SellPriceMax,
                        BuyPriceMin:  entry.BuyPriceMin,
                        BuyPriceMax:  entry.BuyPriceMax,
                        SellUpdated:  entry.SellPriceMinDate,
                        BuyUpdated:   entry.BuyPriceMaxDate);
                })
                .Where(c => c.HasSellData || c.HasBuyData)
                .OrderByDescending(c => c.SellPriceMin)
                .ToList();

            return new CurrentPriceResult(apiItemId, true, null, prices);
        }
        catch (Exception ex)
        {
            return CurrentPriceResult.Fail(apiItemId, ex.Message);
        }
    }

    // ── URL builder ───────────────────────────────────────────────────────────

    private static string BuildUrl(PriceQuery q)
    {
        var now       = DateTime.UtcNow;
        var startDate = now.AddHours(-q.TimeScale.Hours);
        var cities    = string.Join(",", q.Cities);

        return $"{q.Server.BaseUrl}/api/v2/stats/history/{Uri.EscapeDataString(q.ApiItemId)}.json" +
               $"?time-scale={q.TimeScale.ApiTimeScale}" +
               $"&date={startDate:yyyy-MM-dd}" +
               $"&end_date={now:yyyy-MM-dd}" +
               $"&locations={Uri.EscapeDataString(cities)}";
    }

    // ── JSON models ───────────────────────────────────────────────────────────

    private sealed class ApiPriceEntry
    {
        [JsonPropertyName("item_id")]           public string?   ItemId          { get; init; }
        [JsonPropertyName("city")]              public string?   Location        { get; init; }  // precios: "city"
        [JsonPropertyName("quality")]           public int       Quality         { get; init; }
        [JsonPropertyName("sell_price_min")]    public decimal   SellPriceMin    { get; init; }
        [JsonPropertyName("sell_price_max")]    public decimal   SellPriceMax    { get; init; }
        [JsonPropertyName("buy_price_min")]     public decimal   BuyPriceMin     { get; init; }
        [JsonPropertyName("buy_price_max")]     public decimal   BuyPriceMax     { get; init; }
        [JsonPropertyName("sell_price_min_date")] public DateTime SellPriceMinDate { get; init; }
        [JsonPropertyName("buy_price_max_date")]  public DateTime BuyPriceMaxDate  { get; init; }
    }

    private sealed class ApiHistoryEntry
    {
        [JsonPropertyName("item_id")]  public string?              ItemId   { get; init; }
        [JsonPropertyName("location")] public string               Location { get; init; } = "";
        [JsonPropertyName("quality")]  public int                  Quality  { get; init; }
        [JsonPropertyName("data")]     public List<ApiDataPoint>?  Data     { get; init; }
    }

    private sealed class ApiDataPoint
    {
        [JsonPropertyName("item_count")] public int      ItemCount { get; init; }
        [JsonPropertyName("avg_price")]  public long     AvgPrice  { get; init; }
        [JsonPropertyName("timestamp")]  public DateTime Timestamp { get; init; }
    }
}
