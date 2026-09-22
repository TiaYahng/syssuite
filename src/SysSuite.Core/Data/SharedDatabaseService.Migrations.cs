using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SysSuite.Core.Data;

public sealed partial class SharedDatabaseService
{
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
                await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
            }

            var version = 0L;
            await using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.CommandText = "PRAGMA user_version;";
                version = Convert.ToInt64(await versionCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException($"SQLite schema version {version} is newer than supported version {CurrentSchemaVersion}.");
            }

            if (version < 1)
            {
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                await ApplyMigration1Async(connection, cancellationToken);
                await using var setVersion = connection.CreateCommand();
                setVersion.Transaction = transaction;
                setVersion.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
                await setVersion.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static async Task ApplyMigration1Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE apps (
                id INTEGER PRIMARY KEY,
                stable_key TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                publisher TEXT,
                version TEXT,
                install_date TEXT,
                size INTEGER,
                uninstall_string TEXT,
                quiet_string TEXT,
                source INTEGER NOT NULL,
                key_path TEXT,
                install_dir TEXT,
                icon_path TEXT,
                hash TEXT,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX idx_apps_name ON apps(name COLLATE NOCASE);
            CREATE INDEX idx_apps_source_key ON apps(source, key_path);

            CREATE TABLE clean_history (
                id INTEGER PRIMARY KEY,
                batch_id TEXT NOT NULL UNIQUE,
                items_json TEXT NOT NULL,
                freed_bytes INTEGER NOT NULL,
                mode TEXT NOT NULL,
                at TEXT NOT NULL,
                reversible INTEGER NOT NULL CHECK (reversible IN (0, 1)),
                backup_root TEXT,
                restore_state INTEGER NOT NULL CHECK (restore_state BETWEEN 0 AND 4),
                expires_at TEXT
            );
            CREATE INDEX idx_clean_history_at ON clean_history(at DESC);
            CREATE INDEX idx_clean_history_restore ON clean_history(restore_state);

            CREATE TABLE update_history (
                id INTEGER PRIMARY KEY,
                app_stable_key TEXT NOT NULL,
                app_id INTEGER NOT NULL,
                from_ver TEXT,
                to_ver TEXT,
                action INTEGER NOT NULL CHECK (action BETWEEN 0 AND 2),
                result TEXT NOT NULL,
                at TEXT NOT NULL,
                FOREIGN KEY (app_id) REFERENCES apps(id) ON DELETE CASCADE
            );
            CREATE INDEX idx_update_history_app ON update_history(app_stable_key, at DESC);

            CREATE TABLE security_audit (
                id INTEGER PRIMARY KEY,
                action TEXT NOT NULL,
                old_state TEXT NOT NULL,
                new_state TEXT NOT NULL,
                auto_restore_at TEXT,
                at TEXT NOT NULL,
                requested_by TEXT NOT NULL,
                process_id INTEGER NOT NULL,
                command_result TEXT NOT NULL,
                restore_result TEXT NOT NULL
            );
            CREATE INDEX idx_security_audit_at ON security_audit(at DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate;Pooling=False");
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static TEnum ParseEnum<TEnum>(int value) where TEnum : struct, Enum
    {
        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }
}
