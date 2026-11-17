using System.Text.Json;
using CashHarbor.Application;
using CashHarbor.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CashHarbor.Infra;

public sealed record AccessAssignment(Guid Id, bool IsGoal, AccountVisibility Visibility, Guid? OwnerId);
public sealed record LegacyItem(Guid Id, string Name, bool IsGoal);
public sealed record AccessMigrationPlan(IReadOnlyList<LegacyItem> Items, IReadOnlyList<UserSession> Users);

// Administrative recovery surface, exposed only through the offline terminal command.
public sealed class LegacyAccessMigrator(IDbContextFactory<VaultDb> factory, IDataProtectionProvider protection, StorageGate gate, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("CashHarbor.Workspace.v1");
    public async Task<AccessMigrationPlan> PlanAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Vaults.AsNoTracking().SingleOrDefaultAsync();
        var w = row == null ? new Workspace() : JsonSerializer.Deserialize<Workspace>(protector.Unprotect(row.Payload))!;
        var items = w.Accounts.Where(a => a.Visibility == AccountVisibility.Unassigned).Select(a => new LegacyItem(a.Id, a.Name, false))
            .Concat(w.Goals.Where(g => g.Visibility == AccountVisibility.Unassigned).Select(g => new LegacyItem(g.Id, g.Name, true))).ToArray();
        var users = await db.Users.OrderBy(u => u.Email).Select(u => new UserSession(u.Id, u.Email, u.Version)).ToListAsync();
        return new(items, users);
    }
    public async Task<string?> ApplyAsync(IReadOnlyList<AccessAssignment> assignments)
    {
        await gate.Lock.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var row = await db.Vaults.SingleOrDefaultAsync(); if (row == null) return null;
            var w = JsonSerializer.Deserialize<Workspace>(protector.Unprotect(row.Payload))!;
            var pending = w.Accounts.Where(a => a.Visibility == AccountVisibility.Unassigned).Select(a => (a.Id, IsGoal: false))
                .Concat(w.Goals.Where(g => g.Visibility == AccountVisibility.Unassigned).Select(g => (g.Id, IsGoal: true))).ToHashSet();
            if (pending.Count == 0) return null;
            var submitted = assignments.Select(a => (a.Id, a.IsGoal)).ToHashSet();
            if (assignments.Count != pending.Count || !pending.SetEquals(submitted)) throw new RuleException("Classez tous les éléments existants une seule fois. Aucun changement enregistré.");
            var users = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
            foreach (var a in assignments)
            {
                if (!(a.Visibility == AccountVisibility.Joint && a.OwnerId == null || a.Visibility == AccountVisibility.Personal && a.OwnerId != null && users.Contains(a.OwnerId.Value)))
                    throw new RuleException("Titulaire ou visibilité invalide. Aucun changement enregistré.");
                if (a.IsGoal)
                {
                    var i = w.Goals.FindIndex(g => g.Id == a.Id); w.Goals[i] = w.Goals[i] with { Visibility = a.Visibility, OwnerId = a.OwnerId };
                }
                else
                {
                    var i = w.Accounts.FindIndex(x => x.Id == a.Id); w.Accounts[i] = w.Accounts[i] with { Visibility = a.Visibility, OwnerId = a.OwnerId };
                }
            }
            var connection = (SqliteConnection)db.Database.GetDbConnection(); await connection.OpenAsync();
            var backupPath = await DatabaseSchema.CreateBackupAsync(connection, clock, "before-access-classification");
            row.Payload = protector.Protect(JsonSerializer.Serialize(w)); row.Revision++;
            db.Audit.Add(new() { At = clock.GetUtcNow(), Action = "access.legacy-classified" });
            await db.SaveChangesAsync(); return backupPath;
        }
        finally { gate.Lock.Release(); }
    }
}
