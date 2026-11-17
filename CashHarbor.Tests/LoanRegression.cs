using System.Text.Json;
using CashHarbor.Application;
using CashHarbor.Domain;
using CashHarbor.Infra;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class LoanRegression
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, IServiceProvider provider, FinanceService finance, Guid actor, TimeProvider clock)
    {
        var store = provider.GetRequiredService<IWorkspaceStore>(); var loans = new LoanService(store, clock);
        var factory = provider.GetRequiredService<IDbContextFactory<VaultDb>>(); var date = finance.Today;
        Guid partner; await using (var db = await factory.CreateDbContextAsync()) partner = await db.Users.Where(u => u.Id != actor).Select(u => u.Id).SingleAsync();
        Loan Prototype(decimal rate = 5m, LoanInsuranceKind insurance = LoanInsuranceKind.FixedMonthly) => new(Guid.NewGuid(), "Test loan", "Bank", LoanKind.Mortgage, 10_000_000, rate, 120, new DateOnly(2026, 1, 31), insurance, 1000, .36m, 20000, 5.8m, 0, date, AccountVisibility.Personal, actor);
        LoanDraft Draft(string name, Guid? account = null, AccountVisibility visibility = AccountVisibility.Personal, int initial = 0) => new(name, "Bank", LoanKind.Consumer, 12000m, 4.2m, 48, date.AddMonths(-2), LoanInsuranceKind.FixedMonthly, 12m, 0m, 50m, 4.8m, initial, visibility, account);

        await test("Amortization: independent payment reference, exact capital and zero final balance", () =>
        {
            var loan = Prototype(); var rows = LoanCalculator.Schedule(loan);
            var rate = .05d / 12; var independent = 100000d * rate / (1 - Math.Pow(1 + rate, -120));
            Check(Math.Abs(rows[0].PrincipalCents + rows[0].InterestCents - (long)Math.Round(independent * 100, MidpointRounding.AwayFromZero)) <= 1, "Monthly payment differs from reference formula.");
            Check(rows.Count == 120 && rows.Sum(r => r.PrincipalCents) == loan.PrincipalCents && rows[^1].RemainingCents == 0, "Principal not conserved.");
            Check(rows.All(r => r.PrincipalCents > 0 && r.InterestCents >= 0 && r.RemainingCents >= 0), "Negative amortization component.");
            Check(rows[1].DueDate == new DateOnly(2026, 2, 28) && rows[2].DueDate == new DateOnly(2026, 3, 31), "Month-end dates drift.");
            return Task.CompletedTask;
        });
        await test("Zero-rate credit and each insurance mode use the correct capital base", () =>
        {
            var zero = Prototype(0) with { PrincipalCents = 120000, TermMonths = 12 };
            var rows = LoanCalculator.Schedule(zero); Check(rows.Count == 12 && rows.All(r => r.InterestCents == 0 && r.PrincipalCents == 10000), "Zero-rate schedule incorrect.");
            var initial = LoanCalculator.Schedule(Prototype(5, LoanInsuranceKind.InitialCapitalRate));
            var remaining = LoanCalculator.Schedule(Prototype(5, LoanInsuranceKind.RemainingCapitalRate));
            Check(initial.All(r => r.InsuranceCents == 3000), "Insurance on original capital incorrect.");
            Check(remaining[0].InsuranceCents == 3000 && remaining[^1].InsuranceCents < remaining[0].InsuranceCents, "Insurance on remaining balance does not decrease.");
            Check(LoanCalculator.Schedule(Prototype() with { ContractApr = 9m }).SequenceEqual(LoanCalculator.Schedule(Prototype() with { ContractApr = 1m })), "Contract APR incorrectly used as nominal rate.");
            return Task.CompletedTask;
        });
        await test("Loan validation rejects impossible rates, excessive duration and invalid paid counts", async () =>
        {
            foreach (var loan in new[] { Prototype(-1), Prototype(101), Prototype() with { TermMonths = 0 }, Prototype() with { TermMonths = 601 }, Prototype() with { PrincipalCents = 0 }, Prototype() with { AnnualNominalRate = .00001m } })
            { try { LoanCalculator.Schedule(loan); throw new Exception("Invalid loan accepted."); } catch (ArgumentException) { } }
            await Reject(() => loans.SaveAsync(actor, null, Draft("Bad") with { InitialPaidInstallments = 49 }));
            await Reject(() => loans.SaveAsync(actor, null, Draft("Bad") with { FirstDueDate = date.AddMonths(1), InitialPaidInstallments = 1 }));
            await Reject(() => loans.SaveAsync(actor, null, Draft("Bad") with { Visibility = AccountVisibility.Unassigned }));
        });
        await finance.AddAccountAsync(actor, "Loan personal account", "Bank", AccountKind.Current, 20000m, date.AddYears(-1));
        await finance.AddAccountAsync(actor, "Loan joint account", "Bank", AccountKind.Current, 20000m, date.AddYears(-1), AccountVisibility.Joint);
        await finance.AddAccountAsync(partner, "LOAN_PARTNER_PRIVATE_ACCOUNT", "Bank", AccountKind.Current, 20000m, date.AddYears(-1));
        var account = (await finance.ReadAsync(actor)).Accounts.Single(a => a.Name == "Loan personal account").Id;
        var jointAccount = (await finance.ReadAsync(actor)).Accounts.Single(a => a.Name == "Loan joint account").Id;
        var partnerAccount = (await finance.ReadAsync(partner)).Accounts.Single(a => a.Name == "LOAN_PARTNER_PRIVATE_ACCOUNT").Id;
        await loans.SaveAsync(actor, null, Draft("My private loan", account));
        await loans.SaveAsync(actor, null, Draft("Our joint loan", jointAccount, AccountVisibility.Joint));
        await loans.SaveAsync(partner, null, Draft("LOAN_PARTNER_PRIVATE_LOAN", partnerAccount));
        var own = (await finance.ReadAsync(actor)).Loans.Single(l => l.Name == "My private loan");
        var common = (await finance.ReadAsync(actor)).Loans.Single(l => l.Visibility == AccountVisibility.Joint);
        var hidden = (await finance.ReadAsync(partner)).Loans.Single(l => l.OwnerId == partner);
        await test("Loan privacy covers reads, writes, payments and repayment-account relationships", async () =>
        {
            Check(!JsonSerializer.Serialize(await finance.ReadAsync(actor)).Contains("LOAN_PARTNER_PRIVATE"), "Private partner loan leaked.");
            Check(!(await finance.ReadAsync(partner)).Loans.Any(l => l.Id == own.Id), "Own loan leaked to partner.");
            Check((await finance.ReadAsync(partner)).Loans.Any(l => l.Id == common.Id), "Common loan missing for partner.");
            await Reject(() => loans.SaveAsync(actor, hidden.Id, Draft("FORGED")));
            await Reject(() => loans.DeleteAsync(actor, hidden.Id));
            await Reject(() => loans.ConfirmAsync(actor, hidden.Id, 1, date, false));
            await Reject(() => loans.UndoLastAsync(actor, hidden.Id, 1));
            await Reject(() => loans.SetPaymentAccountAsync(actor, hidden.Id, account));
            await Reject(() => loans.SaveAsync(actor, null, Draft("Bad private linked to joint", jointAccount)));
            await Reject(() => loans.SaveAsync(actor, null, Draft("Bad joint linked to private", account, AccountVisibility.Joint)));
            await Reject(() => loans.SetPaymentAccountAsync(actor, own.Id, partnerAccount));
            await Reject(() => finance.ShareAccountAsync(actor, account, true));
            await loans.SetPaymentAccountAsync(actor, own.Id, null);
            await loans.SetPaymentAccountAsync(actor, own.Id, account);
        });
        await test("Payment confirmation is atomic, idempotent and optionally creates exactly one expense", async () =>
        {
            var before = await finance.ReadAsync(actor); var first = LoanCalculator.Schedule(own)[0];
            await loans.ConfirmAsync(actor, own.Id, 1, date, true);
            var w = await finance.ReadAsync(actor); var payment = w.LoanPayments.Single(p => p.LoanId == own.Id);
            var netBefore = before.Accounts.Sum(a => before.Balance(a.Id)) - before.Loans.Sum(l => LoanCalculator.Remaining(l, before.LoanPayments));
            var netAfter = w.Accounts.Sum(a => w.Balance(a.Id)) - w.Loans.Sum(l => LoanCalculator.Remaining(l, w.LoanPayments));
            Check(netBefore - netAfter == first.InterestCents + first.InsuranceCents, "Principal was counted twice in net tracked assets.");
            Check(w.Balance(account) == before.Balance(account) - first.TotalCents, "Expense not applied correctly.");
            Check(LoanCalculator.Remaining(own, w.LoanPayments) == own.PrincipalCents - first.PrincipalCents, "Interest or insurance reduced principal.");
            Check(w.Transactions.Count(t => t.LoanId == own.Id) == 1 && payment.TransactionId != null, "Duplicate or missing loan expense.");
            await Reject(() => loans.ConfirmAsync(actor, own.Id, 1, date, true));
            await Reject(() => finance.DeleteTransactionAsync(actor, payment.TransactionId!.Value));
            await Reject(() => loans.DeleteAsync(actor, own.Id));
            await Reject(() => loans.SaveAsync(actor, own.Id, Draft("Changed contract", account)));
            await Reject(() => finance.ShareAccountAsync(actor, account, true));
            await loans.UndoLastAsync(actor, own.Id, 1);
            w = await finance.ReadAsync(actor); Check(w.Balance(account) == before.Balance(account) && LoanCalculator.Remaining(own, w.LoanPayments) == own.PrincipalCents, "Undo did not restore account and loan together.");
            await loans.ConfirmAsync(actor, own.Id, 1, date, false);
            w = await finance.ReadAsync(actor); Check(w.Balance(account) == before.Balance(account) && !w.Transactions.Any(t => t.LoanId == own.Id), "Tracking-only payment created a debit.");
            await loans.UndoLastAsync(actor, own.Id, 1);
        });
        await test("Existing expense is not duplicated and shared loan can be paid by either user", async () =>
        {
            var next = LoanCalculator.Schedule(own)[0];
            await finance.AddTransactionAsync(actor, account, "Existing credit debit", "Crédit", next.TotalCents / 100m, date, EntryKind.Expense);
            await Reject(() => loans.ConfirmAsync(actor, own.Id, 1, date, true));
            Check(!(await finance.ReadAsync(actor)).LoanPayments.Any(p => p.LoanId == own.Id), "Rejected duplicate partially reduced debt.");
            await loans.ConfirmAsync(actor, own.Id, 1, date, false);
            var before = (await finance.ReadAsync(actor)).Balance(jointAccount);
            await loans.ConfirmAsync(partner, common.Id, 1, date, true);
            var after = await finance.ReadAsync(actor); Check(after.Balance(jointAccount) < before && after.LoanPayments.Any(p => p.LoanId == common.Id), "Partner could not pay shared loan.");
            Check(!(await finance.ReadAsync(partner)).LoanPayments.Any(p => p.LoanId == own.Id), "Private payment leaked to partner.");
            await loans.UndoLastAsync(actor, common.Id, 1);
            Check((await finance.ReadAsync(actor)).Balance(jointAccount) == before, "Shared repayment undo failed.");
        });
        await test("Existing loan import uses declared paid count without reconstructing old debits", async () =>
        {
            var balance = (await finance.ReadAsync(actor)).Balance(account);
            await loans.SaveAsync(actor, null, Draft("Imported loan", account, initial: 2));
            var w = await finance.ReadAsync(actor); var loan = w.Loans.Single(l => l.Name == "Imported loan");
            Check(LoanCalculator.Remaining(loan, w.LoanPayments) == LoanCalculator.Schedule(loan)[1].RemainingCents, "Imported capital incorrect.");
            Check(w.Balance(account) == balance && !w.LoanPayments.Any(p => p.LoanId == loan.Id), "Import generated historical transactions.");
            Check(LoanService.ExportSchedule(loan).Contains("Capital restant EUR") && !LoanService.ExportSchedule(loan).Contains(loan.Name), "Schedule CSV invalid or includes unsafe user formula content.");
            await using var db = await factory.CreateDbContextAsync(); Check(!(await db.Vaults.SingleAsync()).Payload.Contains("Imported loan"), "Loan not encrypted.");
        });
        await test("Schema v2 upgrade creates a readable backup and preserves the entire encrypted vault", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(); var payload = (await db.Vaults.SingleAsync()).Payload;
            await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 2");
            await DatabaseSchema.InitializeAsync(db, clock);
            using var read = db.Database.GetDbConnection().CreateCommand(); read.CommandText = "PRAGMA user_version";
            Check(Convert.ToInt32(await read.ExecuteScalarAsync()) == DatabaseSchema.CurrentVersion, "Schema version did not block old v2 images.");
            Check((await db.Vaults.SingleAsync()).Payload == payload, "Schema upgrade rewrote or lost financial data.");
            var directory = Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!, "backups");
            var backup = Directory.GetFiles(directory, "before-loans-v3-*.db").Single();
            await using var snapshot = new VaultDb(new DbContextOptionsBuilder<VaultDb>().UseSqlite($"Data Source={backup}").Options);
            Check((await snapshot.Vaults.SingleAsync()).Payload == payload, "Pre-upgrade backup differs from original.");
        });
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> work) { try { await work(); } catch (RuleException) { return; } throw new Exception("Expected loan rule rejection."); }
}
