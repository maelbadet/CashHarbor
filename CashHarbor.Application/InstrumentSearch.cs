namespace CashHarbor.Application;
public sealed record MarketInstrument(string Id, string Name, string Symbol, string Currency, string Market);
public interface IInstrumentSearch { Task<IReadOnlyList<MarketInstrument>> SearchAsync(string kind, string query, CancellationToken ct = default); }
