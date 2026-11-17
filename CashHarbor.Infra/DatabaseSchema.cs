using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CashHarbor.Infra;

public static class DatabaseSchema
{
    public const int CurrentVersion = 2;
    public static async Task InitializeAsync(VaultDb db, TimeProvider clock)
    {
        await db.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        using var read = connection.CreateCommand(); read.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(await read.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        if (version is not (0 or 1 or CurrentVersion)) throw new InvalidOperationException("Unsupported database schema. Restore the matching application.");
        await db.Database.EnsureCreatedAsync();
        if (version == 1) await CreateBackupAsync(connection, clock, "before-privacy-v2");
        // v2 changes access semantics, not SQL tables. Block old v1 images from reopening private data.
        using var set = connection.CreateCommand(); set.CommandText = $"PRAGMA user_version = {CurrentVersion}"; await set.ExecuteNonQueryAsync();
    }
    public static async Task<string> CreateBackupAsync(SqliteConnection connection, TimeProvider clock, string reason)
    {
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(connection.DataSource))!, "backups"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{reason}-{clock.GetUtcNow():yyyyMMddTHHmmss}-{Guid.NewGuid():N}.db");
        await using var backup = new SqliteConnection($"Data Source={path};Pooling=False"); await backup.OpenAsync(); connection.BackupDatabase(backup);
        return path;
    }
}
