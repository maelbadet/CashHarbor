namespace CashHarbor.Application;

public enum SmtpEncryption { Tls, StartTls }
public sealed record SmtpSettings(string Host = "smtp.hostinger.com", int Port = 465, string Username = "", string From = "", SmtpEncryption Encryption = SmtpEncryption.Tls);
public sealed record ReminderPreferences(bool Enabled = false, int Day = 1, int Hour = 9, string? SentMonth = null, DateTimeOffset? LastSent = null, string? Status = null, DateTimeOffset? LastAttempt = null, DateTimeOffset? LastTestAttempt = null);
public sealed record ReminderView(SmtpSettings Smtp, bool HasPassword, string Recipient, ReminderPreferences Preferences);
public interface IReminderService
{
    Task<ReminderView> ReadAsync(Guid actor, CancellationToken ct = default);
    Task SaveSmtpAsync(Guid actor, SmtpSettings settings, string? password, CancellationToken ct = default);
    Task SavePreferencesAsync(Guid actor, bool enabled, int day, int hour, CancellationToken ct = default);
    Task SendTestAsync(Guid actor, CancellationToken ct = default);
    Task DispatchDueAsync(CancellationToken ct = default);
}
public interface IReminderMailer
{
    Task SendAsync(SmtpSettings settings, string password, string recipient, bool test, CancellationToken ct);
}
public static class ReminderSchedule
{
    public static DateTime Local(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).DateTime;
    public static string Month(DateTime local) => local.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
    public static bool IsDue(ReminderPreferences p, DateTimeOffset now)
    {
        var local = Local(now);
        var due = new DateTime(local.Year, local.Month, Math.Min(p.Day, DateTime.DaysInMonth(local.Year, local.Month)), p.Hour, 0, 0);
        return p.Enabled && local >= due && p.SentMonth != Month(local) && (p.LastAttempt == null || now - p.LastAttempt >= TimeSpan.FromHours(6));
    }
}
