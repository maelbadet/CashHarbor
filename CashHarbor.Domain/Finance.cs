namespace CashHarbor.Domain;

public enum AccountKind { Current, Savings, Investment }
public enum EntryKind { Expense, Income, Transfer, Adjustment }
// Zero deliberately means unclassified: legacy data must not silently become shared.
public enum AccountVisibility { Unassigned, Personal, Joint }
public enum RecurrenceFrequency { Monthly, Yearly }
public sealed record FinancialAccount(Guid Id, string Name, string Institution, AccountKind Kind, long OpeningCents, DateOnly OpeningDate, AccountVisibility Visibility = AccountVisibility.Unassigned, Guid? OwnerId = null, string Product = "");
public sealed record Transaction(Guid Id, Guid AccountId, string Label, string Category, long Cents, DateOnly Date, EntryKind Kind, Guid? TransferId = null, Guid? RecurrenceId = null, Guid? LoanId = null, int? LoanInstallment = null);
public sealed record FinancialGoal(Guid Id, string Name, long TargetCents, long SavedCents, DateOnly? Deadline, AccountVisibility Visibility = AccountVisibility.Unassigned, Guid? OwnerId = null);
public sealed record RecurringOperation(Guid Id, Guid AccountId, string Label, string Category, long Cents, DateOnly NextDate, int DayOfMonth, bool Essential, bool Active = true, RecurrenceFrequency Frequency = RecurrenceFrequency.Monthly, int? MonthOfYear = null)
{
    public decimal MonthlyEquivalentCents => Frequency == RecurrenceFrequency.Yearly ? Cents / 12m : Cents;
    public DateOnly Following(DateOnly current)
    {
        if (!Enum.IsDefined(Frequency)) throw new ArgumentException("Fréquence invalide.");
        var month = Frequency == RecurrenceFrequency.Yearly
            ? new DateOnly(current.Year + 1, MonthOfYear ?? NextDate.Month, 1)
            : new DateOnly(current.Year, current.Month, 1).AddMonths(1);
        return month.AddDays(Math.Min(DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
    }
}
public sealed record BalanceSnapshot(Guid AccountId, long BalanceCents, DateOnly Date, Guid? AdjustmentId = null);
public sealed record EmergencyFundPolicy(int TargetMonths = 3, long BufferCents = 50000);

// One encrypted household vault; every read and write is scoped to the authenticated user.
public sealed class Workspace
{
    public const string SharedId = "household";
    public string Name { get; set; } = "Notre foyer";
    public string Currency { get; set; } = "EUR";
    public List<FinancialAccount> Accounts { get; set; } = [];
    public List<Transaction> Transactions { get; set; } = [];
    public List<FinancialGoal> Goals { get; set; } = [];
    public List<RecurringOperation> Recurrences { get; set; } = [];
    public List<BalanceSnapshot> Snapshots { get; set; } = [];
    public List<Loan> Loans { get; set; } = [];
    public List<LoanPayment> LoanPayments { get; set; } = [];
    public EmergencyFundPolicy EmergencyPolicy { get; set; } = new();
    public long Balance(Guid accountId, DateOnly? at = null)
    {
        var account = Accounts.Single(a => a.Id == accountId);
        return at != null && at < account.OpeningDate ? 0 : account.OpeningCents
            + Transactions.Where(t => t.AccountId == accountId && (at == null || t.Date <= at)).Sum(t => t.Cents);
    }
}
