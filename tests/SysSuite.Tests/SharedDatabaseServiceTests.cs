using System.Text.Json;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;
using Xunit;

namespace SysSuite.Tests;

public class SharedDatabaseServiceTests
{
    [Fact]
    public async Task CreatesSchemaPersistsAndReopensData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}", "syssuite.db");
        string[] cleanPaths = ["C:\\Test\\old.tmp"];
        try
        {
            const string stableKey = "REG|SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\test|C:\\Test";
            var app = AppRecord.Create(
                stableKey,
                "Test Application",
                AppSource.Registry,
                "Test Publisher",
                "1.0.0",
                "20260912",
                2048,
                "uninstall.exe",
                "uninstall.exe /quiet",
                "HKEY_LOCAL_MACHINE\\SOFTWARE\\Test",
                "C:\\Test",
                "test.png",
                "hash");

            var service = new SharedDatabaseService(path);
            await service.ReplaceAppsAsync([app]);
            var apps = await service.ListAppsAsync();
            var storedApp = Assert.Single(apps);
            Assert.Equal("Test Application", storedApp.Name);
            Assert.Equal(AppSource.Registry, storedApp.Source);

            var updated = app with { Version = "1.0.1", UpdatedAt = DateTimeOffset.UtcNow };
            await service.ReplaceAppsAsync([updated]);
            var reopenedService = new SharedDatabaseService(path);
            apps = await reopenedService.ListAppsAsync();
            storedApp = Assert.Single(apps);
            Assert.Equal("1.0.1", storedApp.Version);
            Assert.Equal(stableKey, storedApp.StableKey);

            await reopenedService.ReplaceAppsAsync([app with { StableKey = "REG|REPLACED", Name = "Replacement" }]);
            apps = await reopenedService.ListAppsAsync();
            storedApp = Assert.Single(apps);
            Assert.Equal("Replacement", storedApp.Name);

            var cleanHistory = new CleanHistoryRecord(
                0,
                "batch-1",
                JsonSerializer.Serialize(cleanPaths),
                12,
                "temp",
                DateTimeOffset.UtcNow,
                true,
                "backup-root",
                CleanRestoreState.Ready,
                DateTimeOffset.UtcNow.AddDays(7));
            await reopenedService.SaveCleanHistoryAsync(cleanHistory);
            var histories = await reopenedService.ListCleanHistoryAsync();
            var storedHistory = Assert.Single(histories);
            Assert.Equal("batch-1", storedHistory.BatchId);
            Assert.Equal(12, storedHistory.FreedBytes);
            Assert.Equal(CleanRestoreState.Ready, storedHistory.RestoreState);

            await reopenedService.SaveUpdateHistoryAsync(new UpdateHistoryRecord(
                0,
                storedApp.Id,
                stableKey,
                "1.0.0",
                "1.0.1",
                AppHistoryAction.Upgrade,
                "Succeeded",
                DateTimeOffset.UtcNow));
            await reopenedService.SaveSecurityAuditAsync(new SecurityAuditRecord(
                0,
                "DefenderControl",
                "Enabled",
                "TemporarilyDisabled",
                DateTimeOffset.UtcNow.AddMinutes(5),
                DateTimeOffset.UtcNow,
                "UI",
                Environment.ProcessId,
                "Succeeded",
                "Pending"));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)!))
            {
                Directory.Delete(Path.GetDirectoryName(path)!, true);
            }
        }
    }

    [Fact]
    public async Task RejectsFutureSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}", "syssuite.db");
        try
        {
            var service = new SharedDatabaseService(path);
            await service.ReplaceAppsAsync([]);
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();

            var futureService = new SharedDatabaseService(path);
            await Assert.ThrowsAsync<InvalidOperationException>(() => futureService.ListAppsAsync());
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)!))
            {
                Directory.Delete(Path.GetDirectoryName(path)!, true);
            }
        }
    }
}
