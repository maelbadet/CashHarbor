using System.Text.Json;
using CashHarbor.Application;
using CashHarbor.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CashHarbor.Infra;

public sealed class VaultRow
{
    public string Id { get; set; } = Workspace.SharedId;
    public string Payload { get; set; } = "";
    public long Revision { get; set; }
}
public sealed class LocalUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string ProtectedSecret { get; set; } = "";
    public string Version { get; set; } = Guid.NewGuid().ToString();
    public int Failures { get; set; }
    public long LockedUntil { get; set; }
    public long LastTotpStep { get; set; } = -1;
}
public sealed class AuditRow
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? Actor { get; set; }
    public string Action { get; set; } = "";
}
public sealed class VaultDb(DbContextOptions<VaultDb> options) : DbContext(options)
{
    public DbSet<VaultRow> Vaults => Set<VaultRow>();
    public DbSet<LocalUser> Users => Set<LocalUser>();
    public DbSet<AuditRow> Audit => Set<AuditRow>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<VaultRow>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<LocalUser>().HasIndex(x => x.Email).IsUnique();
        model.Entity<LocalUser>().Property(x => x.Version).IsConcurrencyToken();
    }
}
public sealed class StorageGate { public SemaphoreSlim Lock { get; } = new(1, 1); }

public sealed class EncryptedWorkspaceStore(IDbContextFactory<VaultDb> factory, IDataProtectionProvider protection, StorageGate gate, TimeProvider clock) : IWorkspaceStore
{
    private readonly IDataProtector protector = protection.CreateProtector("CashHarbor.Workspace.v1");
    public async Task<Workspace> ReadAsync(Guid actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == actor, ct)) throw new UnauthorizedAccessException();
        var row = await db.Vaults.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Workspace.SharedId, ct);
        var workspace = row == null ? new() : JsonSerializer.Deserialize<Workspace>(protector.Unprotect(row.Payload)) ?? throw new InvalidDataException("Vault is empty.");
        return WorkspaceAccess.VisibleTo(workspace, actor);
    }
    public async Task UpdateAsync(Guid actor, string action, Action<Workspace> change, CancellationToken ct = default)
    {
        await gate.Lock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            if (!await db.Users.AnyAsync(u => u.Id == actor, ct)) throw new UnauthorizedAccessException();
            var row = await db.Vaults.SingleOrDefaultAsync(x => x.Id == Workspace.SharedId, ct);
            var workspace = row == null ? new Workspace() : JsonSerializer.Deserialize<Workspace>(protector.Unprotect(row.Payload))!;
            var visible = WorkspaceAccess.VisibleTo(workspace, actor);
            change(visible);
            WorkspaceAccess.Merge(workspace, visible, actor);
            if (row == null) { row = new(); db.Vaults.Add(row); }
            row.Payload = protector.Protect(JsonSerializer.Serialize(workspace)); row.Revision++;
            db.Audit.Add(new() { Actor = actor, Action = action, At = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new RuleException("Les données ont changé. Rechargez la page puis réessayez."); }
        finally { gate.Lock.Release(); }
    }
}
