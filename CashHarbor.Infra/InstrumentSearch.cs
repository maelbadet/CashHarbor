using System.Text.Json;
using CashHarbor.Application;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
namespace CashHarbor.Infra;

public sealed class InstrumentSearch(HttpClient http, IConfiguration configuration, IMemoryCache cache) : IInstrumentSearch
{
    public async Task<IReadOnlyList<MarketInstrument>> SearchAsync(string kind, string query, CancellationToken ct = default)
    {
        query = query.Trim(); if (query.Length is < 2 or > 80 || kind is not ("etf" or "action" or "crypto")) return [];
        var key = "instrument-search:" + kind + ":" + query.ToLowerInvariant();
        if (cache.TryGetValue(key, out MarketInstrument[]? cached)) return cached!;
        var results = new List<MarketInstrument>();
        try
        {
            if (kind == "crypto")
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.coingecko.com/api/v3/search?query=" + Uri.EscapeDataString(query));
                if (configuration["Markets:CoinGeckoKey"] is { Length: > 0 } cg) request.Headers.Add("x-cg-demo-api-key", cg);
                using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                foreach (var item in json.RootElement.GetProperty("coins").EnumerateArray().Take(15)) results.Add(new(item.GetProperty("id").GetString()!, item.GetProperty("name").GetString()!, item.GetProperty("symbol").GetString()!, "EUR", "Crypto · CoinGecko"));
            }
            else if (configuration["Markets:AlphaVantageKey"] is { Length: > 0 } av)
            {
                using var json = JsonDocument.Parse(await http.GetStringAsync("https://www.alphavantage.co/query?function=SYMBOL_SEARCH&keywords=" + Uri.EscapeDataString(query) + "&apikey=" + Uri.EscapeDataString(av), ct));
                foreach (var item in json.RootElement.GetProperty("bestMatches").EnumerateArray())
                {
                    var type = item.GetProperty("3. type").GetString() ?? "";
                    if (kind == "etf" ? !type.Contains("ETF", StringComparison.OrdinalIgnoreCase) : !type.Contains("Equity", StringComparison.OrdinalIgnoreCase)) continue;
                    var id = item.GetProperty("1. symbol").GetString()!;
                    results.Add(new(id, item.GetProperty("2. name").GetString()!, id, item.GetProperty("8. currency").GetString()!, item.GetProperty("4. region").GetString()!));
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        var array = results.Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.Name.Length <= 250 && System.Text.RegularExpressions.Regex.IsMatch(i.Id, "^[a-zA-Z0-9.^_-]{1,50}$")).DistinctBy(i => i.Id).Take(15).ToArray(); cache.Set(key, array, TimeSpan.FromMinutes(array.Length > 0 ? 360 : 2)); return array;
    }
}
