namespace CashHarbor.Application;
public sealed record HistoricalPrice(DateOnly Date, decimal Price);
public sealed record PriceHistory(string Symbol, string Currency, string Source, IReadOnlyList<HistoricalPrice> Prices);
public interface IMarketHistory { Task<PriceHistory?> ReadAsync(string kind, string symbol, DateOnly from, DateOnly to, CancellationToken ct = default); }
