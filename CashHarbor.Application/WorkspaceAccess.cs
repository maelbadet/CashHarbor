using CashHarbor.Domain;

namespace CashHarbor.Application;

public static class WorkspaceAccess
{
    public static bool CanSee(AccountVisibility visibility, Guid? owner, Guid actor) =>
        visibility == AccountVisibility.Joint && owner == null || visibility == AccountVisibility.Personal && owner == actor;

    public static Workspace VisibleTo(Workspace source, Guid actor)
    {
        var accounts = source.Accounts.Where(a => CanSee(a.Visibility, a.OwnerId, actor)).ToList();
        var ids = accounts.Select(a => a.Id).ToHashSet();
        var loans = source.Loans.Where(l => CanSee(l.Visibility, l.OwnerId, actor)).ToList();
        var loanIds = loans.Select(l => l.Id).ToHashSet();
        return new()
        {
            Name = source.Name, Currency = source.Currency, EmergencyPolicy = source.EmergencyPolicy,
            Accounts = accounts, Transactions = source.Transactions.Where(t => ids.Contains(t.AccountId)).ToList(),
            Goals = source.Goals.Where(g => CanSee(g.Visibility, g.OwnerId, actor)).ToList(),
            Recurrences = source.Recurrences.Where(r => ids.Contains(r.AccountId) && (r.TargetAccountId==null||ids.Contains(r.TargetAccountId.Value))).ToList(),
            Snapshots = source.Snapshots.Where(s => ids.Contains(s.AccountId)).ToList(),
            Loans = loans, LoanPayments = source.LoanPayments.Where(p => loanIds.Contains(p.LoanId)).ToList()
        };
    }

    // Merge only the user's authorized slice, retaining every hidden record unchanged.
    public static void Merge(Workspace full, Workspace changed, Guid actor)
    {
        var original = VisibleTo(full, actor);
        var accountIds = original.Accounts.Select(a => a.Id).ToHashSet();
        var goalIds = original.Goals.Select(g => g.Id).ToHashSet();
        var loanIds = original.Loans.Select(l => l.Id).ToHashSet();
        var recurrenceIds=original.Recurrences.Select(r=>r.Id).ToHashSet();
        foreach(var r in changed.Recurrences)if(r.TargetAccountId is {} destination && (!changed.Accounts.Any(a=>a.Id==destination)||r.Cents>=0||destination==r.AccountId))throw new RuleException("Virement récurrent invalide ou compte inaccessible.");
        foreach (var loan in changed.Loans)
        {
            var previous = full.Loans.SingleOrDefault(l => l.Id == loan.Id);
            ValidateAccess(loan.Visibility, loan.OwnerId, previous?.Visibility, previous?.OwnerId, actor);
            if (previous != null && !loanIds.Contains(loan.Id)) throw new RuleException("Prêt introuvable.");
            if (previous != null && (loan.Visibility != previous.Visibility || loan.OwnerId != previous.OwnerId)) throw new RuleException("La visibilité du prêt ne peut pas être modifiée.");
            if (loan.PaymentAccountId is { } paymentId)
            {
                var account = changed.Accounts.SingleOrDefault(a => a.Id == paymentId);
                if (account == null || account.Visibility != loan.Visibility || account.OwnerId != loan.OwnerId)
                    throw new RuleException("Le compte de remboursement doit avoir la même visibilité et le même titulaire que le prêt.");
            }
        }
        var changedLoanIds = changed.Loans.Select(l => l.Id).ToHashSet();
        if (changed.LoanPayments.Any(p => !changedLoanIds.Contains(p.LoanId))) throw new RuleException("Prêt introuvable.");
        foreach (var transaction in changed.Transactions.Where(t => t.LoanId != null))
        {
            var loan = changed.Loans.SingleOrDefault(l => l.Id == transaction.LoanId);
            var account = changed.Accounts.SingleOrDefault(a => a.Id == transaction.AccountId);
            if (loan == null || account == null || account.Visibility != loan.Visibility || account.OwnerId != loan.OwnerId)
                throw new RuleException("Un remboursement ne peut pas exposer les informations d’un prêt personnel sur un compte commun.");
        }
        foreach (var payment in changed.LoanPayments.Where(p => p.TransactionId != null))
            if (!changed.Transactions.Any(t => t.Id == payment.TransactionId && t.LoanId == payment.LoanId && t.LoanInstallment == payment.InstallmentNumber))
                throw new RuleException("Le règlement et son opération liée doivent rester cohérents.");
        RejectForeignIds(full.LoanPayments, changed.LoanPayments, p => p.Id, p => loanIds.Contains(p.LoanId));
        foreach (var a in changed.Accounts)
        {
            var previous = full.Accounts.SingleOrDefault(x => x.Id == a.Id);
            ValidateAccess(a.Visibility, a.OwnerId, previous?.Visibility, previous?.OwnerId, actor);
            if (previous != null && !accountIds.Contains(a.Id)) throw new RuleException("Compte introuvable.");
        }
        foreach (var g in changed.Goals)
        {
            var previous = full.Goals.SingleOrDefault(x => x.Id == g.Id);
            ValidateAccess(g.Visibility, g.OwnerId, previous?.Visibility, previous?.OwnerId, actor);
            if(g.AccountId is {} linked){var account=changed.Accounts.SingleOrDefault(a=>a.Id==linked);if(account==null||account.Visibility!=g.Visibility||account.OwnerId!=g.OwnerId)throw new RuleException("L’objectif et son compte doivent appartenir au même espace et au même titulaire.");}
            if (previous != null && !goalIds.Contains(g.Id)) throw new RuleException("Objectif introuvable.");
        }
        var changedAccountIds = changed.Accounts.Select(a => a.Id).ToHashSet();
        if(full.Recurrences.Any(r=>(accountIds.Contains(r.AccountId)&&!changedAccountIds.Contains(r.AccountId))||(r.TargetAccountId is {} target&&accountIds.Contains(target)&&!changedAccountIds.Contains(target))))throw new RuleException("Ce compte est encore utilisé par une récurrence. Arrêtez-la avant de supprimer le compte.");
        if (changed.Transactions.Any(t => !changedAccountIds.Contains(t.AccountId)) || changed.Recurrences.Any(r => !changedAccountIds.Contains(r.AccountId)) || changed.Snapshots.Any(s => !changedAccountIds.Contains(s.AccountId)))
            throw new RuleException("Compte introuvable.");
        // A shared transfer leg must not let the partner mutate a hidden personal account.
        var removedGroups = original.Transactions.Where(t => t.TransferId != null && !changed.Transactions.Any(c => c.Id == t.Id)).Select(t => t.TransferId).ToHashSet();
        if (full.Transactions.Any(t => t.TransferId != null && removedGroups.Contains(t.TransferId) && !accountIds.Contains(t.AccountId)))
            throw new RuleException("Seul le titulaire du compte personnel lié peut supprimer ce virement.");
        RejectForeignIds(full.Transactions, changed.Transactions, t => t.Id, t => accountIds.Contains(t.AccountId));
        RejectForeignIds(full.Recurrences, changed.Recurrences, r => r.Id, r => recurrenceIds.Contains(r.Id));
        full.Accounts.RemoveAll(a => accountIds.Contains(a.Id)); full.Accounts.AddRange(changed.Accounts);
        full.Transactions.RemoveAll(t => accountIds.Contains(t.AccountId)); full.Transactions.AddRange(changed.Transactions);
        full.Recurrences.RemoveAll(r => recurrenceIds.Contains(r.Id)); full.Recurrences.AddRange(changed.Recurrences);
        full.Snapshots.RemoveAll(s => accountIds.Contains(s.AccountId)); full.Snapshots.AddRange(changed.Snapshots);
        full.Goals.RemoveAll(g => goalIds.Contains(g.Id)); full.Goals.AddRange(changed.Goals);
        full.Loans.RemoveAll(l => loanIds.Contains(l.Id)); full.Loans.AddRange(changed.Loans);
        full.LoanPayments.RemoveAll(p => loanIds.Contains(p.LoanId)); full.LoanPayments.AddRange(changed.LoanPayments);
        full.Name = changed.Name; full.EmergencyPolicy = changed.EmergencyPolicy;
    }
    private static void ValidateAccess(AccountVisibility visibility, Guid? owner, AccountVisibility? oldVisibility, Guid? oldOwner, Guid actor)
    {
        if (!CanSee(visibility, owner, actor) || oldVisibility == AccountVisibility.Joint && visibility != AccountVisibility.Joint ||
            oldVisibility == AccountVisibility.Personal && oldOwner != actor || oldVisibility == AccountVisibility.Unassigned)
            throw new RuleException("Modification de visibilité non autorisée.");
    }
    private static void RejectForeignIds<T>(IEnumerable<T> all, IEnumerable<T> changed, Func<T, Guid> id, Func<T, bool> visible)
    {
        var forbidden = all.Where(x => !visible(x)).Select(id).ToHashSet();
        if (changed.Any(x => forbidden.Contains(id(x)))) throw new RuleException("Opération introuvable.");
    }
}
