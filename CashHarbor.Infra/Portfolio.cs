using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CashHarbor.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace CashHarbor.Infra;

public sealed class PortfolioGate { public SemaphoreSlim Lock { get; } = new(1, 1); }
public sealed class Portfolio(IWorkspaceStore workspace, IDataProtectionProvider protection, IConfiguration config, PortfolioGate gate) : IPortfolio
{
    private readonly IDataProtector protector = protection.CreateProtector("CashHarbor.Portfolio.v1");
    private string PathFor(Guid actor, bool couple) => Path.Combine(config["Storage:Directory"]!, "portfolio-" + (couple ? "couple" : actor.ToString("N")) + ".enc");
    private async Task<List<Holding>> Load(string path) => File.Exists(path) ? JsonSerializer.Deserialize<List<Holding>>(protector.Unprotect(await File.ReadAllTextAsync(path))) ?? [] : [];
    public async Task<IReadOnlyList<Holding>> ReadAsync(Guid actor, bool couple)
    {
        await workspace.ReadAsync(actor); await gate.Lock.WaitAsync();
        try { return await Load(PathFor(actor, couple)); } finally { gate.Lock.Release(); }
    }
    public async Task SaveAsync(Guid actor, bool couple, Holding holding)
    {
        await workspace.ReadAsync(actor);
        if (holding.Name.Length is < 1 or > 100 || holding.Kind is not ("crypto" or "action" or "etf") || !Regex.IsMatch(holding.Symbol, "^[a-zA-Z0-9.^_-]{1,50}$") || holding.Quantity is <= 0 or > 1000000000000m || holding.InvestedEuro is < 0 or > 100000000m || holding.ManualPriceEuro is < 0 or > 100000000m || holding.Bought > DateOnly.FromDateTime(DateTime.UtcNow) || holding.Bought.Year < 1900) throw new RuleException("Vérifiez les données de la position.");
        await gate.Lock.WaitAsync();
        try { var path = PathFor(actor, couple); var data = await Load(path); if (data.Count >= 100) throw new RuleException("100 positions maximum par espace."); data.Add(holding with { Id = Guid.NewGuid() }); await Persist(path, data); }
        finally { gate.Lock.Release(); }
    }
    public async Task DeleteAsync(Guid actor, bool couple, Guid id)
    {
        await workspace.ReadAsync(actor); await gate.Lock.WaitAsync();
        try { var path = PathFor(actor, couple); var data = await Load(path); if (data.RemoveAll(h => h.Id == id) == 0) throw new RuleException("Position introuvable dans cet espace."); await Persist(path, data); }
        finally { gate.Lock.Release(); }
    }
    private async Task Persist(string path, List<Holding> data) { await File.WriteAllTextAsync(path + ".tmp", protector.Protect(JsonSerializer.Serialize(data))); File.Move(path + ".tmp", path, true); }
}

public sealed class MarketPrices(HttpClient http, IConfiguration configuration, IMemoryCache cache) : IMarketPrices
{
    public async Task<MarketPrice?> ReadAsync(string kind, string symbol, DateOnly date, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(symbol, "^[a-zA-Z0-9.^_-]{1,50}$")) return null;
        var key = $"market:{kind}:{symbol}:{date:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out MarketPrice? cached)) return cached;
        MarketPrice? result = null;
        try
        {
            if (kind == "crypto")
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var url = date == today ? $"https://api.coingecko.com/api/v3/simple/price?ids={Uri.EscapeDataString(symbol)}&vs_currencies=eur" : $"https://api.coingecko.com/api/v3/coins/{Uri.EscapeDataString(symbol)}/history?date={date:dd-MM-yyyy}&localization=false";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (configuration["Markets:CoinGeckoKey"] is { Length: > 0 } cg) request.Headers.Add("x-cg-demo-api-key", cg);
                using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var price = date == today ? json.RootElement.GetProperty(symbol).GetProperty("eur").GetDecimal() : json.RootElement.GetProperty("market_data").GetProperty("current_price").GetProperty("eur").GetDecimal();
                result = new(price, date, "EUR", "CoinGecko");
            }
            else if (configuration["Markets:AlphaVantageKey"] is { Length: > 0 } av)
            {
                // Monthly closes: keep their actual observation date. Never label them as today's live quote.
                var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_MONTHLY&symbol={Uri.EscapeDataString(symbol)}&apikey={Uri.EscapeDataString(av)}";
                using var json = JsonDocument.Parse(await http.GetStringAsync(url, ct));
                var row = json.RootElement.GetProperty("Monthly Time Series").EnumerateObject().Where(p => DateOnly.ParseExact(p.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture) <= date).OrderByDescending(p => p.Name).FirstOrDefault();
                if (row.Name != null) result = new(decimal.Parse(row.Value.GetProperty("4. close").GetString()!, CultureInfo.InvariantCulture), DateOnly.ParseExact(row.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture), "devise de cotation", "Alpha Vantage · clôture mensuelle non ajustée");
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or FormatException or TaskCanceledException) { }
        if (result?.Price <= 0) result = null;
        cache.Set(key, result, TimeSpan.FromMinutes(result == null ? 5 : 30)); return result;
    }
}
