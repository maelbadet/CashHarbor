namespace CashHarbor.Domain;

public enum LoanKind { Mortgage, Consumer, Vehicle, Student, Other }
public enum LoanInsuranceKind { FixedMonthly, InitialCapitalRate, RemainingCapitalRate }

public sealed record Loan(Guid Id, string Name, string Lender, LoanKind Kind, long PrincipalCents,
    decimal AnnualNominalRate, int TermMonths, DateOnly FirstDueDate, LoanInsuranceKind InsuranceKind,
    long MonthlyInsuranceCents, decimal AnnualInsuranceRate, long FeesCents, decimal? ContractApr,
    int InitialPaidInstallments, DateOnly TrackingStartedOn, AccountVisibility Visibility, Guid? OwnerId,
    Guid? PaymentAccountId = null);

public sealed record LoanPayment(Guid Id, Guid LoanId, int InstallmentNumber, DateOnly PaidOn, Guid? TransactionId);
public sealed record LoanInstallment(int Number, DateOnly DueDate, long PrincipalCents, long InterestCents,
    long InsuranceCents, long RemainingCents)
{
    public long TotalCents => PrincipalCents + InterestCents + InsuranceCents;
}

public static class LoanCalculator
{
    public static IReadOnlyList<LoanInstallment> Schedule(Loan loan)
    {
        if (loan.PrincipalCents is < 1 or > 10_000_000_000L || loan.TermMonths is < 1 or > 600 ||
            !Rate(loan.AnnualNominalRate) || !Rate(loan.AnnualInsuranceRate) || loan.ContractApr is { } apr && !Rate(apr) ||
            !Enum.IsDefined(loan.Kind) || !Enum.IsDefined(loan.InsuranceKind) ||
            loan.MonthlyInsuranceCents is < 0 or > 10_000_000_000L || loan.FeesCents is < 0 or > 10_000_000_000L ||
            loan.FirstDueDate.Year < 1900 || loan.FirstDueDate.Year > 2100)
            throw new ArgumentException("Vérifiez le capital, les taux (0 à 100 %, quatre décimales), les frais et la durée (1 à 600 mois).");
        var monthlyRate = loan.AnnualNominalRate / 1200m;
        decimal inverseFactor = 1m;
        for (var i = 0; i < loan.TermMonths; i++) inverseFactor /= 1m + monthlyRate;
        var regular = Cents(monthlyRate == 0 ? loan.PrincipalCents / (decimal)loan.TermMonths : loan.PrincipalCents * monthlyRate / (1m - inverseFactor));
        if (regular < 1 || loan.TermMonths > 1 && regular <= Cents(loan.PrincipalCents * monthlyRate))
            throw new ArgumentException("Ces paramètres ne permettent pas un amortissement mensuel au centime. Vérifiez le contrat.");
        var balance = loan.PrincipalCents;
        var rows = new List<LoanInstallment>();
        for (var number = 1; number <= loan.TermMonths && balance > 0; number++)
        {
            var interest = Cents(balance * monthlyRate);
            var insurance = loan.InsuranceKind switch
            {
                LoanInsuranceKind.InitialCapitalRate => Cents(loan.PrincipalCents * loan.AnnualInsuranceRate / 1200m),
                LoanInsuranceKind.RemainingCapitalRate => Cents(balance * loan.AnnualInsuranceRate / 1200m),
                _ => loan.MonthlyInsuranceCents
            };
            var principal = number == loan.TermMonths ? balance : Math.Min(balance, regular - interest);
            balance -= principal;
            var month = new DateOnly(loan.FirstDueDate.Year, loan.FirstDueDate.Month, 1).AddMonths(number - 1);
            var due = month.AddDays(Math.Min(loan.FirstDueDate.Day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
            rows.Add(new(number, due, principal, interest, insurance, balance));
        }
        return rows;
    }
    public static int PaidCount(Loan loan, IEnumerable<LoanPayment> payments) => loan.InitialPaidInstallments + payments.Count(p => p.LoanId == loan.Id);
    public static long Remaining(Loan loan, IEnumerable<LoanPayment> payments)
    {
        var rows = Schedule(loan); var paid = PaidCount(loan, payments);
        if (paid < 0 || paid > rows.Count) throw new ArgumentException("Nombre d’échéances réglées invalide.");
        return paid == 0 ? loan.PrincipalCents : rows[paid - 1].RemainingCents;
    }
    private static bool Rate(decimal value) => value is >= 0 and <= 100 && decimal.Round(value, 4) == value;
    private static long Cents(decimal value) => checked((long)decimal.Round(value, 0, MidpointRounding.AwayFromZero));
}
