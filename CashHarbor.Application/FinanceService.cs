using System.Globalization;
using System.Text;
using CashHarbor.Domain;

namespace CashHarbor.Application;

public sealed class FinanceService(IWorkspaceStore store, TimeProvider clock)
{
    public static readonly string[] Categories = ["Logement", "Courses", "Transport", "Santé", "Loisirs", "Abonnements", "Crédit", "Salaire", "Autres"];
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    public Task<Workspace> ReadAsync(Guid actor, CancellationToken ct = default) => store.ReadAsync(actor, ct);
    public static long Money(decimal value)
    {
        if (decimal.Abs(value) > 100_000_000m || decimal.Round(value, 2) != value) throw new RuleException("Montant invalide : deux décimales maximum, jusqu’à 100 millions d’euros.");
        return checked((long)(value * 100m));
    }
    private static string Text(string? value, int max = 100)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new RuleException($"Texte requis, de 1 à {max} caractères.");
        return value;
    }
    private static FinancialAccount Account(Workspace w, Guid id) => w.Accounts.SingleOrDefault(a => a.Id == id) ?? throw new RuleException("Compte introuvable.");
    private static void Limit(bool condition) { if (condition) throw new RuleException("Limite de capacité atteinte. Exportez vos données avant de poursuivre."); }
    private void ValidDate(DateOnly date) { if (date < new DateOnly(1900, 1, 1) || date > Today) throw new RuleException("La date doit être comprise entre 1900 et aujourd’hui."); }
    public Task AddAccountAsync(Guid actor, string name, string institution, AccountKind kind, decimal opening, DateOnly date, AccountVisibility visibility = AccountVisibility.Personal, string product = "") => store.UpdateAsync(actor, "account.created", w =>
    {
        Limit(w.Accounts.Count >= 100); ValidDate(date);
        if (!Enum.IsDefined(kind)) throw new RuleException("Type de compte invalide.");
        if (visibility is not (AccountVisibility.Personal or AccountVisibility.Joint)) throw new RuleException("Visibilité invalide.");
        w.Accounts.Add(new(Guid.NewGuid(), Text(name), Text(institution), kind, Money(opening), date, visibility, visibility == AccountVisibility.Personal ? actor : null, string.IsNullOrWhiteSpace(product) ? "" : Text(product, 60)));
    });
    public Task ShareAccountAsync(Guid actor, Guid id, bool confirmed) => store.UpdateAsync(actor, "account.shared", w =>
    {
        var a = Account(w, id);
        if (!confirmed || a.OwnerId != actor || a.Visibility != AccountVisibility.Personal) throw new RuleException("Confirmez le partage de ce compte et de tout son historique.");
        if (w.Loans.Any(l => l.PaymentAccountId == id) || w.Transactions.Any(t => t.AccountId == id && t.LoanId != null))
            throw new RuleException("Ce compte est lié à un prêt personnel ou à ses remboursements. Il ne peut pas être partagé afin de préserver leur confidentialité.");
        w.Accounts[w.Accounts.IndexOf(a)] = a with { Visibility = AccountVisibility.Joint, OwnerId = null };
    });
    public Task RecordBalanceAsync(Guid actor, Guid id, decimal balance, DateOnly date) => store.UpdateAsync(actor, "balance.recorded", w =>
    {
        var a = Account(w, id); ValidDate(date);
        if (date < a.OpeningDate) throw new RuleException("La date précède l’ouverture du compte.");
        var value = Money(balance); var prior = w.Snapshots.SingleOrDefault(s => s.AccountId == id && s.Date == date);
        w.Snapshots.RemoveAll(s => s.AccountId == id && s.Date == date);
        w.Snapshots.Add(new(id, value, date, prior?.AdjustmentId));
        Reconcile(w, id);
    });
    public Task RenameAccountAsync(Guid actor, Guid id, string name, string institution, string? product = null) => store.UpdateAsync(actor, "account.updated", w =>
    {
        var a = Account(w, id); w.Accounts[w.Accounts.IndexOf(a)] = a with { Name = Text(name), Institution = Text(institution), Product = product == null ? a.Product : string.IsNullOrWhiteSpace(product) ? "" : Text(product,60) };
    });
    public Task DeleteAccountAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "account.deleted", w =>
    {
        var a = Account(w, id);
        if(w.Goals.Any(g=>g.AccountId==id))throw new RuleException("Détachez d’abord ce compte des objectifs qui l’utilisent.");
        if (w.Loans.Any(l => l.PaymentAccountId == id)) throw new RuleException("Détachez d’abord ce compte des prêts qui l’utilisent.");
        if (w.Transactions.Any(t => t.AccountId == id) || w.Recurrences.Any(r => r.AccountId == id) || w.Snapshots.Any(s => s.AccountId == id)) throw new RuleException("Supprimez d’abord les opérations et échéances de ce compte. Les comptes avec relevés sont conservés.");
        w.Accounts.Remove(a);
    });
    public Task AddTransactionAsync(Guid actor, Guid account, string label, string category, decimal amount, DateOnly date, EntryKind kind, Guid? target = null) => store.UpdateAsync(actor, "transaction.created", w =>
    {
        Limit(w.Transactions.Count >= 100000); var a = Account(w, account); ValidDate(date);
        if (date < a.OpeningDate) throw new RuleException("La date précède l’ouverture du compte.");
        var cents = Money(amount);
        if (cents <= 0 || kind is not (EntryKind.Expense or EntryKind.Income or EntryKind.Transfer)) throw new RuleException("Le montant doit être positif et le type valide.");
        label = Text(label); category = Text(category, 60);
        if (kind == EntryKind.Transfer)
        {
            if (target == null || target == account) throw new RuleException("Choisissez un autre compte destinataire.");
            var b = Account(w, target.Value);
            if (date < b.OpeningDate) throw new RuleException("La date précède l’ouverture du compte destinataire.");
            var group = Guid.NewGuid();
            w.Transactions.Add(new(Guid.NewGuid(), account, label, "Virement", -cents, date, kind, group));
            w.Transactions.Add(new(Guid.NewGuid(), b.Id, label, "Virement", cents, date, kind, group));
        }
        else w.Transactions.Add(new(Guid.NewGuid(), account, label, category, kind == EntryKind.Expense ? -cents : cents, date, kind));
        Reconcile(w, account);
        if (kind == EntryKind.Transfer) Reconcile(w, target!.Value);
    });
    public Task DeleteTransactionAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "transaction.deleted", w =>
    {
        var t = w.Transactions.SingleOrDefault(t => t.Id == id) ?? throw new RuleException("Opération introuvable.");
        if (t.Kind == EntryKind.Adjustment) throw new RuleException("Modifiez ce solde depuis le compte, à la date du relevé.");
        if (t.LoanId != null) throw new RuleException("Annulez ce remboursement depuis le prêt pour conserver un capital restant dû cohérent.");
        var touched = w.Transactions.Where(x => x.Id == id || t.TransferId != null && x.TransferId == t.TransferId).Select(x => x.AccountId).Distinct().ToArray();
        w.Transactions.RemoveAll(x => x.Id == id || (t.TransferId != null && x.TransferId == t.TransferId));
        foreach (var accountId in touched) Reconcile(w, accountId);
    });
    public Task SaveGoalAsync(Guid actor, Guid? id, string name, decimal target, decimal saved, DateOnly? deadline, AccountVisibility visibility = AccountVisibility.Personal, Guid? accountId = null) => store.UpdateAsync(actor, "goal.saved", w =>
    {
        var t = Money(target); var s = accountId == null ? Money(saved) : 0;
        if (t <= 0 || s < 0 || s > t || (deadline != null && deadline < new DateOnly(1900, 1, 1))) throw new RuleException("Objectif invalide : la somme mise de côté doit être comprise entre zéro et la cible.");
        var existing = id == null ? null : w.Goals.SingleOrDefault(g => g.Id == id) ?? throw new RuleException("Objectif introuvable.");
        if (visibility is not (AccountVisibility.Personal or AccountVisibility.Joint)) throw new RuleException("Visibilité invalide.");
        // Editing preserves access; sharing a personal goal requires creating a separate common goal.
        var effectiveVisibility=existing?.Visibility??visibility;var owner=existing?.OwnerId??(effectiveVisibility==AccountVisibility.Personal?actor:(Guid?)null);
        if(accountId is {} linked){var account=Account(w,linked);if(account.Visibility!=effectiveVisibility||account.OwnerId!=owner)throw new RuleException("Choisissez un compte du même espace et du même titulaire que l’objectif.");}
        var goal = new FinancialGoal(id ?? Guid.NewGuid(), Text(name), t, s, deadline, effectiveVisibility, owner, accountId);
        if (id == null) { Limit(w.Goals.Count >= 100); w.Goals.Add(goal); }
        else { var index = w.Goals.FindIndex(g => g.Id == id); if (index < 0) throw new RuleException("Objectif introuvable."); w.Goals[index] = goal; }
    });
    public Task DeleteGoalAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "goal.deleted", w => { if (w.Goals.RemoveAll(g => g.Id == id) == 0) throw new RuleException("Objectif introuvable."); });
    public Task AddRecurrenceAsync(Guid actor, Guid account, string label, string category, decimal amount, DateOnly next, bool essential, RecurrenceFrequency frequency = RecurrenceFrequency.Monthly) => store.UpdateAsync(actor, "recurrence.created", w =>
    {
        var a = Account(w, account); var cents = Money(amount); Limit(w.Recurrences.Count >= 200);
        if (cents == 0 || next < a.OpeningDate || next > Today.AddYears(5) || !Enum.IsDefined(frequency)) throw new RuleException("Montant non nul, fréquence et première date valides requis.");
        w.Recurrences.Add(new(Guid.NewGuid(), account, Text(label), Text(category, 60), cents, next, next.Day, essential, Frequency: frequency, MonthOfYear: next.Month));
    });
    public Task UpdateRecurrenceAsync(Guid actor, Guid id, string label, string category, decimal amount, DateOnly next, bool essential, RecurrenceFrequency frequency) => store.UpdateAsync(actor, "recurrence.updated", w =>
    {
        var r = w.Recurrences.SingleOrDefault(r => r.Id == id) ?? throw new RuleException("Échéance introuvable.");
        var a = Account(w, r.AccountId); var cents = Money(amount);
        if (cents == 0 || next < a.OpeningDate || next > Today.AddYears(5) || !Enum.IsDefined(frequency)) throw new RuleException("Montant non nul, fréquence et prochaine date valides requis.");
        var preserveAnchor = frequency == r.Frequency && next == r.NextDate;
        w.Recurrences[w.Recurrences.IndexOf(r)] = r with { Label = Text(label), Category = Text(category, 60), Cents = cents, NextDate = next, DayOfMonth = preserveAnchor ? r.DayOfMonth : next.Day, MonthOfYear = preserveAnchor ? r.MonthOfYear ?? r.NextDate.Month : next.Month, Essential = essential, Frequency = frequency };
    });
    public static long EssentialMonthlyCents(Workspace w) => checked((long)decimal.Round(-w.Recurrences.Where(r => r.Essential && r.Cents < 0 && r.Active).Sum(r => r.MonthlyEquivalentCents), 0, MidpointRounding.AwayFromZero));
    public Task DeleteRecurrenceAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "recurrence.deleted", w => { if (w.Recurrences.RemoveAll(r => r.Id == id) == 0) throw new RuleException("Échéance introuvable."); });
    // Explicit confirmation only: no background duplication or external bank instruction.
    public Task BookDueAsync(Guid actor, AccountVisibility? visibility = null) => store.UpdateAsync(actor, "recurrence.booked", w =>
    {
        if (visibility != null && visibility is not (AccountVisibility.Personal or AccountVisibility.Joint)) throw new RuleException("Espace inconnu.");
        var selectedAccounts = w.Accounts.Where(a => visibility == null || a.Visibility == visibility).Select(a => a.Id).ToHashSet();
        foreach (var r in w.Recurrences.Where(r => selectedAccounts.Contains(r.AccountId)).ToArray())
        {
            var next = r.NextDate; var count = 0;
            while (r.Active && next <= Today)
            {
                if (++count > 120) throw new RuleException("Plus de 120 échéances à rattraper : corrigez la prochaine date de la récurrence.");
                Limit(w.Transactions.Count >= 100000);
                if (!w.Transactions.Any(t => t.RecurrenceId == r.Id && t.Date == next))
                    w.Transactions.Add(new(Guid.NewGuid(), r.AccountId, r.Label, r.Category, r.Cents, next, r.Cents < 0 ? EntryKind.Expense : EntryKind.Income, RecurrenceId: r.Id));
                next = r.Following(next);
            }
            w.Recurrences[w.Recurrences.IndexOf(r)] = r with { NextDate = next };
        }
        foreach (var id in w.Recurrences.Select(r => r.AccountId).Distinct().Where(selectedAccounts.Contains)) Reconcile(w, id);
    });
    // Preserve observed balances even when a missing older entry is entered later.
    internal static void Reconcile(Workspace w, Guid account)
    {
        var snapshots = w.Snapshots.Where(s => s.AccountId == account).OrderBy(s => s.Date).ToArray();
        var ids = snapshots.Select(s => s.AdjustmentId).Where(id => id != null).ToHashSet();
        w.Transactions.RemoveAll(t => ids.Contains(t.Id));
        foreach (var snapshot in snapshots)
        {
            var difference = snapshot.BalanceCents - w.Balance(account, snapshot.Date); Guid? id = null;
            if (difference != 0)
            {
                Limit(w.Transactions.Count >= 100000); id = snapshot.AdjustmentId ?? Guid.NewGuid();
                w.Transactions.Add(new(id.Value, account, "Ajustement de solde", "Valorisation", difference, snapshot.Date, EntryKind.Adjustment));
            }
            w.Snapshots[w.Snapshots.IndexOf(snapshot)] = snapshot with { AdjustmentId = id };
        }
    }
    public Task SavePolicyAsync(Guid actor, string name, int months, decimal buffer, decimal? sharedMonthly = null) => store.UpdateAsync(actor, "workspace.updated", w =>
    {
        if (months is < 1 or > 24 || buffer < 0 || sharedMonthly < 0) throw new RuleException("Choisissez entre 1 et 24 mois et des montants positifs.");
        w.Name = Text(name); w.EmergencyPolicy = new(months, Money(buffer), Money(buffer), sharedMonthly == null ? null : Money(sharedMonthly.Value), months);
    });
    public static string ExportCsv(Workspace w)
    {
        static string Cell(string value)
        {
            if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var result = new StringBuilder("Date;Compte;Libellé;Catégorie;Montant EUR;Type\r\n");
        foreach (var t in w.Transactions.OrderByDescending(t => t.Date))
            result.AppendLine(string.Join(';', t.Date.ToString("yyyy-MM-dd"), Cell(w.Accounts.Single(a => a.Id == t.AccountId).Name), Cell(t.Label), Cell(t.Category), (t.Cents / 100m).ToString("0.00", CultureInfo.InvariantCulture), t.Kind));
        return result.ToString();
    }
}
