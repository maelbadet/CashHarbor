using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CashHarbor.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
namespace CashHarbor.Infra;

public sealed class MarketHistory(HttpClient http, IConfiguration configuration, IMemoryCache cache) : IMarketHistory
{
    private static string Key(string kind, string symbol, DateOnly from, DateOnly to) => $"history:{kind}:{symbol}:{from}:{to}";
    public string? GetError(string kind, string symbol, DateOnly from, DateOnly to) => cache.Get<string>(Key(kind,symbol,from,to)+":error");
    private static void CheckProvider(JsonElement root)
    {
        foreach (var field in new[] { "Information", "Note", "Error Message" })
        {
            if (!root.TryGetProperty(field, out var value)) continue;
            var text = value.ToString().ToLowerInvariant();
            if (text.Contains("limit") || text.Contains("frequency") || text.Contains("requests per")) throw new RuleException("Quota Alpha Vantage atteint ou appels trop rapprochés. Attendez le renouvellement du quota ; recréer la clé ne rétablit pas nécessairement l’accès.");
            if (text.Contains("apikey") || text.Contains("api key")) throw new RuleException("Clé Alpha Vantage absente ou refusée. Vérifiez ALPHAVANTAGE_API_KEY dans .env puis recréez le conteneur.");
            if (text.Contains("premium") || text.Contains("subscription")) throw new RuleException("Cet historique nécessite une offre Alpha Vantage compatible.");
            throw new RuleException("Alpha Vantage ne fournit pas cet historique pour ce symbole. Un résultat dans la recherche ne garantit pas la couverture des cours.");
        }
    }
    public async Task<PriceHistory?> ReadAsync(string kind, string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (kind is not ("etf" or "action" or "crypto") || !Regex.IsMatch(symbol, "^[a-zA-Z0-9.^_-]{1,50}$") || from > to || to.DayNumber - from.DayNumber > 3660) return null;
        var key = Key(kind,symbol,from,to); if (cache.TryGetValue(key, out PriceHistory? found)) return found;
        cache.Remove(key+":error");
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
                var seriesKey = "monthly-series:"+kind+":"+symbol.ToUpperInvariant();
                if (cache.TryGetValue(seriesKey, out PriceHistory? series) && series != null)
                {
                    result = series with { Prices = series.Prices.Where(p=>p.Date>=from && p.Date<=to).ToArray() };
                    cache.Set(key,result,TimeSpan.FromMinutes(60)); return result;
                }
                var credentials = "&apikey=" + Uri.EscapeDataString(av); var escaped = Uri.EscapeDataString(symbol);
                string currency;
                if (cache.TryGetValue("instrument:"+kind+":"+symbol.ToUpperInvariant(), out MarketInstrument? known) && known != null) currency=known.Currency;
                else {
                using var metadata = JsonDocument.Parse(await http.GetStringAsync("https://www.alphavantage.co/query?function=SYMBOL_SEARCH&keywords=" + escaped + credentials, ct));
                CheckProvider(metadata.RootElement);
                var match = metadata.RootElement.GetProperty("bestMatches").EnumerateArray().FirstOrDefault(m => string.Equals(m.GetProperty("1. symbol").GetString(), symbol, StringComparison.OrdinalIgnoreCase));
                if (match.ValueKind == JsonValueKind.Undefined) throw new RuleException("Symbole exact non retrouvé par Alpha Vantage. Recherchez à nouveau le titre et vérifiez sa place de cotation.");
                var type = match.GetProperty("3. type").GetString() ?? "";
                if (kind == "etf" ? !type.Contains("ETF", StringComparison.OrdinalIgnoreCase) : !type.Contains("Equity", StringComparison.OrdinalIgnoreCase)) throw new RuleException("Ce titre n’est pas classé dans le type sélectionné par Alpha Vantage.");
                currency = match.GetProperty("8. currency").GetString()!;
                }
                using var json = JsonDocument.Parse(await http.GetStringAsync("https://www.alphavantage.co/query?function=TIME_SERIES_MONTHLY&symbol=" + escaped + credentials, ct));
                CheckProvider(json.RootElement);
                var prices = json.RootElement.GetProperty("Monthly Time Series").EnumerateObject().Select(p => new HistoricalPrice(DateOnly.ParseExact(p.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture), decimal.Parse(p.Value.GetProperty("4. close").GetString()!, CultureInfo.InvariantCulture))).Where(p => p.Price > 0).OrderBy(p => p.Date).ToArray();
                var complete = new PriceHistory(symbol, currency, "Alpha Vantage · clôtures mensuelles non ajustées", prices);
                cache.Set(seriesKey,complete,TimeSpan.FromHours(1));
                result = complete with { Prices = prices.Where(p=>p.Date>=from && p.Date<=to).ToArray() };
            }
            else throw new RuleException("Clé Alpha Vantage non configurée. Ajoutez ALPHAVANTAGE_API_KEY dans .env, puis recréez le conteneur.");
        }
        catch (RuleException e) { cache.Set(key+":error", e.Message, TimeSpan.FromMinutes(5)); }
        catch (HttpRequestException e) { cache.Set(key+":error", e.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "Le fournisseur limite les appels. Réessayez après le renouvellement du quota." : "Connexion au fournisseur impossible ou historique refusé par son serveur.", TimeSpan.FromMinutes(5)); }
        catch (Exception e) when (e is TaskCanceledException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException or OverflowException) { cache.Set(key+":error", "Réponse fournisseur incomplète ou inexploitable. Aucun cours utilisable n’a été reçu.", TimeSpan.FromMinutes(5)); }
        cache.Set(key, result, TimeSpan.FromMinutes(result == null ? 5 : 60)); return result;
    }
}
