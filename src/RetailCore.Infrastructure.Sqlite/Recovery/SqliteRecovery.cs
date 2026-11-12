using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Foundation;
using RetailCore.Application.Recovery;
using RetailCore.Domain.Common;
using RetailCore.Domain.Security;
using RetailCore.Infrastructure.Sqlite.Foundation;
namespace RetailCore.Infrastructure.Sqlite.Recovery;

public interface IRecoveryFaults { void At(string stage); }
public sealed class NoRecoveryFaults : IRecoveryFaults { public void At(string stage) { } }

/// <summary>SQLite backup/restore, persistent maintenance journal and pre-migration safety copies.
/// Pending operations prevent trading until startup verifies completion or restores the safety copy.</summary>
public sealed class SqliteRecovery(LocalDatabase database, IServiceScopeFactory scopes, IClock clock, IRecoveryFaults faults) : IRecoveryStorage
{
    private sealed record Journal(string Kind, string Safety, Guid Id, string Digest, RecoveryActor? Actor, string? Reason);
    private string StateFile => Path.Combine(database.StateDirectory, "status.json");
    private string Pending => Path.Combine(database.StateDirectory, "pending.json");
    private BackupStatus LoadStatus() => File.Exists(StateFile) ? JsonSerializer.Deserialize<BackupStatus>(File.ReadAllText(StateFile))! :
        new(new(Path.GetDirectoryName(database.Path)! + "-backups", null));
    private static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<T> AuthorizedAsync<T>(Func<ILocalStore, CancellationToken, Task<T>> authorize, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var result = await authorize(new SqliteLocalTransactions.LocalStore(db), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false); return result;
    }
    public async Task<BackupStatus> StatusAsync(Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); database.RequireReady();
        await AuthorizedAsync(authorize, ct).ConfigureAwait(false); return LoadStatus();
    }
    public async Task<BackupStatus> ConfigureAsync(BackupSettings settings, Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); database.RequireReady();
        var actor = await AuthorizedAsync(authorize, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.PrimaryDirectory)) throw new FoundationException("InvalidBackupSettings");
        var primary = Path.GetFullPath(settings.PrimaryDirectory); var second = string.IsNullOrWhiteSpace(settings.SecondDirectory) ? null : Path.GetFullPath(settings.SecondDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        static bool Nested(string a, string b, StringComparison comparison) => a.Equals(b, comparison) || a.StartsWith(Path.TrimEndingDirectorySeparator(b) + Path.DirectorySeparatorChar, comparison);
        if (settings.RetainCount is < 2 or > 365 || Nested(primary, Path.GetDirectoryName(database.Path)!, comparison)
            || second is not null && (Nested(second, Path.GetDirectoryName(database.Path)!, comparison) || Nested(second, primary, comparison) || Nested(primary, second, comparison)))
            throw new FoundationException("InvalidBackupSettings");
        var status = LoadStatus() with { Settings = new(primary, second, settings.RetainCount), LastDailyDate = null };
        await AuditAsync(actor, "BackupSettingsRequested", Guid.NewGuid(), JsonSerializer.Serialize(status.Settings), ct).ConfigureAwait(false);
        Save(StateFile, status); return status;
    }
    public async Task<BackupStatus> BackupAsync(Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); database.RequireReady();
        var actor = await AuthorizedAsync(authorize, ct).ConfigureAwait(false); var result = await BackupWithinAsync("manual", null, ct).ConfigureAwait(false);
        await AuditAsync(actor, "Backup", Guid.NewGuid(), JsonSerializer.Serialize(new { result.State, result.LastFile }), ct).ConfigureAwait(false); return result;
    }
    // Called only by the desktop's trusted scheduler, including before login. Destinations are owner configured.
    public async Task<BackupStatus> DailyAsync(CancellationToken ct = default)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); database.RequireReady();
        if (!File.Exists(database.Path)) return LoadStatus();
        using var connection = Open(database.Path, true);
        using var command = connection.CreateCommand(); command.CommandText = "SELECT TimeZoneId FROM Shops WHERE IsActive=1";
        if (command.ExecuteScalar() is not string zone) return LoadStatus();
        var date = TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zone)).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (LoadStatus().LastDailyDate == date) return LoadStatus();
        return await BackupWithinAsync("daily", date, ct).ConfigureAwait(false);
    }
    public async Task PrepareUpgradeAsync(CancellationToken ct = default)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); database.RequireReady();
        if (!File.Exists(database.Path)) return;
        using (var connection = Open(database.Path, true)) using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM Shifts WHERE Status='Open'";
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0) throw new FoundationException("UpgradeOpenShift");
        }
        await SafetyAsync("upgrade",ct).ConfigureAwait(false);
    }
    private async Task<BackupStatus> BackupWithinAsync(string kind, string? date, CancellationToken ct)
    {
        var prior = LoadStatus();
        var status = prior with { LastAttempt = clock.UtcNow, State = "BackupFailed" }; Save(StateFile, status);
        var primarySaved = false;
        try
        {
            Directory.CreateDirectory(status.Settings.PrimaryDirectory);
            // Reuse only a known primary snapshot when retrying its failed second copy.
            // A reset after restore/configuration must never adopt an unrelated same-day file.
            var retry = date is not null && prior.State == "BackupSecondFailed" && prior.LastFile is {} previous
                && Path.GetDirectoryName(previous) == status.Settings.PrimaryDirectory
                && Path.GetFileName(previous).StartsWith($"retailcore-daily-{date}-",StringComparison.Ordinal) && File.Exists(previous);
            var name = retry ? Path.GetFileName(prior.LastFile!) : date is null ? $"retailcore-{kind}-{clock.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.db" : $"retailcore-daily-{date}-{Guid.NewGuid():N}.db";
            var primary = Path.Combine(status.Settings.PrimaryDirectory, name);
            if (!File.Exists(primary)) CreateSnapshot(database.Path, primary);
            await ValidateAsync(primary, ct).ConfigureAwait(false);
            primarySaved = true;
            status = status with { LastPrimarySuccess = clock.UtcNow, LastFile = primary }; Save(StateFile, status);
            if (status.Settings.SecondDirectory is { } second)
            {
                if (!Directory.Exists(second)) throw new FoundationException("BackupSecondUnavailable");
                var copy = Path.Combine(second, name); CopyVerified(primary, copy);
                status = status with { LastSecondSuccess = clock.UtcNow }; Prune(second, status.Settings.RetainCount);
            }
            status = status with { State = status.Settings.SecondDirectory is null ? "BackupPrimaryOnly" : "BackupSucceeded", LastDailyDate = date ?? status.LastDailyDate };
            Prune(status.Settings.PrimaryDirectory, status.Settings.RetainCount); Save(StateFile, status); return status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or FoundationException)
        {
            status = status with { State = primarySaved ? "BackupSecondFailed" : "BackupFailed" }; Save(StateFile, status); return status;
        }
    }
    private static void Prune(string directory, int count)
    {
        // Only this application's exact automatic/manual names. Safety and pre-upgrade copies are never aged out.
        var names = Directory.EnumerateFiles(directory, "retailcore-*.db").Where(p =>
            System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"^retailcore-(daily-\d{8}(?:-[a-f0-9]{32})?|manual-\d{8}T\d{9}-[a-f0-9]{32})\.db$", System.Text.RegularExpressions.RegexOptions.CultureInvariant));
        foreach (var path in names.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal).Skip(count)) File.Delete(path);
    }
    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false, ForeignKeys = true, DefaultTimeout = 2 }.ToString());
        try { connection.Open(); return connection; } catch { connection.Dispose(); throw; }
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void CopyVerified(string source, string target)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(source)) using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
            if (Hash(source) != Hash(temporary)) throw new IOException("Backup verification failed."); File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void BackupTo(string source, string target)
    {
        using var input = Open(source, true); using var output = Open(target, false);
        using (var command = output.CreateCommand())
        { command.CommandText = "PRAGMA synchronous=FULL; PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE; COMMIT;"; command.ExecuteNonQuery(); }
        input.BackupDatabase(output);
    }
    private static void CreateSnapshot(string source, string target)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            BackupTo(source, temporary);
            // Online backup can preserve WAL mode in the copied header. Archives must be standalone.
            using (var connection = Open(temporary, false)) using (var command = connection.CreateCommand())
            { command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;"; command.ExecuteNonQuery(); }
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static TillDbContext Context(string path, IServiceScope scope) => new(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString()).Options, scope.ServiceProvider.GetRequiredService<ITenantContext>());
    private async Task<string[]> ValidateAsync(string path, CancellationToken ct)
    {
        using var connection = Open(path, true);
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal)) throw new FoundationException("InvalidBackup");
        command.CommandText = "PRAGMA foreign_key_check;"; using (var reader = command.ExecuteReader()) { if (reader.Read()) throw new FoundationException("InvalidBackup"); }
        using var scope = scopes.CreateScope(); await using var context = Context(path, scope);
        var known = context.Database.GetMigrations().ToArray(); var applied = (await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToArray();
        if (applied.Length == 0 || applied.Length > known.Length || !known.Take(applied.Length).SequenceEqual(applied)) throw new FoundationException("BackupSchemaIncompatible");
        // Compare exact tables/indexes/triggers with a database built by the declared migrations.
        var reference = Path.Combine(database.StateDirectory, Guid.NewGuid().ToString("N") + ".schema.db"); Directory.CreateDirectory(database.StateDirectory);
        try
        {
            await using (var expected = Context(reference, scope)) { await expected.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(applied[^1], ct).ConfigureAwait(false); }
            if (Schema(path) != Schema(reference)) throw new FoundationException("BackupSchemaIncompatible");
        }
        finally { DeleteWork(reference); }
        return applied;
    }
    private static string Schema(string path)
    {
        using var connection = Open(path, true); using var command = connection.CreateCommand(); command.CommandText = "SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name";
        using var reader = command.ExecuteReader(); var rows = new List<string>(); while (reader.Read()) rows.Add(reader.GetString(0) + "|" + reader.GetString(1) + "|" + (reader.IsDBNull(2) ? "" : reader.GetString(2))); return string.Join('\n', rows);
    }
    private static void DeleteWork(string path) { foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    private async Task<(string Stage, RestoreReview Review)> StageAsync(string path, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        if (path.Equals(database.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || File.Exists(path + "-wal") || File.Exists(path + "-journal")) throw new FoundationException("InvalidBackup");
        Directory.CreateDirectory(database.StateDirectory); var stage = Path.Combine(database.StateDirectory, Guid.NewGuid().ToString("N") + ".stage.db");
        try
        {
            // The selected archive is closed/immutable. Never copy the live WAL database as a file.
            CopyVerified(path, stage); var digest = Hash(stage); await ValidateAsync(stage, ct).ConfigureAwait(false);
            using (var scope = scopes.CreateScope()) await using (var context = Context(stage, scope)) { await context.Database.MigrateAsync(ct).ConfigureAwait(false); }
            await ValidateAsync(stage, ct).ConfigureAwait(false);
            using var connection = Open(stage, true); using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name FROM Shops WHERE IsActive=1"; var shop = command.ExecuteScalar() as string ?? throw new FoundationException("InvalidBackup");
            int Count(string sql) { command.CommandText = sql; return checked(Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture)); }
            if (Count("SELECT COUNT(*) FROM Shops") != 1 || Count("SELECT COUNT(*) FROM Terminals WHERE IsActive=1") != 1 || Count("SELECT COUNT(*) FROM Users WHERE IsActive=1 AND Role='Owner'") == 0) throw new FoundationException("InvalidBackup");
            return (stage, new(path, digest, shop, Count("SELECT COUNT(*) FROM Sales WHERE CompletedAt IS NOT NULL"), Count("SELECT COUNT(*) FROM StockMovements"), Count("SELECT COUNT(*) FROM Shifts"), File.GetLastWriteTimeUtc(path)));
        }
        catch { DeleteWork(stage); throw; }
    }
    public async Task<RestoreReview> ReviewAsync(string path, Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); database.RequireReady(); await AuthorizedAsync(authorize, ct).ConfigureAwait(false);
        try { var candidate = await StageAsync(path, ct).ConfigureAwait(false); DeleteWork(candidate.Stage); return candidate.Review; }
        catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException) { throw new FoundationException("InvalidBackup"); }
    }
    public async Task RestoreAsync(RestoreRequest request, Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); database.RequireReady();
        var actor = await AuthorizedAsync(authorize, ct).ConfigureAwait(false);
        var candidate = await StageAsync(request.Path, ct).ConfigureAwait(false);
        try
        {
            if (candidate.Review.Sha256 != request.Sha256) throw new FoundationException("RestoreFileChanged");
            var safety = await SafetyAsync("restore",ct).ConfigureAwait(false);
            var journal = new Journal("restore", safety, request.Id, request.Sha256, actor, request.Reason); Save(Pending, journal); faults.At("RestorePrepared");
            BackupTo(candidate.Stage, database.Path); faults.At("RestoreCopied");
            await ValidateAsync(database.Path, ct).ConfigureAwait(false);
            await MarkRestoreAsync(journal, ct).ConfigureAwait(false); faults.At("RestoreCommitted");
            File.Delete(Pending); Save(StateFile, LoadStatus() with { State = "RestoreSucceeded", LastDailyDate = null, LastMaintenanceState = "RestoreSucceeded", LastMaintenanceAt = clock.UtcNow, LastSafetyFile = safety });
        }
        finally { DeleteWork(candidate.Stage); }
    }
    private async Task<string> SafetyAsync(string kind,CancellationToken ct)
    {
        var path=Safety(kind);CreateSnapshot(database.Path,path);await ValidateAsync(path,ct).ConfigureAwait(false);
        if(LoadStatus().Settings.SecondDirectory is {} second)
        {
            if(!Directory.Exists(second))throw new FoundationException("BackupSecondUnavailable");
            CopyVerified(path,Path.Combine(second,Path.GetFileName(path)));
        }
        return path;
    }
    private string Safety(string kind)
    {
        var directory = Path.Combine(LoadStatus().Settings.PrimaryDirectory, "safety"); Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"retailcore-{kind}-{clock.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.db");
    }
    private async Task MarkRestoreAsync(Journal journal, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var store = new SqliteLocalTransactions.LocalStore(db); var existing = await store.CommandAsync(journal.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            store.Audit(AuditEntry.Record(shop.Id, AuditAction.DatabaseRestored, "DatabaseRestore", journal.Id, clock.UtcNow, summary: journal.Reason,
                detailsJson: JsonSerializer.Serialize(new { journal.Actor, journal.Digest, Safety = journal.Safety })));
            store.CompleteCommand(journal.Id, "DatabaseRestore", journal.Digest, "true"); await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else if (existing.Kind != "DatabaseRestore" || existing.Fingerprint != journal.Digest) throw new FoundationException("OperationConflict");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
    private async Task AuditAsync(RecoveryActor actor, string entity, Guid id, string details, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        db.AuditEntries.Add(AuditEntry.Record(new ShopId(actor.ShopId), entity=="Backup"?AuditAction.BackupCreated:AuditAction.BackupSettingsRequested, entity, id, clock.UtcNow, new UserId(actor.UserId), new UserId(actor.OwnerId), detailsJson: details));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var lease = await database.EnterAsync(ct).ConfigureAwait(false); Directory.CreateDirectory(database.StateDirectory);
        if (File.Exists(Pending)) await RecoverAsync(ct).ConfigureAwait(false);
        using var scope = scopes.CreateScope(); await using var context = Context(database.Path, scope);
        var applied = (await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToArray(); var known = context.Database.GetMigrations().ToArray();
        if (applied.Length > known.Length || !known.Take(applied.Length).SequenceEqual(applied)) throw new FoundationException("BackupSchemaIncompatible");
        if (applied.Length == 0 && File.Exists(database.Path))
        {
            using var connection = Open(database.Path, true); using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('__EFMigrationsHistory','__EFMigrationsLock')";
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0) throw new FoundationException("BackupSchemaIncompatible");
        }
        if (applied.Length == known.Length) return;
        Journal? journal = null;
        if (applied.Length > 0)
        {
            using (var connection = Open(database.Path, true)) using (var command = connection.CreateCommand())
            { command.CommandText = "SELECT COUNT(*) FROM Shifts WHERE Status='Open'"; if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0) throw new FoundationException("UpgradeOpenShift"); }
            var safety = await SafetyAsync("upgrade",ct).ConfigureAwait(false);
            journal = new("upgrade", safety, Guid.NewGuid(), "", null, null); Save(Pending, journal); faults.At("UpgradePrepared");
        }
        try
        {
            await context.Database.MigrateAsync(ct).ConfigureAwait(false); faults.At("UpgradeMigrated");
            if (journal is not null) { await ValidateAsync(database.Path, ct).ConfigureAwait(false); File.Delete(Pending); Save(StateFile, LoadStatus() with { State = "UpgradeSucceeded", LastDailyDate = null, LastMaintenanceState = "UpgradeSucceeded", LastMaintenanceAt = clock.UtcNow, LastSafetyFile = journal.Safety }); }
        }
        catch
        {
            if (journal is not null) { BackupTo(journal.Safety, database.Path); File.Delete(Pending); Save(StateFile, LoadStatus() with { State = "UpgradeFailed", LastDailyDate = null, LastMaintenanceState = "UpgradeFailed", LastMaintenanceAt = clock.UtcNow, LastSafetyFile = journal.Safety }); }
            throw;
        }
    }
    private async Task RecoverAsync(CancellationToken ct)
    {
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(Pending)) ?? throw new FoundationException("RecoveryPending");
        var committed = false;
        if (journal.Kind == "restore")
        {
            try { committed = await AuthorizedAsync(async (store, token) => (await store.CommandAsync(journal.Id, token).ConfigureAwait(false)) is { Kind: "DatabaseRestore" } prior && prior.Fingerprint == journal.Digest, ct).ConfigureAwait(false); }
            catch (SqliteException) { committed = false; }
        }
        if (!committed) { await ValidateAsync(journal.Safety, ct).ConfigureAwait(false); BackupTo(journal.Safety, database.Path); }
        await ValidateAsync(database.Path, ct).ConfigureAwait(false); Save(StateFile, LoadStatus() with { State = committed ? "RestoreSucceeded" : "RecoveryRolledBack", LastDailyDate = null, LastMaintenanceState = committed ? "RestoreSucceeded" : "RecoveryRolledBack", LastMaintenanceAt = clock.UtcNow, LastSafetyFile = journal.Safety }); File.Delete(Pending);
    }
}
