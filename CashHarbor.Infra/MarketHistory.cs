using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CashHarbor.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
namespace CashHarbor.Infra;

public sealed class MarketHistory(HttpClient http, IConfiguration configuration, IMemoryCache cache) : IMarketHistory
{
    public async Task<PriceHistory?> ReadAsync(string kind, string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (kind is not ("etf" or "action" or "crypto") || !Regex.IsMatch(symbol, "^[a-zA-Z0-9.^_-]{1,50}$") || from > to || to.DayNumber - from.DayNumber > 3660) return null;
        var key = $"history:{kind}:{symbol}:{from}:{to}"; if (cache.TryGetValue(key, out PriceHistory? found)) return found;
        PriceHistory? result = null;
        try
        {
            if (kind == "crypto")
            {
                var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
                var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.coingecko.com/api/v3/coins/{Uri.EscapeDataString(symbol)}/market_chart/range?vs_currency=eur&from={start}&to={end}");
                if (configuration["Markets:CoinGeckoKey"] is { Length: > 0 } cg) request.Headers.Add("x-cg-demo-api-key", cg);
                using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var prices = json.RootElement.GetProperty("prices").EnumerateArray().Select(p => new HistoricalPrice(DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(p[0].GetInt64()).UtcDateTime), p[1].GetDecimal())).Where(p => p.Date >= from && p.Date <= to && p.Price > 0).GroupBy(p => p.Date).Select(g => g.Last()).OrderBy(p => p.Date).ToArray();
                result = new(symbol, "EUR", "CoinGecko · dernier point disponible par jour UTC", prices);
            }
            else if (configuration["Markets:AlphaVantageKey"] is { Length: > 0 } av)
            {
                var credentials = "&apikey=" + Uri.EscapeDataString(av); var escaped = Uri.EscapeDataString(symbol);
                using var metadata = JsonDocument.Parse(await http.GetStringAsync("https://www.alphavantage.co/query?function=SYMBOL_SEARCH&keywords=" + escaped + credentials, ct));
                var match = metadata.RootElement.GetProperty("bestMatches").EnumerateArray().FirstOrDefault(m => string.Equals(m.GetProperty("1. symbol").GetString(), symbol, StringComparison.OrdinalIgnoreCase));
                if (match.ValueKind == JsonValueKind.Undefined) return null;
                var type = match.GetProperty("3. type").GetString() ?? "";
                if (kind == "etf" ? !type.Contains("ETF", StringComparison.OrdinalIgnoreCase) : !type.Contains("Equity", StringComparison.OrdinalIgnoreCase)) return null;
                var currency = match.GetProperty("8. currency").GetString()!;
                using var json = JsonDocument.Parse(await http.GetStringAsync("https://www.alphavantage.co/query?function=TIME_SERIES_MONTHLY&symbol=" + escaped + credentials, ct));
                var prices = json.RootElement.GetProperty("Monthly Time Series").EnumerateObject().Select(p => new HistoricalPrice(DateOnly.ParseExact(p.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture), decimal.Parse(p.Value.GetProperty("4. close").GetString()!, CultureInfo.InvariantCulture))).Where(p => p.Date >= from && p.Date <= to && p.Price > 0).OrderBy(p => p.Date).ToArray();
                result = new(symbol, currency, "Alpha Vantage · clôtures mensuelles non ajustées", prices);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException or OverflowException) { }
        cache.Set(key, result, TimeSpan.FromMinutes(result == null ? 5 : 60)); return result;
    }
}
