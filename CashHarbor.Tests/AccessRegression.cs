using System.Text.Json;
using CashHarbor.Application;
using CashHarbor.Domain;
using CashHarbor.Infra;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class AccessRegression
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, IServiceProvider provider, FinanceService finance, Guid first, TimeProvider clock)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<VaultDb>>();
        var store = provider.GetRequiredService<IWorkspaceStore>();
        var identity = provider.GetRequiredService<IIdentityService>();
        Guid second; await using (var db = await factory.CreateDbContextAsync()) second = await db.Users.Where(u => u.Id != first).Select(u => u.Id).SingleAsync();
        var date = finance.Today;
        await finance.AddAccountAsync(first, "Shared savings", "Joint bank", AccountKind.Savings, 500m, date.AddMonths(-3), AccountVisibility.Joint, "Épargne commune");
        await finance.AddAccountAsync(second, "PARTNER PRIVATE ACCOUNT", "PARTNER PRIVATE BANK", AccountKind.Savings, 9876.54m, date.AddMonths(-3));
        var own = (await finance.ReadAsync(first)).Accounts.First(a => a.Visibility == AccountVisibility.Personal).Id;
        var shared = (await finance.ReadAsync(first)).Accounts.Single(a => a.Name == "Shared savings").Id;
        var partner = (await finance.ReadAsync(second)).Accounts.Single(a => a.Name == "PARTNER PRIVATE ACCOUNT").Id;
        await finance.AddTransactionAsync(second, partner, "PARTNER PRIVATE OPERATION", "Autres", 10m, date.AddDays(-3), EntryKind.Income);
        await finance.AddRecurrenceAsync(second, partner, "PARTNER PRIVATE RECURRENCE", "Autres", -5m, date, true);
        await finance.SaveGoalAsync(second, null, "PARTNER PRIVATE GOAL", 200m, 15m, null);
        await finance.RecordBalanceAsync(second, partner, 9999m, date.AddDays(-1));
        var partnerView = await finance.ReadAsync(second);
        var partnerTransaction = partnerView.Transactions.First(t => t.AccountId == partner && t.Kind == EntryKind.Income).Id;
        var partnerRecurrence = partnerView.Recurrences.Single(r => r.AccountId == partner).Id;
        var partnerGoal = partnerView.Goals.Single(g => g.Name == "PARTNER PRIVATE GOAL").Id;

        await test("Personal accounts, goals, recurrences and snapshots never leave their owner's slice", async () =>
        {
            var a = await finance.ReadAsync(first); var b = await finance.ReadAsync(second);
            Check(!JsonSerializer.Serialize(a).Contains("PARTNER PRIVATE"), "Partner content leaked in serialized workspace.");
            Check(!a.Accounts.Any(x => x.Id == partner) && !a.Snapshots.Any(s => s.AccountId == partner), "Private account or snapshot leaked.");
            Check(!b.Accounts.Any(x => x.Id == own), "First user's private account leaked to partner.");
            Check(a.Accounts.Any(x => x.Id == shared) && b.Accounts.Any(x => x.Id == shared), "Joint account not visible to both.");
            Check(!FinanceService.ExportCsv(a).Contains("PARTNER PRIVATE"), "CSV leaked partner data.");
            try { await finance.ReadAsync(Guid.NewGuid()); throw new Exception("Unknown reader accepted."); } catch (UnauthorizedAccessException) { }
        });
        await test("Forged private IDs cannot mutate accounts, operations, goals or recurrences", async () =>
        {
            await Reject(() => finance.RenameAccountAsync(first, partner, "HACK", "HACK"));
            await Reject(() => finance.DeleteAccountAsync(first, partner));
            await Reject(() => finance.ShareAccountAsync(first, partner, true));
            await Reject(() => finance.RecordBalanceAsync(first, partner, 0, date));
            await Reject(() => finance.AddTransactionAsync(first, partner, "HACK", "Autres", 1m, date, EntryKind.Expense));
            await Reject(() => finance.AddTransactionAsync(first, own, "HACK", "Autres", 1m, date, EntryKind.Transfer, partner));
            await Reject(() => finance.DeleteTransactionAsync(first, partnerTransaction));
            await Reject(() => finance.DeleteRecurrenceAsync(first, partnerRecurrence));
            await Reject(() => finance.AddRecurrenceAsync(first, partner, "HACK", "Autres", 1m, date, true));
            await Reject(() => finance.SaveGoalAsync(first, partnerGoal, "HACK", 200m, 10m, null));
            await Reject(() => finance.DeleteGoalAsync(first, partnerGoal));
            await Reject(() => store.UpdateAsync(first, "test.forged-account", w => w.Accounts.Add(partnerView.Accounts.Single(a => a.Id == partner))));
            await Reject(() => store.UpdateAsync(first, "test.forged-operation", w => w.Transactions.Add(partnerView.Transactions.Single(t => t.Id == partnerTransaction) with { AccountId = own })));
        });
        await test("Updates and recurrence booking retain hidden data and private activity stays private", async () =>
        {
            var before = await finance.ReadAsync(second);
            await finance.BookDueAsync(first);
            await finance.RenameAccountAsync(first, own, "My account renamed", "My bank");
            var after = await finance.ReadAsync(second);
            Check(after.Balance(partner) == before.Balance(partner), "Hidden balance changed.");
            Check(after.Recurrences.Single(r => r.Id == partnerRecurrence).NextDate == date, "Partner recurrence booked by another user.");
            Check(after.Transactions.Count(t => t.AccountId == partner) == before.Transactions.Count(t => t.AccountId == partner), "Partner operations changed.");
            Check((await identity.EventsAsync(first)).All(e => e.Actor == first), "Partner activity leaked.");
            await finance.BookDueAsync(second);
            Check((await finance.ReadAsync(second)).Recurrences.Single(r => r.Id == partnerRecurrence).NextDate > date, "Owner could not book recurrence.");
        });
        await test("Joint transfer counterpart stays hidden and partner cannot delete its private leg", async () =>
        {
            await finance.AddTransactionAsync(first, own, "Contribution commune", "Autres", 25m, date, EntryKind.Transfer, shared);
            var a = await finance.ReadAsync(first); var group = a.Transactions.Last(t => t.AccountId == own && t.Kind == EntryKind.Transfer).TransferId;
            var b = await finance.ReadAsync(second); var leg = b.Transactions.Single(t => t.TransferId == group);
            Check(leg.AccountId == shared && leg.Cents == 2500, "Partner received a private transfer leg.");
            await Reject(() => finance.DeleteTransactionAsync(second, leg.Id));
            Check((await finance.ReadAsync(first)).Transactions.Count(t => t.TransferId == group) == 2, "Rejected deletion partially applied.");
            await finance.DeleteTransactionAsync(first, leg.Id);
            Check(!(await finance.ReadAsync(first)).Transactions.Any(t => t.TransferId == group), "Owner could not remove both legs.");
        });
        await test("Personal sharing requires explicit owner consent; joint accounts cannot be made private", async () =>
        {
            await finance.AddAccountAsync(first, "Private to share", "Bank", AccountKind.Savings, 100m, date);
            var id = (await finance.ReadAsync(first)).Accounts.Single(a => a.Name == "Private to share").Id;
            await finance.AddTransactionAsync(first, id, "History to share", "Autres", 10m, date, EntryKind.Income);
            await Reject(() => finance.ShareAccountAsync(second, id, true));
            await Reject(() => finance.ShareAccountAsync(first, id, false));
            Check(!(await finance.ReadAsync(second)).Accounts.Any(a => a.Id == id), "Account shared without consent.");
            await finance.ShareAccountAsync(first, id, true);
            var b = await finance.ReadAsync(second); Check(b.Transactions.Any(t => t.AccountId == id && t.Label == "History to share"), "Shared history unavailable.");
            await Reject(() => store.UpdateAsync(second, "test.unshare", w => { var i = w.Accounts.FindIndex(a => a.Id == id); w.Accounts[i] = w.Accounts[i] with { Visibility = AccountVisibility.Personal, OwnerId = second }; }));
        });
        await test("Observed balances reconcile exactly without creating income or expense", async () =>
        {
            await finance.AddAccountAsync(first, "Valuation", "Bank", AccountKind.Investment, 100m, date.AddMonths(-2));
            var id = (await finance.ReadAsync(first)).Accounts.Single(a => a.Name == "Valuation").Id;
            await finance.RecordBalanceAsync(first, id, 120m, date.AddDays(-10));
            await finance.RecordBalanceAsync(first, id, 150m, date);
            await finance.AddTransactionAsync(first, id, "Older missing entry", "Autres", 5m, date.AddDays(-15), EntryKind.Income);
            var w = await finance.ReadAsync(first);
            Check(w.Balance(id, date.AddDays(-10)) == 12000 && w.Balance(id) == 15000, "Reconciliation changed observed balances.");
            Check(w.Transactions.Where(t => t.AccountId == id && t.Kind == EntryKind.Income).Sum(t => t.Cents) == 500, "Valuation inflated income.");
            await finance.RecordBalanceAsync(first, id, 160m, date);
            w = await finance.ReadAsync(first); Check(w.Balance(id) == 16000 && w.Snapshots.Count(s => s.AccountId == id) == 2, "Replacing a dated observation duplicated it.");
            await Reject(() => finance.AddTransactionAsync(first, id, "Forged adjustment", "Autres", 1m, date, EntryKind.Adjustment));
            await Reject(() => finance.DeleteTransactionAsync(first, w.Transactions.First(t => t.AccountId == id && t.Kind == EntryKind.Adjustment).Id));
        });
        await test("Legacy v1 data is hidden until classified; migration backs up and retains all history", async () =>
        {
            var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("CashHarbor.Workspace.v1");
            var idA = Guid.NewGuid(); var idB = Guid.NewGuid(); var idJoint = Guid.NewGuid(); var txId = Guid.NewGuid(); var goalId = Guid.NewGuid();
            // Exact old shape: no owner, visibility, product or snapshots properties.
            var legacy = JsonSerializer.Serialize(new { Name = "Legacy household", Accounts = new[] {
                new { Id = idA, Name = "Legacy private A", Institution = "Bank A", Kind = 0, OpeningCents = 12345L, OpeningDate = date },
                new { Id = idB, Name = "Legacy private B", Institution = "Bank B", Kind = 1, OpeningCents = 54321L, OpeningDate = date },
                new { Id = idJoint, Name = "Legacy joint", Institution = "Joint bank", Kind = 1, OpeningCents = 50000L, OpeningDate = date }
            }, Transactions = new[] { new { Id = txId, AccountId = idA, Label = "Preserved history", Category = "Autres", Cents = -45L, Date = date, Kind = 0 } },
                Goals = new[] { new { Id = goalId, Name = "Legacy goal", TargetCents = 10000L, SavedCents = 500L, Deadline = (DateOnly?)null } } });
            await using (var db = await factory.CreateDbContextAsync()) { var row = await db.Vaults.SingleAsync(); row.Payload = protector.Protect(legacy); row.Revision++; await db.SaveChangesAsync(); }
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 1");
                await DatabaseSchema.InitializeAsync(db, clock);
                using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA user_version";
                Check(Convert.ToInt32(await command.ExecuteScalarAsync()) == DatabaseSchema.CurrentVersion, "Old application images can still open the private vault.");
            }
            Check((await finance.ReadAsync(first)).Accounts.Count == 0 && (await finance.ReadAsync(second)).Goals.Count == 0, "Legacy data automatically disclosed.");
            var migration = new LegacyAccessMigrator(factory, provider.GetRequiredService<IDataProtectionProvider>(), provider.GetRequiredService<StorageGate>(), clock);
            var plan = await migration.PlanAsync(); Check(plan.Items.Count == 4, "Legacy items not discovered.");
            await Reject(async () => { await migration.ApplyAsync([]); });
            var assignments = new[] { new AccessAssignment(idA, false, AccountVisibility.Personal, first), new AccessAssignment(idB, false, AccountVisibility.Personal, second), new AccessAssignment(idJoint, false, AccountVisibility.Joint, null), new AccessAssignment(goalId, true, AccountVisibility.Joint, null) };
            var backup = await migration.ApplyAsync(assignments); Check(backup != null && File.Exists(backup), "No pre-migration backup.");
            var a = await finance.ReadAsync(first); var b = await finance.ReadAsync(second);
            Check(a.Balance(idA) == 12300 && a.Transactions.Single().Id == txId && a.Goals.Single().SavedCents == 500, "Legacy balances or history lost.");
            Check(!a.Accounts.Any(x => x.Id == idB) && !b.Accounts.Any(x => x.Id == idA) && b.Balance(idB) == 54321 && b.Accounts.Any(x => x.Id == idJoint), "Legacy ownership incorrect.");
            var backupOptions = new DbContextOptionsBuilder<VaultDb>().UseSqlite($"Data Source={backup}").Options;
            await using (var db = new VaultDb(backupOptions)) Check(protector.Unprotect((await db.Vaults.SingleAsync()).Payload) == legacy, "Backup does not preserve original encrypted vault.");
            Check(await migration.ApplyAsync(assignments) == null, "Migration was not idempotent.");
        });
    }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
    private static async Task Reject(Func<Task> work) { try { await work(); } catch (RuleException) { return; } throw new Exception("Expected access or validation rejection."); }
}
