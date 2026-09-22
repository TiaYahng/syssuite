using Microsoft.Data.Sqlite;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class SharedDatabaseService : ISharedDatabaseService, IDisposable
{
    public const int CurrentSchemaVersion = 1;
    private readonly string databasePath;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public SharedDatabaseService(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = Path.GetFullPath(databasePath);
    }

    public async Task<IReadOnlyList<AppRecord>> ListAppsAsync(CancellationToken cancellationToken = default)
    {
        return await ReadAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, stable_key, name, publisher, version, install_date, size, uninstall_string,
                       quiet_string, source, key_path, install_dir, icon_path, hash, updated_at
                FROM apps
                ORDER BY name COLLATE NOCASE, stable_key
                """;
            var apps = new List<AppRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                apps.Add(new AppRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    ParseEnum<AppSource>(reader.GetInt32(9)),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetDateTimeOffset(14)));
            }

            return (IReadOnlyList<AppRecord>)apps;
        }, cancellationToken);
    }

    public async Task ReplaceAppsAsync(IReadOnlyCollection<AppRecord> apps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apps);
        await WriteAsync(async connection =>
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM apps";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO apps (stable_key, name, publisher, version, install_date, size, uninstall_string,
                                  quiet_string, source, key_path, install_dir, icon_path, hash, updated_at)
                VALUES (@stable_key, @name, @publisher, @version, @install_date, @size, @uninstall_string,
                        @quiet_string, @source, @key_path, @install_dir, @icon_path, @hash, @updated_at)
                ON CONFLICT(stable_key) DO UPDATE SET
                    name = excluded.name,
                    publisher = excluded.publisher,
                    version = excluded.version,
                    install_date = excluded.install_date,
                    size = excluded.size,
                    uninstall_string = excluded.uninstall_string,
                    quiet_string = excluded.quiet_string,
                    source = excluded.source,
                    key_path = excluded.key_path,
                    install_dir = excluded.install_dir,
                    icon_path = excluded.icon_path,
                    hash = excluded.hash,
                    updated_at = excluded.updated_at
                """;
            AddParameter(command, "@stable_key", null);
            AddParameter(command, "@name", null);
            AddParameter(command, "@publisher", null);
            AddParameter(command, "@version", null);
            AddParameter(command, "@install_date", null);
            AddParameter(command, "@size", null);
            AddParameter(command, "@uninstall_string", null);
            AddParameter(command, "@quiet_string", null);
            AddParameter(command, "@source", null);
            AddParameter(command, "@key_path", null);
            AddParameter(command, "@install_dir", null);
            AddParameter(command, "@icon_path", null);
            AddParameter(command, "@hash", null);
            AddParameter(command, "@updated_at", null);

            foreach (var app in apps)
            {
                command.Parameters["@stable_key"].Value = app.StableKey;
                command.Parameters["@name"].Value = app.Name;
                command.Parameters["@publisher"].Value = (object?)app.Publisher ?? DBNull.Value;
                command.Parameters["@version"].Value = (object?)app.Version ?? DBNull.Value;
                command.Parameters["@install_date"].Value = (object?)app.InstallDate ?? DBNull.Value;
                command.Parameters["@size"].Value = (object?)app.Size ?? DBNull.Value;
                command.Parameters["@uninstall_string"].Value = (object?)app.UninstallString ?? DBNull.Value;
                command.Parameters["@quiet_string"].Value = (object?)app.QuietString ?? DBNull.Value;
                command.Parameters["@source"].Value = (int)app.Source;
                command.Parameters["@key_path"].Value = (object?)app.KeyPath ?? DBNull.Value;
                command.Parameters["@install_dir"].Value = (object?)app.InstallDir ?? DBNull.Value;
                command.Parameters["@icon_path"].Value = (object?)app.IconPath ?? DBNull.Value;
                command.Parameters["@hash"].Value = (object?)app.Hash ?? DBNull.Value;
                command.Parameters["@updated_at"].Value = app.UpdatedAt;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task SaveCleanHistoryAsync(CleanHistoryRecord record, CancellationToken cancellationToken = default)
    {
        await WriteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO clean_history (batch_id, items_json, freed_bytes, mode, at, reversible,
                                           backup_root, restore_state, expires_at)
                VALUES (@batch_id, @items_json, @freed_bytes, @mode, @at, @reversible,
                        @backup_root, @restore_state, @expires_at)
                """;
            AddParameter(command, "@batch_id", record.BatchId);
            AddParameter(command, "@items_json", record.ItemsJson);
            AddParameter(command, "@freed_bytes", record.FreedBytes);
            AddParameter(command, "@mode", record.Mode);
            AddParameter(command, "@at", record.At);
            AddParameter(command, "@reversible", record.Reversible);
            AddParameter(command, "@backup_root", record.BackupRoot);
            AddParameter(command, "@restore_state", (int)record.RestoreState);
            AddParameter(command, "@expires_at", record.ExpiresAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CleanHistoryRecord>> ListCleanHistoryAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        return await ReadAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, batch_id, items_json, freed_bytes, mode, at, reversible, backup_root,
                       restore_state, expires_at
                FROM clean_history
                ORDER BY at DESC, id DESC
                LIMIT @limit
                """;
            AddParameter(command, "@limit", limit);
            var records = new List<CleanHistoryRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(new CleanHistoryRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetDateTimeOffset(5),
                    reader.GetBoolean(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    ParseEnum<CleanRestoreState>(reader.GetInt32(8)),
                    reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9)));
            }

            return (IReadOnlyList<CleanHistoryRecord>)records;
        }, cancellationToken);
    }

    public async Task UpdateCleanHistoryStateAsync(long id, CleanRestoreState state, CancellationToken cancellationToken = default)
    {
        await WriteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE clean_history SET restore_state = @restore_state WHERE id = @id";
            AddParameter(command, "@restore_state", (int)state);
            AddParameter(command, "@id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task SaveUpdateHistoryAsync(UpdateHistoryRecord record, CancellationToken cancellationToken = default)
    {
        await WriteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO update_history (app_stable_key, app_id, from_ver, to_ver, action, result, at)
                VALUES (@app_stable_key, @app_id, @from_ver, @to_ver, @action, @result, @at)
                """;
            AddParameter(command, "@app_stable_key", record.AppStableKey);
            AddParameter(command, "@app_id", record.AppId);
            AddParameter(command, "@from_ver", record.FromVersion);
            AddParameter(command, "@to_ver", record.ToVersion);
            AddParameter(command, "@action", (int)record.Action);
            AddParameter(command, "@result", record.Result);
            AddParameter(command, "@at", record.At);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task SaveSecurityAuditAsync(SecurityAuditRecord record, CancellationToken cancellationToken = default)
    {
        await WriteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO security_audit (action, old_state, new_state, auto_restore_at, at,
                                            requested_by, process_id, command_result, restore_result)
                VALUES (@action, @old_state, @new_state, @auto_restore_at, @at,
                        @requested_by, @process_id, @command_result, @restore_result)
                """;
            AddParameter(command, "@action", record.Action);
            AddParameter(command, "@old_state", record.OldState);
            AddParameter(command, "@new_state", record.NewState);
            AddParameter(command, "@auto_restore_at", record.AutoRestoreAt);
            AddParameter(command, "@at", record.At);
            AddParameter(command, "@requested_by", record.RequestedBy);
            AddParameter(command, "@process_id", record.ProcessId);
            AddParameter(command, "@command_result", record.CommandResult);
            AddParameter(command, "@restore_result", record.RestoreResult);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task<T> ReadAsync<T>(Func<SqliteConnection, Task<T>> work, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await work(connection);
    }

    private async Task WriteAsync(Func<SqliteConnection, Task<bool>> work, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        if (!await work(connection))
        {
            throw new InvalidOperationException("The database operation completed without success.");
        }
    }

    public void Dispose()
    {
        initializationLock.Dispose();
    }
}
