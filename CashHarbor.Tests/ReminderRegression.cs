using CashHarbor.Application;
using CashHarbor.Domain;
using CashHarbor.Infra;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class ReminderRegression
{
    public static async Task Run(IDbContextFactory<VaultDb> factory, IDataProtectionProvider protection, string directory)
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static async Task Reject(Func<Task> task) { try { await task(); } catch (RuleException) { return; } throw new Exception("Invalid reminder operation accepted."); }
        await using var db = await factory.CreateDbContextAsync(); var users = await db.Users.AsNoTracking().ToListAsync();
        var a = users[0].Id; var b = users[1].Id;
        var clock = new ReminderClock(new(2026, 2, 28, 8, 0, 0, TimeSpan.Zero));
        var path = Path.Combine(directory, "reminder-tests");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Directory"] = path }).Build();
        var sender = new FakeMailer(); var gate = new ReminderGate();
        var service = new ReminderService(factory, protection, gate, config, clock, sender);
        Check(!(await service.ReadAsync(a)).Preferences.Enabled, "Reminders enabled without opt-in.");
        await Reject(() => service.SavePreferencesAsync(a, true, 31, 9));
        var smtp = new SmtpSettings("smtp.hostinger.com", 465, "mail@example.test", "mail@example.test");
        await Reject(() => service.SaveSmtpAsync(a, smtp with { Encryption = SmtpEncryption.StartTls }, "synthetic-smtp-password"));
        await service.SaveSmtpAsync(a, smtp, "synthetic-smtp-password");
        await Reject(() => service.SaveSmtpAsync(a, smtp with { Host = "different.example.test" }, ""));
        await Reject(() => service.SavePreferencesAsync(a, true, 0, 9));
        await service.SavePreferencesAsync(a, true, 31, 9);
        Check(!(await service.ReadAsync(b)).Preferences.Enabled, "Partner preferences changed.");
        await service.SendTestAsync(a);
        Check(sender.Sent.Single() == (users[0].Email, true), "Test not restricted to authenticated recipient.");
        await Reject(() => service.SendTestAsync(a));
        await Task.WhenAll(service.DispatchDueAsync(), service.DispatchDueAsync());
        Check(sender.Sent.Count == 2 && sender.Sent[1] == (users[0].Email, false), "Monthly reminder duplicated or test suppressed it.");
        var restart = new ReminderService(factory, protection, new ReminderGate(), config, clock, sender);
        await restart.DispatchDueAsync(); Check(sender.Sent.Count == 2, "Restart duplicated monthly reminder.");
        Check((await restart.ReadAsync(a)).Preferences.SentMonth == "2026-02", "Monthly success not persisted.");
        var bytes = await File.ReadAllTextAsync(Path.Combine(path, "reminders.enc"));
        Check(!bytes.Contains("synthetic-smtp-password") && !bytes.Contains("smtp.hostinger.com") && !bytes.Contains(users[0].Email), "SMTP secrets or reminder state persisted unencrypted.");
        await service.SavePreferencesAsync(b, true, 31, 9);
        sender.Fail = true; await service.DispatchDueAsync(); var attempts = sender.Attempts;
        await service.DispatchDueAsync(); Check(sender.Attempts == attempts, "Failure retry interval ignored.");
        Check((await service.ReadAsync(b)).Preferences.LastSent == null, "Failed SMTP marked sent.");
        sender.Fail = false; clock.Now = clock.Now.AddHours(6); await service.DispatchDueAsync();
        Check(sender.Sent.Count == 3 && sender.Sent[^1] == (users[1].Email, false), "Failure recovery or recipient isolation incorrect.");
        clock.Now = new(2026, 3, 31, 7, 0, 0, TimeSpan.Zero); await service.DispatchDueAsync();
        Check(sender.Sent.Count == 5, "Paris summer time or next month scheduling incorrect.");
        await service.SavePreferencesAsync(a, false, 31, 9); await service.SavePreferencesAsync(b, false, 31, 9);
        clock.Now = clock.Now.AddMonths(1); await service.DispatchDueAsync(); Check(sender.Sent.Count == 5, "Disabled reminders sent.");
        try { await service.ReadAsync(Guid.NewGuid()); throw new Exception("Unknown actor accepted."); } catch (UnauthorizedAccessException) { }
        Check(!ReminderSchedule.IsDue(new(true, 31, 9), new(2028, 2, 29, 7, 59, 0, TimeSpan.Zero)) && ReminderSchedule.IsDue(new(true, 31, 9), new(2028, 2, 29, 8, 0, 0, TimeSpan.Zero)), "Leap-day schedule incorrect.");
    }
    private sealed class ReminderClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakeMailer : IReminderMailer
    {
        public bool Fail; public int Attempts; public List<(string Recipient, bool Test)> Sent = [];
        public Task SendAsync(SmtpSettings settings, string password, string recipient, bool test, CancellationToken ct)
        { Attempts++; if (Fail) throw new IOException("synthetic secret must not be exposed"); Sent.Add((recipient, test)); return Task.CompletedTask; }
    }
}
