using System.Net;
using CashHarbor.Infra;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

internal static class MarketHistoryRegression
{
    public static async Task Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Markets:AlphaVantageKey"] = "synthetic-key" }).Build();
        using var cache = new MemoryCache(new MemoryCacheOptions()); var handler = new Handler(); using var http = new HttpClient(handler);
        var history = new MarketHistory(http, config, cache); var from = new DateOnly(2026,1,1); var to = new DateOnly(2026,2,28);
        var crypto = await history.ReadAsync("crypto", "ethereum", from, to);
        Check(crypto?.Currency == "EUR" && crypto.Prices.Count == 2 && crypto.Prices[0].Price == 3000 && crypto.Prices[1].Date == new DateOnly(2026,1,2), "Crypto date normalization or EUR pricing incorrect.");
        var calls = handler.Calls; await history.ReadAsync("crypto", "ethereum", from, to); Check(handler.Calls == calls, "Historical cache missed.");
        var etf = await history.ReadAsync("etf", "TEST", from, to);
        Check(etf?.Currency == "USD" && etf.Prices.Count == 2 && etf.Prices[1].Price == 120, "Stock currency or monthly history incorrect.");
        Check(await history.ReadAsync("action", "TEST", from, to) == null, "ETF accepted as an action.");
        Check(await history.ReadAsync("crypto", "../../secret", from, to) == null, "Invalid symbol accepted.");
        handler.Fail = true; Check(await history.ReadAsync("crypto", "bitcoin", from, to) == null, "Missing provider data fabricated.");
        handler.Fail = false;
        var search = new InstrumentSearch(http, config, cache);
        var matches = await search.SearchAsync("etf", "test");
        Check(matches.Count == 1 && matches[0].Name == "Test Fund" && matches[0].Currency == "USD", "Friendly ETF name or currency missing.");
        Check((await search.SearchAsync("action", "test")).Count == 0, "ETF appeared in action search.");
        calls = handler.Calls; await search.SearchAsync("etf", "test"); Check(handler.Calls == calls, "Instrument search not cached.");
        var coins = await search.SearchAsync("crypto", "Ethereum"); Check(coins.Single().Id == "ethereum" && coins.Single().Name == "Ethereum", "Crypto name search failed.");
        calls = handler.Calls; await history.ReadAsync("etf", "TEST", from.AddDays(2), to); Check(handler.Calls == calls, "Changing period repeated provider requests.");
        handler.Quota = true;
        Check(await history.ReadAsync("action", "APLE", from, to) == null && history.GetError("action", "APLE", from, to)?.Contains("Quota") == true, "Provider quota not diagnosed.");
        Check(!history.GetError("action", "APLE", from, to)!.Contains("synthetic-key"), "Provider diagnostic exposed a credential.");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public bool Fail; public bool Quota;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; var uri = request.RequestUri!.ToString();
            var json = uri.Contains("SYMBOL_SEARCH") ? "{\"bestMatches\":[{\"1. symbol\":\"TEST\",\"3. type\":\"ETF\",\"8. currency\":\"USD\"}]}" : uri.Contains("coingecko") ? "{\"prices\":[[1767225600000,3000],[1767312000000,3200]]}" : "{\"Monthly Time Series\":{\"2026-01-30\":{\"4. close\":\"100\"},\"2026-02-27\":{\"4. close\":\"120\"}}}";
            if (uri.Contains("SYMBOL_SEARCH")) json = "{\"bestMatches\":[{\"1. symbol\":\"TEST\",\"2. name\":\"Test Fund\",\"3. type\":\"ETF\",\"4. region\":\"United States\",\"8. currency\":\"USD\"}]}";
            if (uri.Contains("/search?")) json = "{\"coins\":[{\"id\":\"ethereum\",\"name\":\"Ethereum\",\"symbol\":\"ETH\"}]}";
            if (Quota) json = "{\"Information\":\"Our standard API rate limit is 25 requests per day. synthetic-key\"}";
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
