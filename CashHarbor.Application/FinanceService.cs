using System.Globalization;
using System.Text;
using CashHarbor.Domain;

namespace CashHarbor.Application;

public sealed class FinanceService(IWorkspaceStore store, TimeProvider clock)
{
    public static readonly string[] Categories = ["Logement", "Courses", "Transport", "Santé", "Loisirs", "Abonnements", "Salaire", "Autres"];
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    public Task<Workspace> ReadAsync(CancellationToken ct = default) => store.ReadAsync(ct);
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
    public Task AddAccountAsync(Guid actor, string name, string institution, AccountKind kind, decimal opening, DateOnly date) => store.UpdateAsync(actor, "account.created", w =>
    {
        Limit(w.Accounts.Count >= 100); ValidDate(date);
        if (!Enum.IsDefined(kind)) throw new RuleException("Type de compte invalide.");
        w.Accounts.Add(new(Guid.NewGuid(), Text(name), Text(institution), kind, Money(opening), date));
    });
    public Task RenameAccountAsync(Guid actor, Guid id, string name, string institution) => store.UpdateAsync(actor, "account.updated", w =>
    {
        var a = Account(w, id); w.Accounts[w.Accounts.IndexOf(a)] = a with { Name = Text(name), Institution = Text(institution) };
    });
    public Task DeleteAccountAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "account.deleted", w =>
    {
        var a = Account(w, id);
        if (w.Transactions.Any(t => t.AccountId == id) || w.Recurrences.Any(r => r.AccountId == id)) throw new RuleException("Supprimez d’abord les opérations et échéances de ce compte.");
        w.Accounts.Remove(a);
    });
    public Task AddTransactionAsync(Guid actor, Guid account, string label, string category, decimal amount, DateOnly date, EntryKind kind, Guid? target = null) => store.UpdateAsync(actor, "transaction.created", w =>
    {
        Limit(w.Transactions.Count >= 100000); var a = Account(w, account); ValidDate(date);
        if (date < a.OpeningDate) throw new RuleException("La date précède l’ouverture du compte.");
        var cents = Money(amount);
        if (cents <= 0 || !Enum.IsDefined(kind)) throw new RuleException("Le montant doit être positif et le type valide.");
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
    });
    public Task DeleteTransactionAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "transaction.deleted", w =>
    {
        var t = w.Transactions.SingleOrDefault(t => t.Id == id) ?? throw new RuleException("Opération introuvable.");
        w.Transactions.RemoveAll(x => x.Id == id || (t.TransferId != null && x.TransferId == t.TransferId));
    });
    public Task SaveGoalAsync(Guid actor, Guid? id, string name, decimal target, decimal saved, DateOnly? deadline) => store.UpdateAsync(actor, "goal.saved", w =>
    {
        var t = Money(target); var s = Money(saved);
        if (t <= 0 || s < 0 || s > t || (deadline != null && deadline < new DateOnly(1900, 1, 1))) throw new RuleException("Objectif invalide : la somme mise de côté doit être comprise entre zéro et la cible.");
        var goal = new FinancialGoal(id ?? Guid.NewGuid(), Text(name), t, s, deadline);
        if (id == null) { Limit(w.Goals.Count >= 100); w.Goals.Add(goal); }
        else { var index = w.Goals.FindIndex(g => g.Id == id); if (index < 0) throw new RuleException("Objectif introuvable."); w.Goals[index] = goal; }
    });
    public Task DeleteGoalAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "goal.deleted", w => w.Goals.RemoveAll(g => g.Id == id));
    public Task AddRecurrenceAsync(Guid actor, Guid account, string label, string category, decimal amount, DateOnly next, bool essential) => store.UpdateAsync(actor, "recurrence.created", w =>
    {
        var a = Account(w, account); var cents = Money(amount); Limit(w.Recurrences.Count >= 200);
        if (cents == 0 || next < a.OpeningDate || next > Today.AddYears(5)) throw new RuleException("Montant non nul et première date valide requis.");
        w.Recurrences.Add(new(Guid.NewGuid(), account, Text(label), Text(category, 60), cents, next, next.Day, essential));
    });
    public Task DeleteRecurrenceAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "recurrence.deleted", w => w.Recurrences.RemoveAll(r => r.Id == id));
    // Explicit confirmation only: no background duplication or external bank instruction.
    public Task BookDueAsync(Guid actor) => store.UpdateAsync(actor, "recurrence.booked", w =>
    {
        foreach (var r in w.Recurrences.ToArray())
        {
            var next = r.NextDate; var count = 0;
            while (r.Active && next <= Today)
            {
                if (++count > 120) throw new RuleException("Plus de dix ans d’échéances à rattraper : corrigez la récurrence.");
                Limit(w.Transactions.Count >= 100000);
                if (!w.Transactions.Any(t => t.RecurrenceId == r.Id && t.Date == next))
                    w.Transactions.Add(new(Guid.NewGuid(), r.AccountId, r.Label, r.Category, r.Cents, next, r.Cents < 0 ? EntryKind.Expense : EntryKind.Income, RecurrenceId: r.Id));
                var month = new DateOnly(next.Year, next.Month, 1).AddMonths(1);
                next = month.AddDays(Math.Min(r.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
            }
            w.Recurrences[w.Recurrences.IndexOf(r)] = r with { NextDate = next };
        }
    });
    public Task SavePolicyAsync(Guid actor, string name, int months, decimal buffer) => store.UpdateAsync(actor, "workspace.updated", w =>
    {
        if (months is < 1 or > 24 || buffer < 0) throw new RuleException("Choisissez entre 1 et 24 mois et une réserve positive.");
        w.Name = Text(name); w.EmergencyPolicy = new(months, Money(buffer));
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
