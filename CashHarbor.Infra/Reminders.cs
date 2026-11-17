using System.Text.Json;
using CashHarbor.Application;
using CashHarbor.Domain;
using MailKit;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace CashHarbor.Infra;

public sealed class ReminderGate { public SemaphoreSlim Lock { get; } = new(1, 1); }
public sealed class ReminderState
{
    public SmtpSettings Smtp { get; set; } = new();
    public string Password { get; set; } = "";
    public Dictionary<Guid, ReminderPreferences> Users { get; set; } = [];
}
// Separate encrypted file in the same persistent /data volume. No financial data enters email.
public sealed class ReminderService(IDbContextFactory<VaultDb> factory, IDataProtectionProvider protection, ReminderGate gate,
    IConfiguration configuration, TimeProvider clock, IReminderMailer mailer) : IReminderService
{
    private readonly IDataProtector protector = protection.CreateProtector("CashHarbor.Reminders.v1");
    private string PathName => Path.Combine(Path.GetFullPath(configuration["Storage:Directory"] ?? "App_Data"), "reminders.enc");
    private async Task<ReminderState> Load(CancellationToken ct) => File.Exists(PathName)
        ? JsonSerializer.Deserialize<ReminderState>(protector.Unprotect(await File.ReadAllTextAsync(PathName, ct))) ?? throw new InvalidDataException("Invalid reminders file.") : new();
    private async Task Persist(ReminderState state, CancellationToken ct)
    {
        var path = PathName; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, protector.Protect(JsonSerializer.Serialize(state)), ct);
        File.Move(temporary, path, true);
    }
    private async Task<string> Recipient(Guid actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.Where(u => u.Id == actor).Select(u => u.Email).SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
    }
    public async Task<ReminderView> ReadAsync(Guid actor, CancellationToken ct = default)
    {
        var recipient = await Recipient(actor, ct); await gate.Lock.WaitAsync(ct);
        try { var state = await Load(ct); return new(state.Smtp, state.Password.Length > 0, recipient, state.Users.GetValueOrDefault(actor) ?? new()); }
        finally { gate.Lock.Release(); }
    }
    public async Task SaveSmtpAsync(Guid actor, SmtpSettings settings, string? password, CancellationToken ct = default)
    {
        await Recipient(actor, ct);
        settings = settings with { Host = settings.Host.Trim(), Username = settings.Username.Trim(), From = settings.From.Trim() };
        if (settings.Host.Length is < 1 or > 253 || Uri.CheckHostName(settings.Host) == UriHostNameType.Unknown || settings.Port is not (465 or 587) || !Enum.IsDefined(settings.Encryption) ||
            settings.Port == 465 && settings.Encryption != SmtpEncryption.Tls || settings.Port == 587 && settings.Encryption != SmtpEncryption.StartTls ||
            settings.Username.Length is < 1 or > 254 || settings.Username.Any(char.IsControl) || !MailboxAddress.TryParse(settings.From, out var from) || from.Address != settings.From || password?.Length > 1024)
            throw new RuleException("Vérifiez le serveur, l’adresse d’envoi et le chiffrement : TLS sur 465 ou STARTTLS sur 587.");
        await gate.Lock.WaitAsync(ct);
        try
        {
            var state = await Load(ct);
            // Do not reuse credentials silently for a different server or username.
            if (string.IsNullOrEmpty(password) && (state.Smtp.Host != settings.Host || state.Smtp.Username != settings.Username || state.Password.Length == 0))
                throw new RuleException("Renseignez le mot de passe SMTP pour ce serveur et cet identifiant.");
            state.Smtp = settings; if (!string.IsNullOrEmpty(password)) state.Password = password;
            await Persist(state, ct);
        }
        finally { gate.Lock.Release(); }
    }
    public async Task SavePreferencesAsync(Guid actor, bool enabled, int day, int hour, CancellationToken ct = default)
    {
        await Recipient(actor, ct);
        if (day is < 1 or > 31 || hour is < 0 or > 23) throw new RuleException("Choisissez un jour de 1 à 31 et une heure de 0 à 23.");
        await gate.Lock.WaitAsync(ct);
        try
        {
            var state = await Load(ct);
            if (enabled && state.Password.Length == 0) throw new RuleException("Configurez d’abord le SMTP.");
            var old = state.Users.GetValueOrDefault(actor) ?? new();
            state.Users[actor] = old with { Enabled = enabled, Day = day, Hour = hour }; await Persist(state, ct);
        }
        finally { gate.Lock.Release(); }
    }
    public async Task SendTestAsync(Guid actor, CancellationToken ct = default)
    {
        var recipient = await Recipient(actor, ct); await gate.Lock.WaitAsync(ct);
        try
        {
            var state = await Load(ct); var p = state.Users.GetValueOrDefault(actor) ?? new();
            if (state.Password.Length == 0) throw new RuleException("Configurez d’abord le SMTP.");
            if (p.LastTestAttempt != null && clock.GetUtcNow() - p.LastTestAttempt < TimeSpan.FromMinutes(5)) throw new RuleException("Patientez cinq minutes avant un nouvel envoi.");
            state.Users[actor] = p with { LastTestAttempt = clock.GetUtcNow() }; await Persist(state, ct);
            try { await Deliver(state, recipient, true, ct); state.Users[actor] = state.Users[actor] with { Status = "Test accepté par le serveur SMTP." }; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { state.Users[actor] = state.Users[actor] with { Status = "Échec du test : vérifiez la configuration SMTP et l’accès réseau." }; await Persist(state, ct); throw new RuleException(state.Users[actor].Status!); }
            await Persist(state, ct);
        }
        finally { gate.Lock.Release(); }
    }
    private async Task Deliver(ReminderState state, string recipient, bool test, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await mailer.SendAsync(state.Smtp, state.Password, recipient, test, deadline.Token);
    }
    public async Task DispatchDueAsync(CancellationToken ct = default)
    {
        await gate.Lock.WaitAsync(ct);
        try
        {
            var state = await Load(ct); if (state.Password.Length == 0) return;
            await using var db = await factory.CreateDbContextAsync(ct); var users = await db.Users.AsNoTracking().ToListAsync(ct);
            foreach (var user in users)
            {
                var p = state.Users.GetValueOrDefault(user.Id) ?? new(); var now = clock.GetUtcNow();
                if (!ReminderSchedule.IsDue(p, now)) continue;
                state.Users[user.Id] = p with { LastAttempt = now }; await Persist(state, ct);
                try
                {
                    await Deliver(state, user.Email, false, ct);
                    state.Users[user.Id] = state.Users[user.Id] with { LastSent = now, SentMonth = ReminderSchedule.Month(ReminderSchedule.Local(now)), Status = "Rappel accepté par le serveur SMTP." };
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { state.Users[user.Id] = state.Users[user.Id] with { Status = "Échec SMTP ; nouvelle tentative dans six heures." }; }
                await Persist(state, ct);
            }
        }
        finally { gate.Lock.Release(); }
    }
}
public sealed class SmtpReminderMailer : IReminderMailer
{
    public async Task SendAsync(SmtpSettings settings, string password, string recipient, bool test, CancellationToken ct)
    {
        var message = new MimeMessage(); message.From.Add(MailboxAddress.Parse(settings.From)); message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = test ? "CashHarbor — test des rappels" : "CashHarbor — votre point mensuel";
        message.Body = new TextPart("plain") { Text = test ? "Votre configuration SMTP fonctionne. Aucun solde ni donnée bancaire n’est envoyé par e-mail." :
            "Bonjour,\n\nC’est le moment de mettre à jour vos soldes et vos opérations dans CashHarbor. Ouvrez votre application privée via Tailscale pour faire votre point mensuel.\n\nVous pouvez désactiver ce rappel dans Paramètres.\n\nCashHarbor" };
        using var smtp = new MailKit.Net.Smtp.SmtpClient { Timeout = 25000 };
        await smtp.ConnectAsync(settings.Host, settings.Port, settings.Encryption == SmtpEncryption.Tls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await smtp.AuthenticateAsync(settings.Username, password, ct); await smtp.SendAsync(message, ct);
        // Acceptance has already succeeded; a failed QUIT must not cause a second delivery.
        try { await smtp.DisconnectAsync(true, ct); } catch { }
    }
}
