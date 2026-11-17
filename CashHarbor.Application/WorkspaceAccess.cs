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
        return new()
        {
            Name = source.Name, Currency = source.Currency, EmergencyPolicy = source.EmergencyPolicy,
            Accounts = accounts, Transactions = source.Transactions.Where(t => ids.Contains(t.AccountId)).ToList(),
            Goals = source.Goals.Where(g => CanSee(g.Visibility, g.OwnerId, actor)).ToList(),
            Recurrences = source.Recurrences.Where(r => ids.Contains(r.AccountId)).ToList(),
            Snapshots = source.Snapshots.Where(s => ids.Contains(s.AccountId)).ToList()
        };
    }

    // Merge only the user's authorized slice, retaining every hidden record unchanged.
    public static void Merge(Workspace full, Workspace changed, Guid actor)
    {
        var original = VisibleTo(full, actor);
        var accountIds = original.Accounts.Select(a => a.Id).ToHashSet();
        var goalIds = original.Goals.Select(g => g.Id).ToHashSet();
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
            if (previous != null && !goalIds.Contains(g.Id)) throw new RuleException("Objectif introuvable.");
        }
        var changedAccountIds = changed.Accounts.Select(a => a.Id).ToHashSet();
        if (changed.Transactions.Any(t => !changedAccountIds.Contains(t.AccountId)) || changed.Recurrences.Any(r => !changedAccountIds.Contains(r.AccountId)) || changed.Snapshots.Any(s => !changedAccountIds.Contains(s.AccountId)))
            throw new RuleException("Compte introuvable.");
        // A shared transfer leg must not let the partner mutate a hidden personal account.
        var removedGroups = original.Transactions.Where(t => t.TransferId != null && !changed.Transactions.Any(c => c.Id == t.Id)).Select(t => t.TransferId).ToHashSet();
        if (full.Transactions.Any(t => t.TransferId != null && removedGroups.Contains(t.TransferId) && !accountIds.Contains(t.AccountId)))
            throw new RuleException("Seul le titulaire du compte personnel lié peut supprimer ce virement.");
        RejectForeignIds(full.Transactions, changed.Transactions, t => t.Id, t => accountIds.Contains(t.AccountId));
        RejectForeignIds(full.Recurrences, changed.Recurrences, r => r.Id, r => accountIds.Contains(r.AccountId));
        full.Accounts.RemoveAll(a => accountIds.Contains(a.Id)); full.Accounts.AddRange(changed.Accounts);
        full.Transactions.RemoveAll(t => accountIds.Contains(t.AccountId)); full.Transactions.AddRange(changed.Transactions);
        full.Recurrences.RemoveAll(r => accountIds.Contains(r.AccountId)); full.Recurrences.AddRange(changed.Recurrences);
        full.Snapshots.RemoveAll(s => accountIds.Contains(s.AccountId)); full.Snapshots.AddRange(changed.Snapshots);
        full.Goals.RemoveAll(g => goalIds.Contains(g.Id)); full.Goals.AddRange(changed.Goals);
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
