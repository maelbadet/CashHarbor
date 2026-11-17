using System.Globalization;
using System.Text;
using CashHarbor.Domain;

namespace CashHarbor.Application;

public sealed record LoanDraft(string Name, string Lender, LoanKind Kind, decimal Principal, decimal AnnualNominalRate,
    int TermMonths, DateOnly FirstDueDate, LoanInsuranceKind InsuranceKind, decimal MonthlyInsurance,
    decimal AnnualInsuranceRate, decimal Fees, decimal? ContractApr, int InitialPaidInstallments,
    AccountVisibility Visibility, Guid? PaymentAccountId);

public sealed class LoanService(IWorkspaceStore store, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private static Loan Find(Workspace w, Guid id) => w.Loans.SingleOrDefault(l => l.Id == id) ?? throw new RuleException("Prêt introuvable.");
    private static string Text(string value)
    {
        value = value.Trim();
        if (value.Length is < 1 or > 100 || value.Any(char.IsControl)) throw new RuleException("Nom et établissement requis, de 1 à 100 caractères.");
        return value;
    }
    public Task SaveAsync(Guid actor, Guid? id, LoanDraft draft) => store.UpdateAsync(actor, id == null ? "loan.created" : "loan.updated", w =>
    {
        var previous = id == null ? null : Find(w, id.Value);
        if (previous != null && w.LoanPayments.Any(p => p.LoanId == id)) throw new RuleException("Le contrat ne peut plus être modifié après un règlement enregistré. Annulez les règlements concernés avant de corriger le contrat.");
        if (id == null && w.Loans.Count >= 50) throw new RuleException("Limite de 50 prêts accessibles atteinte.");
        if (draft.Visibility is not (AccountVisibility.Personal or AccountVisibility.Joint)) throw new RuleException("Visibilité invalide.");
        var visibility = previous?.Visibility ?? draft.Visibility; var owner = previous == null ? visibility == AccountVisibility.Personal ? actor : (Guid?)null : previous.OwnerId;
        var loan = new Loan(id ?? Guid.NewGuid(), Text(draft.Name), Text(draft.Lender), draft.Kind,
            FinanceService.Money(draft.Principal), draft.AnnualNominalRate, draft.TermMonths, draft.FirstDueDate,
            draft.InsuranceKind, FinanceService.Money(draft.MonthlyInsurance), draft.AnnualInsuranceRate,
            FinanceService.Money(draft.Fees), draft.ContractApr, draft.InitialPaidInstallments,
            previous?.TrackingStartedOn ?? Today, visibility, owner, draft.PaymentAccountId);
        var rows = Schedule(loan);
        if (loan.InitialPaidInstallments < 0 || loan.InitialPaidInstallments > rows.Count ||
            loan.InitialPaidInstallments > 0 && rows[loan.InitialPaidInstallments - 1].DueDate > Today ||
            loan.FirstDueDate > Today.AddYears(2)) throw new RuleException("Vérifiez la première échéance et le nombre de mensualités déjà réglées (sans échéance future).");
        CheckPaymentAccount(w, loan);
        if (previous == null) w.Loans.Add(loan); else w.Loans[w.Loans.IndexOf(previous)] = loan;
    });
    public Task SetPaymentAccountAsync(Guid actor, Guid id, Guid? account) => store.UpdateAsync(actor, "loan.account-updated", w =>
    {
        var loan = Find(w, id); var updated = loan with { PaymentAccountId = account }; CheckPaymentAccount(w, updated);
        w.Loans[w.Loans.IndexOf(loan)] = updated;
    });
    private static void CheckPaymentAccount(Workspace w, Loan loan)
    {
        if (loan.PaymentAccountId is not { } id) return;
        var account = w.Accounts.SingleOrDefault(a => a.Id == id);
        if (account == null || account.Visibility != loan.Visibility || account.OwnerId != loan.OwnerId)
            throw new RuleException("Choisissez un compte avec la même visibilité que le prêt : personnel avec personnel, commun avec commun.");
    }
    public Task ConfirmAsync(Guid actor, Guid id, int expectedNumber, DateOnly paidOn, bool createExpense) => store.UpdateAsync(actor, "loan.payment-confirmed", w =>
    {
        var loan = Find(w, id); var rows = Schedule(loan); var paid = LoanCalculator.PaidCount(loan, w.LoanPayments);
        if (paid >= rows.Count) throw new RuleException("Ce prêt est déjà soldé.");
        if (expectedNumber != paid + 1) throw new RuleException("Cette échéance a déjà été traitée ou a changé. Rechargez le prêt.");
        var row = rows[paid];
        var lastDate = w.LoanPayments.Where(p => p.LoanId == id).Select(p => p.PaidOn).DefaultIfEmpty(loan.TrackingStartedOn).Max();
        if (paidOn > Today || paidOn < lastDate || row.DueDate > Today) throw new RuleException("Confirmez une échéance arrivée à terme, à une date entre le début de suivi ou le dernier règlement et aujourd’hui.");
        Guid? transactionId = null;
        if (createExpense)
        {
            CheckPaymentAccount(w, loan);
            var account = w.Accounts.SingleOrDefault(a => a.Id == loan.PaymentAccountId) ?? throw new RuleException("Choisissez d’abord un compte de remboursement dans le prêt.");
            if (paidOn < account.OpeningDate || w.Transactions.Count >= 100000) throw new RuleException("Date antérieure au compte ou capacité d’opérations atteinte.");
            if (w.Transactions.Any(t => t.AccountId == account.Id && t.Date == paidOn && t.Kind == EntryKind.Expense && t.Cents == -row.TotalCents))
                throw new RuleException("Une dépense du même montant existe déjà ce jour sur ce compte. Vérifiez-la et confirmez le prêt sans création de dépense pour éviter un doublon.");
            transactionId = Guid.NewGuid();
            w.Transactions.Add(new(transactionId.Value, account.Id, $"{loan.Name} · échéance {row.Number}", "Crédit", -row.TotalCents, paidOn, EntryKind.Expense, LoanId: id, LoanInstallment: row.Number));
            FinanceService.Reconcile(w, account.Id);
        }
        w.LoanPayments.Add(new(Guid.NewGuid(), id, row.Number, paidOn, transactionId));
    });
    public Task UndoLastAsync(Guid actor, Guid id, int expectedNumber) => store.UpdateAsync(actor, "loan.payment-undone", w =>
    {
        Find(w, id);
        var last = w.LoanPayments.Where(p => p.LoanId == id).MaxBy(p => p.InstallmentNumber) ?? throw new RuleException("Aucun règlement enregistré à annuler. Les échéances importées se corrigent dans le contrat.");
        if (last.InstallmentNumber != expectedNumber) throw new RuleException("Le dernier règlement a changé. Rechargez la page.");
        if (last.TransactionId != null)
        {
            var transaction = w.Transactions.SingleOrDefault(t => t.Id == last.TransactionId) ?? throw new RuleException("Opération liée introuvable.");
            w.Transactions.Remove(transaction); FinanceService.Reconcile(w, transaction.AccountId);
        }
        w.LoanPayments.Remove(last);
    });
    public Task DeleteAsync(Guid actor, Guid id) => store.UpdateAsync(actor, "loan.deleted", w =>
    {
        var loan = Find(w, id);
        if (w.LoanPayments.Any(p => p.LoanId == id) || w.Transactions.Any(t => t.LoanId == id)) throw new RuleException("Annulez les règlements enregistrés avant de supprimer le prêt.");
        w.Loans.Remove(loan);
    });
    public static IReadOnlyList<LoanInstallment> Schedule(Loan loan)
    {
        try { return LoanCalculator.Schedule(loan); }
        catch (ArgumentException e) { throw new RuleException(e.Message); }
    }
    public static string ExportSchedule(Loan loan)
    {
        var csv = new StringBuilder("Echeance;Date;Capital EUR;Interets EUR;Assurance EUR;Total EUR;Capital restant EUR\r\n");
        static string Money(long amount) => (amount / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        foreach (var row in Schedule(loan)) csv.AppendLine(string.Join(';', row.Number.ToString(CultureInfo.InvariantCulture), row.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Money(row.PrincipalCents), Money(row.InterestCents), Money(row.InsuranceCents), Money(row.TotalCents), Money(row.RemainingCents)));
        return csv.ToString();
    }
}
