using System.Text.Json;
using CashHarbor.Application;
using CashHarbor.Domain;
using CashHarbor.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class RecurrenceRegression
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, IServiceProvider provider, FinanceService finance, Guid actor, TimeProvider clock)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<VaultDb>>(); var today = finance.Today;
        Guid partner; await using (var db = await factory.CreateDbContextAsync()) partner = await db.Users.Where(u => u.Id != actor).Select(u => u.Id).SingleAsync();
        await finance.AddAccountAsync(actor, "Annual bills account", "Bank", AccountKind.Current, 10000m, new DateOnly(2020, 1, 1));
        var account = (await finance.ReadAsync(actor)).Accounts.Single(a => a.Name == "Annual bills account").Id;
        await test("Annual invoice is debited once per year, at full amount, with idempotent catch-up", async () =>
        {
            await finance.AddRecurrenceAsync(actor, account, "Annual water", "Logement", -600m, today.AddYears(-2), true, RecurrenceFrequency.Yearly);
            var before = (await finance.ReadAsync(actor)).Balance(account);
            await finance.BookDueAsync(actor); await finance.BookDueAsync(actor);
            var w = await finance.ReadAsync(actor); var r = w.Recurrences.Single(r => r.Label == "Annual water"); var entries = w.Transactions.Where(t => t.RecurrenceId == r.Id).ToArray();
            Check(entries.Length == 3 && entries.All(t => t.Cents == -60000), "Annual bill was split monthly or duplicated.");
            Check(entries.Select(t => t.Date).SequenceEqual(new[] { today.AddYears(-2), today.AddYears(-1), today }), "Wrong annual catch-up dates.");
            Check(w.Balance(account) == before - 180000 && r.NextDate == today.AddYears(1), "Wrong yearly balance or next due date.");
        });
        await test("Annual leap-day anchor returns to February 29 after non-leap years", async () =>
        {
            await finance.AddRecurrenceAsync(actor, account, "Leap year bill", "Autres", -12m, new DateOnly(2024, 2, 29), true, RecurrenceFrequency.Yearly);
            await finance.BookDueAsync(actor); var w = await finance.ReadAsync(actor); var r = w.Recurrences.Single(r => r.Label == "Leap year bill");
            var dates = w.Transactions.Where(t => t.RecurrenceId == r.Id).Select(t => t.Date).ToArray();
            Check(dates.SequenceEqual(new[] { new DateOnly(2024, 2, 29), new DateOnly(2025, 2, 28), new DateOnly(2026, 2, 28) }), "Leap-day dates incorrect.");
            Check(r.NextDate == new DateOnly(2027, 2, 28) && r.Following(r.NextDate) == new DateOnly(2028, 2, 29), "Leap-day recurrence drifted permanently to February 28.");
            await finance.UpdateRecurrenceAsync(actor, r.Id, r.Label, r.Category, -14m, r.NextDate, r.Essential, r.Frequency);
            var edited = (await finance.ReadAsync(actor)).Recurrences.Single(x => x.Id == r.Id);
            Check(edited.DayOfMonth == 29 && edited.Following(edited.NextDate) == new DateOnly(2028, 2, 29), "Amount-only edit lost original leap-day anchor.");
        });
        await test("Legacy monthly JSON stays monthly; annual essential costs are normalized only for reserve", () =>
        {
            var old = JsonSerializer.Serialize(new { Id = Guid.NewGuid(), AccountId = account, Label = "Legacy monthly", Category = "Autres", Cents = -10000L, NextDate = new DateOnly(2026, 1, 31), DayOfMonth = 31, Essential = true, Active = true });
            var legacy = JsonSerializer.Deserialize<RecurringOperation>(old)!;
            Check(legacy.Frequency == RecurrenceFrequency.Monthly && legacy.Following(legacy.NextDate) == new DateOnly(2026, 2, 28), "Legacy recurrence changed frequency.");
            var annual = legacy with { Id = Guid.NewGuid(), Cents = -60000, Frequency = RecurrenceFrequency.Yearly };
            var w = new Workspace { Recurrences = [legacy, annual, annual with { Id = Guid.NewGuid(), Essential = false }, annual with { Id = Guid.NewGuid(), Cents = 120000 }, annual with { Id = Guid.NewGuid(), Active = false }] };
            Check(FinanceService.EssentialMonthlyCents(w) == 15000, "Annual essential expense counted as monthly or non-essential values included.");
            return Task.CompletedTask;
        });
        await test("Changing recurrence frequency preserves history and rejects private or invalid edits", async () =>
        {
            await finance.AddRecurrenceAsync(actor, account, "Change frequency", "Autres", -10m, today.AddMonths(-1), false);
            await finance.BookDueAsync(actor); var before = await finance.ReadAsync(actor); var r = before.Recurrences.Single(r => r.Label == "Change frequency");
            var history = before.Transactions.Where(t => t.RecurrenceId == r.Id).ToArray();
            await Reject(() => finance.UpdateRecurrenceAsync(partner, r.Id, "FORGED", "Autres", -600m, today.AddYears(1), true, RecurrenceFrequency.Yearly));
            await Reject(() => finance.UpdateRecurrenceAsync(actor, r.Id, "Invalid", "Autres", -600m, today, true, (RecurrenceFrequency)99));
            await Reject(() => finance.AddRecurrenceAsync(actor, account, "Invalid", "Autres", -600m, today, true, (RecurrenceFrequency)99));
            await finance.UpdateRecurrenceAsync(actor, r.Id, "Now annual", "Logement", -600m, today.AddYears(1), true, RecurrenceFrequency.Yearly);
            await finance.BookDueAsync(actor); var after = await finance.ReadAsync(actor); var updated = after.Recurrences.Single(x => x.Id == r.Id);
            Check(updated.Frequency == RecurrenceFrequency.Yearly && updated.Cents == -60000 && updated.AccountId == r.AccountId, "Edited recurrence incorrect.");
            Check(after.Transactions.Where(t => t.RecurrenceId == r.Id).SequenceEqual(history), "Existing operations were changed or a future invoice booked.");
            Check(!(await finance.ReadAsync(partner)).Recurrences.Any(x => x.Id == r.Id), "Edited private recurrence leaked.");
            await finance.AddRecurrenceAsync(actor, account, "Month-end anchor", "Autres", -1m, new DateOnly(2026, 1, 31), false);
            await finance.BookDueAsync(actor); var monthEnd = (await finance.ReadAsync(actor)).Recurrences.Single(x => x.Label == "Month-end anchor");
            await finance.UpdateRecurrenceAsync(actor, monthEnd.Id, monthEnd.Label, monthEnd.Category, -2m, monthEnd.NextDate, false, RecurrenceFrequency.Monthly);
            var edited = (await finance.ReadAsync(actor)).Recurrences.Single(x => x.Id == monthEnd.Id);
            Check(edited.DayOfMonth == 31 && edited.Following(edited.NextDate) == new DateOnly(2026, 12, 31), "Amount-only edit changed a monthly end-of-month anchor.");
        });
        await test("Schema v6 upgrade protects recurring transfers and crypto account classification", async () =>
        {
            await using var db=await factory.CreateDbContextAsync();var payload=(await db.Vaults.SingleAsync()).Payload;
            await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 6");await DatabaseSchema.InitializeAsync(db,clock);
            var directory=Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!,"backups");
            var path=Directory.GetFiles(directory,"before-recurring-transfers-v7-*.db").Single();
            await using var backup=new VaultDb(new DbContextOptionsBuilder<VaultDb>().UseSqlite($"Data Source={path}").Options);
            Check((await backup.Vaults.SingleAsync()).Payload==payload&&(await db.Vaults.SingleAsync()).Payload==payload,"Recurring transfer upgrade changed existing data.");
        });
        await test("Schema v5 upgrade backs up before monthly safety-reserve settings", async () =>
        {
            await using var db=await factory.CreateDbContextAsync();var payload=(await db.Vaults.SingleAsync()).Payload;
            await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 5");await DatabaseSchema.InitializeAsync(db,clock);
            var directory=Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!,"backups");
            var path=Directory.GetFiles(directory,"before-safety-reserve-v6-*.db").Single();
            await using var backup=new VaultDb(new DbContextOptionsBuilder<VaultDb>().UseSqlite($"Data Source={path}").Options);
            Check((await backup.Vaults.SingleAsync()).Payload==payload&&(await db.Vaults.SingleAsync()).Payload==payload,"Safety upgrade modified vault contents.");
        });
        await test("Schema v4 upgrade preserves data and backs up before account-linked goals", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(); var payload=(await db.Vaults.SingleAsync()).Payload;
            await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 4"); await DatabaseSchema.InitializeAsync(db,clock);
            using var read=db.Database.GetDbConnection().CreateCommand();read.CommandText="PRAGMA user_version";
            Check(Convert.ToInt32(await read.ExecuteScalarAsync())==DatabaseSchema.CurrentVersion,"Linked goal downgrade protection missing.");
            var directory=Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!,"backups");
            var path=Directory.GetFiles(directory,"before-linked-goals-v5-*.db").Single();
            await using var backup=new VaultDb(new DbContextOptionsBuilder<VaultDb>().UseSqlite($"Data Source={path}").Options);
            Check((await backup.Vaults.SingleAsync()).Payload==payload&&(await db.Vaults.SingleAsync()).Payload==payload,"Goal upgrade lost vault data.");
        });
        await test("Schema v3 upgrade backs up encrypted data and marks annual recurrence compatibility", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(); var payload = (await db.Vaults.SingleAsync()).Payload;
            await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 3"); await DatabaseSchema.InitializeAsync(db, clock);
            using var read = db.Database.GetDbConnection().CreateCommand(); read.CommandText = "PRAGMA user_version";
            Check(Convert.ToInt32(await read.ExecuteScalarAsync()) == DatabaseSchema.CurrentVersion, "Old monthly-only image can still reopen annual recurrences.");
            var directory = Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!, "backups");
            var backup = Directory.GetFiles(directory, "before-annual-recurrences-v4-*.db").Single();
            await using var snapshot = new VaultDb(new DbContextOptionsBuilder<VaultDb>().UseSqlite($"Data Source={backup}").Options);
            Check((await snapshot.Vaults.SingleAsync()).Payload == payload && (await db.Vaults.SingleAsync()).Payload == payload, "Upgrade changed or lost existing data.");
        });
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> work) { try { await work(); } catch (RuleException) { return; } throw new Exception("Expected recurrence rule rejection."); }
}
