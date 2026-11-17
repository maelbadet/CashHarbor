using CashHarbor.Application;
using CashHarbor.Domain;

internal static class AnalyticsRegression
{
    public static Task Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var actor = Guid.NewGuid(); var other = Guid.NewGuid();
        var a = Guid.NewGuid(); var joint = Guid.NewGuid(); var hidden = Guid.NewGuid();
        DateOnly D(int day) => new(2026, 1, day);
        var full = new Workspace { Accounts = [
            new(a, "Personnel", "Banque", AccountKind.Current, 100000, D(1), AccountVisibility.Personal, actor),
            new(joint, "Commun", "Banque", AccountKind.Savings, 50000, D(12), AccountVisibility.Joint),
            new(hidden, "Invisible", "Banque", AccountKind.Current, 99999999, D(1), AccountVisibility.Personal, other)] };
        void Tx(Guid id, int day, long cents, EntryKind kind) => full.Transactions.Add(new(Guid.NewGuid(), id, "Test", "Eau", cents, D(day), kind));
        Tx(a, 9, 10000, EntryKind.Income); Tx(a, 10, 20000, EntryKind.Income);
        Tx(a, 11, -301, EntryKind.Expense); Tx(a, 12, -5000, EntryKind.Transfer); Tx(joint, 12, 5000, EntryKind.Transfer);
        Tx(a, 13, 1234, EntryKind.Adjustment); Tx(joint, 14, -60000, EntryKind.Expense);
        Tx(a, 16, 10, EntryKind.Income); Tx(a, 16, 20, EntryKind.Income);
        Tx(a, 17, 999999, EntryKind.Income); Tx(hidden, 15, 999999, EntryKind.Income);
        var authorized = WorkspaceAccess.VisibleTo(full, actor);
        var request = new AnalyticsRequest(D(10), D(16));
        var r = FinancialAnalytics.Build(authorized, request, D(20));
        Check(r.OpeningCents == 110000 && r.ClosingCents == 120963, "Opening/closing balances differ from ledger.");
        Check(r.IncomeCents == 20030 && r.ExpenseCents == 60301 && r.NetFlowCents == -40271, "Transfers, adjustments or out-of-period entries polluted flows.");
        Check(r.ChangeCents == 10963 && r.PreviousIncomeCents == 10000 && r.PreviousFrom == D(3) && r.PreviousTo == D(9), "Period comparison incorrect.");
        foreach (var p in r.History) Check(p.TotalCents == authorized.Accounts.Sum(x => authorized.Balance(x.Id, p.Date)), "Daily balance differs from exact ledger.");
        Check(r.History[0].TrackedAccounts == 1 && r.History[2].TrackedAccounts == 2 && r.History[^1].JointCents == -5000, "Coverage or negative balances incorrect.");
        Check(r.Spending.Sum(x => x.Cents) == r.ExpenseCents && r.Accounts.Count == 2, "Category totals or privacy incorrect.");
        foreach (var grain in new[] { AnalyticsGrain.Daily, AnalyticsGrain.Weekly, AnalyticsGrain.Monthly })
        {
            var grouped = FinancialAnalytics.Build(authorized, request with { Grain = grain }, D(20));
            Check(grouped.IncomeCents == r.IncomeCents && grouped.ExpenseCents == r.ExpenseCents, "Grouping changed totals.");
            if (grain == AnalyticsGrain.Weekly) Check(grouped.Flows.Count == 2 && grouped.Flows[0].To == D(11), "Partial weekly buckets incorrect.");
        }
        var personal = FinancialAnalytics.Build(authorized, request with { Scope = AnalyticsScope.Personal }, D(20));
        Check(personal.Accounts.Count == 1 && personal.ExpenseCents == 301 && personal.ClosingCents == 125963, "Personal filter incorrect.");
        var common = FinancialAnalytics.Build(authorized, request with { AccountId = joint }, D(20));
        Check(common.ClosingCents == -5000 && common.ExpenseCents == 60000, "Annual expense must be counted once at full amount.");
        foreach (var bad in new[] { request with { AccountId = hidden }, request with { AccountId = joint, Scope = AnalyticsScope.Personal }, request with { To = D(21) }, request with { From = D(18) }, request with { Grain = (AnalyticsGrain)99 } })
        {
            try { FinancialAnalytics.Build(authorized, bad, D(20)); throw new Exception("Invalid analytics request accepted."); } catch (RuleException) { }
        }
        var csv = FinancialAnalytics.ExportCsv(r);
        Check(csv.Contains("2026-01-16;1259.63;-50.00;1209.63;0.30;0.00;0.30;2;2") && !csv.Contains("Invisible"), "CSV precision or privacy incorrect.");
        var empty = FinancialAnalytics.Build(new Workspace(), new(D(10), D(10)), D(20));
        Check(empty.History.Count == 1 && empty.ClosingCents == 0 && empty.NetFlowRate == null, "Empty one-day report incorrect.");
        return Task.CompletedTask;
    }
}
