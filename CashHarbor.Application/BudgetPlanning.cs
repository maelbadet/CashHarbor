namespace CashHarbor.Application;

public sealed record BudgetPlan(decimal Income = 0, int NeedsPercent = 50, int WantsPercent = 30, decimal SavingsGoal = 0, decimal AnnualBills = 0, decimal SafetyBuffer = 0, Dictionary<string, decimal>? Limits = null);
public interface IBudgetPlans
{
    Task<BudgetPlan> ReadAsync(Guid actor, bool couple, string month);
    Task SaveAsync(Guid actor, bool couple, string month, BudgetPlan plan);
}
public sealed record ProjectionPoint(int Year, decimal Contributed, decimal Nominal, decimal PurchasingPower);
public static class InvestmentProjection
{
    public static decimal Median(IEnumerable<decimal> returns)
    {
        var a = returns.Order().ToArray(); if (a.Length == 0 || a.Any(v => v <= -100 || v > 1000)) throw new RuleException("Saisissez des rendements annuels valides.");
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }
    public static IReadOnlyList<ProjectionPoint> Calculate(decimal initial, decimal monthly, int years, decimal annual, decimal fees, decimal inflation)
    {
        if (initial is < 0 or > 100000000 || monthly is < 0 or > 100000 || years is < 1 or > 60 || annual is <= -100 or > 50 || fees is < 0 or > 10 || inflation is <= -100 or > 30) throw new RuleException("Vérifiez les montants, la durée (1 à 60 ans), les taux et les frais.");
        var monthlyFactor = Math.Pow((double)((1 + annual / 100) * (1 - fees / 100)), 1d / 12);
        double value = (double)initial; var rows = new List<ProjectionPoint> { new(0, initial, initial, initial) };
        for (var month = 1; month <= years * 12; month++)
        {
            value = value * monthlyFactor + (double)monthly;
            if (value > 1e18 || !double.IsFinite(value)) throw new RuleException("Résultat trop élevé pour cette simulation.");
            if (month % 12 == 0) rows.Add(new(month / 12, initial + monthly * month, decimal.Round((decimal)value, 2), decimal.Round((decimal)(value / Math.Pow(1 + (double)inflation / 100, month / 12d)), 2)));
        }
        return rows;
    }
}
