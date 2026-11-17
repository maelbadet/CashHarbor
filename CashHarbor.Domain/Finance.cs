namespace CashHarbor.Domain;

public enum AccountKind { Current, Savings, Investment }
public enum EntryKind { Expense, Income, Transfer }
public sealed record FinancialAccount(Guid Id, string Name, string Institution, AccountKind Kind, long OpeningCents, DateOnly OpeningDate);
public sealed record Transaction(Guid Id, Guid AccountId, string Label, string Category, long Cents, DateOnly Date, EntryKind Kind, Guid? TransferId = null, Guid? RecurrenceId = null);
public sealed record FinancialGoal(Guid Id, string Name, long TargetCents, long SavedCents, DateOnly? Deadline);
public sealed record RecurringOperation(Guid Id, Guid AccountId, string Label, string Category, long Cents, DateOnly NextDate, int DayOfMonth, bool Essential, bool Active = true);
public sealed record BalanceSnapshot(Guid AccountId, long BalanceCents, DateOnly Date);
public sealed record EmergencyFundPolicy(int TargetMonths = 3, long BufferCents = 50000);

// One shared workspace. Amounts are exact integer euro cents; no floating point money.
public sealed class Workspace
{
    public const string SharedId = "household";
    public string Name { get; set; } = "Notre foyer";
    public string Currency { get; set; } = "EUR";
    public List<FinancialAccount> Accounts { get; set; } = [];
    public List<Transaction> Transactions { get; set; } = [];
    public List<FinancialGoal> Goals { get; set; } = [];
    public List<RecurringOperation> Recurrences { get; set; } = [];
    public EmergencyFundPolicy EmergencyPolicy { get; set; } = new();
    public long Balance(Guid accountId, DateOnly? at = null)
    {
        var account = Accounts.Single(a => a.Id == accountId);
        return at != null && at < account.OpeningDate ? 0 : account.OpeningCents
            + Transactions.Where(t => t.AccountId == accountId && (at == null || t.Date <= at)).Sum(t => t.Cents);
    }
}
