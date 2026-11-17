using CashHarbor.Domain;
namespace CashHarbor.Application;

// Apply only after WorkspaceAccess has removed the partner's private records.
public static class WorkspaceSpaces
{
    public static Workspace Select(Workspace authorized, AccountVisibility visibility)
    {
        if (visibility is not (AccountVisibility.Personal or AccountVisibility.Joint)) throw new RuleException("Espace inconnu.");
        var accounts = authorized.Accounts.Where(a => a.Visibility == visibility).ToList();
        var ids = accounts.Select(a => a.Id).ToHashSet();
        var loans = authorized.Loans.Where(l => l.Visibility == visibility).ToList();
        var loanIds = loans.Select(l => l.Id).ToHashSet();
        return new() { Name = authorized.Name, Currency = authorized.Currency, EmergencyPolicy = authorized.EmergencyPolicy,
            Accounts = accounts, Transactions = authorized.Transactions.Where(t => ids.Contains(t.AccountId)).ToList(),
            Recurrences = authorized.Recurrences.Where(r => ids.Contains(r.AccountId)).ToList(),
            Snapshots = authorized.Snapshots.Where(s => ids.Contains(s.AccountId)).ToList(),
            Goals = authorized.Goals.Where(g => g.Visibility == visibility).ToList(), Loans = loans,
            LoanPayments = authorized.LoanPayments.Where(p => loanIds.Contains(p.LoanId)).ToList() };
    }
}
