using System.Globalization;
using System.Text;
using CashHarbor.Domain;

namespace CashHarbor.Application;

public enum AnalyticsScope { All, Personal, Joint }
public enum AnalyticsGrain { Auto, Daily, Weekly, Monthly }
public sealed record AnalyticsRequest(DateOnly From, DateOnly To, AnalyticsScope Scope = AnalyticsScope.All, Guid? AccountId = null, AnalyticsGrain Grain = AnalyticsGrain.Auto);
public sealed record DailyFinancialPoint(DateOnly Date, long PersonalCents, long JointCents, long IncomeCents, long ExpenseCents, int TrackedAccounts, int SelectedAccounts)
{
    public long TotalCents => PersonalCents + JointCents;
}
public sealed record FlowBucket(DateOnly From, DateOnly To, long IncomeCents, long ExpenseCents)
{
    public long NetCents => IncomeCents - ExpenseCents;
}
public sealed record SpendingCategory(string Name, long Cents, int Count);
public sealed record AccountPosition(FinancialAccount Account, long OpeningCents, long ClosingCents, long IncomeCents, long ExpenseCents)
{
    public long ChangeCents => ClosingCents - OpeningCents;
}
public sealed record UpcomingPayment(string Label, DateOnly Date, long Cents, string AccountName, string Url, bool IsLoan);
public sealed record AnalyticsReport(AnalyticsRequest Request, AnalyticsGrain Grain,
    IReadOnlyList<DailyFinancialPoint> History, IReadOnlyList<FlowBucket> Flows, IReadOnlyList<SpendingCategory> Spending,
    IReadOnlyList<AccountPosition> Accounts, IReadOnlyList<Transaction> Transactions, IReadOnlyList<UpcomingPayment> Upcoming,
    DateOnly PreviousFrom, DateOnly PreviousTo, long PreviousIncomeCents, long PreviousExpenseCents)
{
    public long ClosingCents => Accounts.Sum(a => a.ClosingCents);
    public long OpeningCents => Accounts.Sum(a => a.OpeningCents);
    public long ChangeCents => ClosingCents - OpeningCents;
    public long IncomeCents => Flows.Sum(f => f.IncomeCents);
    public long ExpenseCents => Flows.Sum(f => f.ExpenseCents);
    public long NetFlowCents => IncomeCents - ExpenseCents;
    public decimal? NetFlowRate => IncomeCents > 0 ? NetFlowCents * 100m / IncomeCents : null;
}

// Receives only the workspace slice already authorized by IWorkspaceStore. No database or UI dependencies.
public static class FinancialAnalytics
{
    public static AnalyticsReport Build(Workspace authorized, AnalyticsRequest request, DateOnly today)
    {
        if (request.From < new DateOnly(1900, 1, 1) || request.To > today || request.To < request.From || request.To.DayNumber - request.From.DayNumber > 3660 ||
            !Enum.IsDefined(request.Scope) || !Enum.IsDefined(request.Grain)) throw new RuleException("Choisissez une période passée ou courante, de dix ans maximum, et des filtres valides.");
        var scoped = authorized.Accounts.Where(a => InScope(a.Visibility, request.Scope)).ToArray();
        if (request.AccountId != null && !scoped.Any(a => a.Id == request.AccountId)) throw new RuleException("Compte introuvable dans cette vue ou inaccessible.");
        var accounts = scoped.Where(a => request.AccountId == null || a.Id == request.AccountId).ToArray();
        var ids = accounts.Select(a => a.Id).ToHashSet();
        var all = authorized.Transactions.Where(t => ids.Contains(t.AccountId)).ToArray();
        var groups = all.ToLookup(t => t.AccountId);
        var before = request.From.AddDays(-1);
        static long Balance(FinancialAccount a, IEnumerable<Transaction> tx, DateOnly at) => at < a.OpeningDate ? 0 : a.OpeningCents + tx.Where(t => t.Date <= at).Sum(t => t.Cents);
        var positions = accounts.Select(a => new AccountPosition(a, Balance(a, groups[a.Id], before), Balance(a, groups[a.Id], request.To),
            groups[a.Id].Where(t => t.Date >= request.From && t.Date <= request.To && t.Kind == EntryKind.Income).Sum(t => t.Cents),
            -groups[a.Id].Where(t => t.Date >= request.From && t.Date <= request.To && t.Kind == EntryKind.Expense).Sum(t => t.Cents))).ToArray();
        var personal = positions.Where(p => p.Account.Visibility == AccountVisibility.Personal).Sum(p => p.OpeningCents);
        var joint = positions.Where(p => p.Account.Visibility == AccountVisibility.Joint).Sum(p => p.OpeningCents);
        var events = new Dictionary<DateOnly, (long Personal, long Joint)>();
        void Add(DateOnly date, long cents, AccountVisibility visibility)
        {
            if (date < request.From || date > request.To) return;
            var value = events.GetValueOrDefault(date);
            events[date] = visibility == AccountVisibility.Personal ? (value.Personal + cents, value.Joint) : (value.Personal, value.Joint + cents);
        }
        foreach (var a in accounts)
        {
            // Older malformed imports before opening are included when the account opens, matching Workspace.Balance.
            Add(a.OpeningDate, a.OpeningCents + groups[a.Id].Where(t => t.Date < a.OpeningDate).Sum(t => t.Cents), a.Visibility);
            foreach (var t in groups[a.Id].Where(t => t.Date >= a.OpeningDate)) Add(t.Date, t.Cents, a.Visibility);
        }
        var selected = all.Where(t => t.Date >= request.From && t.Date <= request.To).OrderByDescending(t => t.Date).ThenBy(t => t.Id).ToArray();
        var dailyFlows = selected.Where(t => t.Kind is EntryKind.Income or EntryKind.Expense).ToLookup(t => t.Date);
        var history = new List<DailyFinancialPoint>();
        for (var d = request.From; d <= request.To; d = d.AddDays(1))
        {
            var change = events.GetValueOrDefault(d); personal += change.Personal; joint += change.Joint;
            history.Add(new(d, personal, joint, dailyFlows[d].Where(t => t.Kind == EntryKind.Income).Sum(t => t.Cents), -dailyFlows[d].Where(t => t.Kind == EntryKind.Expense).Sum(t => t.Cents), accounts.Count(a => a.OpeningDate <= d), accounts.Length));
        }
        var days = request.To.DayNumber - request.From.DayNumber + 1;
        var grain = request.Grain == AnalyticsGrain.Auto ? days <= 45 ? AnalyticsGrain.Daily : days <= 180 ? AnalyticsGrain.Weekly : AnalyticsGrain.Monthly : request.Grain;
        DateOnly Bucket(DateOnly d) => grain switch
        {
            AnalyticsGrain.Weekly => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
            AnalyticsGrain.Monthly => new(d.Year, d.Month, 1),
            _ => d
        };
        var flows = history.GroupBy(p => Bucket(p.Date)).Select(g => new FlowBucket(g.First().Date, g.Last().Date, g.Sum(p => p.IncomeCents), g.Sum(p => p.ExpenseCents))).ToArray();
        var spending = selected.Where(t => t.Kind == EntryKind.Expense).GroupBy(t => t.Category).Select(g => new SpendingCategory(g.Key, -g.Sum(t => t.Cents), g.Count())).OrderByDescending(c => c.Cents).ToArray();
        var previousTo = before; var previousFrom = request.From.AddDays(-days);
        var prior = all.Where(t => t.Date >= previousFrom && t.Date <= previousTo).ToArray();
        var upcoming = authorized.Recurrences.Where(r => r.Active && ids.Contains(r.AccountId)).Select(r => new UpcomingPayment(r.Label, r.NextDate, r.Cents, accounts.Single(a => a.Id == r.AccountId).Name, "/echeances", false)).ToList();
        foreach (var loan in authorized.Loans.Where(l => InScope(l.Visibility, request.Scope) && (request.AccountId == null || l.PaymentAccountId == request.AccountId)))
        {
            var rows = LoanCalculator.Schedule(loan); var paid = LoanCalculator.PaidCount(loan, authorized.LoanPayments);
            if (paid < rows.Count) upcoming.Add(new(loan.Name, rows[paid].DueDate, -rows[paid].TotalCents, loan.PaymentAccountId == null ? "Suivi du prêt" : authorized.Accounts.Single(a => a.Id == loan.PaymentAccountId).Name, "/prets/" + loan.Id, true));
        }
        return new(request, grain, history, flows, spending, positions, selected, upcoming.OrderBy(p => p.Date).ThenBy(p => p.Label).Take(6).ToArray(), previousFrom, previousTo,
            prior.Where(t => t.Kind == EntryKind.Income).Sum(t => t.Cents), -prior.Where(t => t.Kind == EntryKind.Expense).Sum(t => t.Cents));
    }
    public static bool InScope(AccountVisibility visibility, AnalyticsScope scope) => scope == AnalyticsScope.All || scope == AnalyticsScope.Personal && visibility == AccountVisibility.Personal || scope == AnalyticsScope.Joint && visibility == AccountVisibility.Joint;
    public static string ExportCsv(AnalyticsReport report)
    {
        var csv = new StringBuilder("Date;Solde personnel EUR;Solde commun EUR;Solde total EUR;Revenus EUR;Depenses EUR;Solde des flux EUR;Comptes renseignes;Comptes selectionnes\r\n");
        static string M(long amount) => (amount / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        foreach (var p in report.History) csv.AppendLine(string.Join(';', p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), M(p.PersonalCents), M(p.JointCents), M(p.TotalCents), M(p.IncomeCents), M(p.ExpenseCents), M(p.IncomeCents - p.ExpenseCents), p.TrackedAccounts.ToString(CultureInfo.InvariantCulture), p.SelectedAccounts.ToString(CultureInfo.InvariantCulture)));
        return csv.ToString();
    }
}
