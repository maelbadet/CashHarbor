using CashHarbor.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CashHarbor.Infra;

public sealed class IdentityService(IDbContextFactory<VaultDb> factory, IDataProtectionProvider protection, StorageGate gate, TimeProvider clock) : IIdentityService
{
    private readonly IDataProtector protector = protection.CreateProtector("CashHarbor.Totp.v1");
    private static readonly PasswordHasher<LocalUser> Hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private static readonly string DummyHash = Hasher.HashPassword(new(), Guid.NewGuid().ToString());
    private static string Email(string value) => value.Trim().ToLowerInvariant();
    private static void Password(string value)
    {
        if (value.Length is < 14 or > 128 || string.IsNullOrWhiteSpace(value)) throw new RuleException("Utilisez un mot de passe unique de 14 à 128 caractères.");
    }
    public async Task<UserSession?> LoginAsync(string email, string password, string code, CancellationToken ct = default)
    {
        if (email.Length > 254 || password.Length > 128 || code.Length > 10) return null;
        await gate.Lock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == Email(email), ct);
            var result = Hasher.VerifyHashedPassword(user ?? new(), user?.PasswordHash ?? DummyHash, password);
            var now = clock.GetUtcNow();
            long? step = user == null ? null : Totp.Match(protector.Unprotect(user.ProtectedSecret), code, now, user.LastTotpStep);
            if (user == null || user.LockedUntil > now.ToUnixTimeSeconds() || result == PasswordVerificationResult.Failed || step == null)
            {
                if (user != null && user.LockedUntil <= now.ToUnixTimeSeconds())
                {
                    user.Failures++;
                    if (user.Failures >= 5) { user.LockedUntil = now.AddMinutes(15).ToUnixTimeSeconds(); user.Failures = 0; }
                }
                db.Audit.Add(new() { Actor = user?.Id, At = now, Action = "login.failed" });
                await db.SaveChangesAsync(ct); return null;
            }
            user.Failures = 0; user.LockedUntil = 0; user.LastTotpStep = step.Value;
            if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = Hasher.HashPassword(user, password);
            db.Audit.Add(new() { Actor = user.Id, At = now, Action = "login.success" });
            await db.SaveChangesAsync(ct);
            return new(user.Id, user.Email, user.Version);
        }
        finally { gate.Lock.Release(); }
    }
    public async Task<bool> IsValidAsync(Guid id, string version, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(u => u.Id == id && u.Version == version, ct);
    }
    public async Task RevokeSessionsAsync(Guid id, CancellationToken ct = default)
    {
        await gate.Lock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var user = await db.Users.SingleAsync(u => u.Id == id, ct); user.Version = Guid.NewGuid().ToString();
            db.Audit.Add(new() { Actor = id, At = clock.GetUtcNow(), Action = "sessions.revoked" }); await db.SaveChangesAsync(ct);
        }
        finally { gate.Lock.Release(); }
    }
    public async Task ProvisionAsync(string email, string password, string secret, string code, bool reset, CancellationToken ct = default)
    {
        email = Email(email); Password(password);
        if (email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email) throw new RuleException("Adresse e-mail invalide.");
        var step = Totp.Match(secret, code, clock.GetUtcNow()) ?? throw new RuleException("Code de double authentification incorrect.");
        await gate.Lock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
            if (reset && user == null) throw new RuleException("Utilisateur introuvable.");
            if (!reset && user != null) throw new RuleException("Cet utilisateur existe déjà.");
            if (user == null)
            {
                if (await db.Users.CountAsync(ct) >= 2) throw new RuleException("Les deux accès du foyer sont déjà créés.");
                user = new() { Email = email }; db.Users.Add(user);
            }
            user.PasswordHash = Hasher.HashPassword(user, password); user.ProtectedSecret = protector.Protect(secret);
            user.Version = Guid.NewGuid().ToString(); user.Failures = 0; user.LockedUntil = 0; user.LastTotpStep = step;
            db.Audit.Add(new() { Actor = user.Id, At = clock.GetUtcNow(), Action = reset ? "user.reset" : "user.created" });
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Lock.Release(); }
    }
    public async Task ChangePasswordAsync(Guid id, string current, string password, string code, CancellationToken ct = default)
    {
        Password(password); if (current.Length > 128) throw new RuleException("Identifiants invalides.");
        await gate.Lock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var user = await db.Users.SingleAsync(u => u.Id == id, ct);
            var now = clock.GetUtcNow();
            var step = Totp.Match(protector.Unprotect(user.ProtectedSecret), code, now, user.LastTotpStep);
            if (user.LockedUntil > now.ToUnixTimeSeconds()) throw new RuleException("Accès temporairement verrouillé.");
            if (Hasher.VerifyHashedPassword(user, user.PasswordHash, current) == PasswordVerificationResult.Failed || step == null)
            {
                if (++user.Failures >= 5) { user.LockedUntil = now.AddMinutes(15).ToUnixTimeSeconds(); user.Failures = 0; }
                db.Audit.Add(new() { Actor = id, At = now, Action = "password.failed" }); await db.SaveChangesAsync(ct);
                throw new RuleException("Identifiants invalides. Utilisez un nouveau code à six chiffres.");
            }
            user.PasswordHash = Hasher.HashPassword(user, password); user.Version = Guid.NewGuid().ToString(); user.LastTotpStep = step.Value; user.Failures = 0;
            db.Audit.Add(new() { Actor = id, At = now, Action = "password.changed" }); await db.SaveChangesAsync(ct);
        }
        finally { gate.Lock.Release(); }
    }
    public async Task<IReadOnlyList<SecurityEvent>> EventsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Audit.AsNoTracking().OrderByDescending(a => a.Id).Take(50).Select(a => new SecurityEvent(a.At, a.Action, a.Actor)).ToListAsync(ct);
    }
}
