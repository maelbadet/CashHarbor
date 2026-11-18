namespace CashHarbor.Application;

public sealed record Holding(Guid Id, string Name, string Kind, string Symbol, decimal Quantity, decimal InvestedEuro, DateOnly Bought, decimal? ManualPriceEuro = null);
public sealed record MarketPrice(decimal Price, DateOnly Date, string Currency, string Source);
public interface IPortfolio
{
    Task<IReadOnlyList<Holding>> ReadAsync(Guid actor, bool couple);
    Task SaveAsync(Guid actor, bool couple, Holding holding);
    Task DeleteAsync(Guid actor, bool couple, Guid id);
}
public interface IMarketPrices { Task<MarketPrice?> ReadAsync(string kind, string symbol, DateOnly date, CancellationToken ct = default); }
public static class InvestmentMath
{
    public static decimal Value(decimal quantity, decimal price) => decimal.Round(quantity * price, 2, MidpointRounding.AwayFromZero);
    public static decimal Scenario(decimal capital, decimal annualRate, DateOnly from, DateOnly to)
    {
        if (capital < 0 || annualRate is < 0 or > 100 || to < from) throw new RuleException("Capital, taux ou dates invalides.");
        // Explicit constant-rate scenario, not the regulated fortnight calculation.
        var value = (double)capital * Math.Pow(1 + (double)annualRate / 100, (to.DayNumber - from.DayNumber) / 365.2425);
        return decimal.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
    }
}
