using System.IO;
using SysSuite.Core;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Diagnostics;
using SysSuite.Core.Settings;
using SysSuite.Core.Abstractions.Settings;
using SysSuite.Core.System;
using Xunit;

namespace SysSuite.Tests;

public class InfrastructureTests
{
    [Fact]
    public async Task SettingsServicePersistsAndRejectsExperimentalDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            var service = new SettingsService(directory);
            Assert.False(service.Current.EnableDefenderControl);
            service.Current.Language = "en";
            var saveResult = await service.SaveAsync();
            Assert.True(saveResult.IsSuccess, saveResult.Message);
            Assert.Equal("en", new SettingsService(directory).Current.Language);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void DiagnosticsServiceExportsLogArchive()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            var service = new DiagnosticsService(directory);
            File.WriteAllText(Path.Combine(service.LogDirectory, "test.log"), "ok");
            Assert.True(service.ExportLogs().IsSuccess);
            Assert.Single(Directory.GetFiles(directory, "*.zip"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task TaskQueueSerializesExclusiveResources()
    {
        var maximum = 0;
        {
            await using var queue = new TaskCoordinator();
            var counter = 0;
            var first = queue.EnqueueAsync(new TaskMeta("one", "One", false, TaskResource.DiskDrive), async cancellationToken =>
            {
                maximum = Math.Max(maximum, Interlocked.Increment(ref counter));
                await Task.Delay(20, cancellationToken);
                Interlocked.Decrement(ref counter);
            });
            var second = queue.EnqueueAsync(new TaskMeta("two", "Two", false, TaskResource.DiskDrive), async cancellationToken =>
            {
                maximum = Math.Max(maximum, Interlocked.Increment(ref counter));
                await Task.Delay(20, cancellationToken);
                Interlocked.Decrement(ref counter);
            });
            await Task.WhenAll(first, second);
        }
        Assert.Equal(1, maximum);
    }

    [Fact]
    public async Task TempCleanerServiceScansAndDeletesOnlySafeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var oldPath = Path.Combine(root, "old.tmp");
            var newPath = Path.Combine(root, "new.tmp");
            await File.WriteAllTextAsync(oldPath, "old");
            await File.WriteAllTextAsync(newPath, "new");
            File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-30));

            var service = new TempCleanerService(root, Path.Combine(root, "missing"));
            var scanResult = await service.ScanAsync();
            Assert.True(scanResult.IsSuccess, scanResult.Message);
            var item = Assert.Single(scanResult.Value!);
            Assert.Equal(oldPath, item.Path);
            Assert.Equal(3, item.SizeBytes);
            Assert.Equal(SysSuite.Core.Abstractions.System.CleanRisk.Safe, item.Risk);

            var cleanResult = await service.CleanAsync([item]);
            Assert.True(cleanResult.IsSuccess, cleanResult.Message);
            Assert.Equal(1, cleanResult.Value!.DeletedCount);
            Assert.Equal(3, cleanResult.Value.FreedBytes);
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task TempCleanerServiceRejectsPathsOutsideSafeRoots()
    {
        var safeRoot = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        var outsideRoot = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(safeRoot);
            Directory.CreateDirectory(outsideRoot);
            var outsidePath = Path.Combine(outsideRoot, "outside.tmp");
            await File.WriteAllTextAsync(outsidePath, "outside");
            var service = new TempCleanerService(safeRoot, Path.Combine(safeRoot, "missing"));

            var result = await service.CleanAsync([new SysSuite.Core.Abstractions.System.CleanItem(
                outsidePath,
                "外部路径",
                7,
                SysSuite.Core.Abstractions.System.CleanRisk.Caution,
                "越界路径")]);

            Assert.False(result.IsSuccess);
            Assert.Equal(SysSuite.Core.Abstractions.ErrorType.InvalidInput, result.Error);
            Assert.True(File.Exists(outsidePath));
        }
        finally
        {
            Directory.Delete(safeRoot, true);
            Directory.Delete(outsideRoot, true);
        }
    }

    [Fact]
    public async Task DuplicateFileServiceKeepsOldestDuplicate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var oldest = Path.Combine(root, "old.dat");
            var newest = Path.Combine(root, "new.dat");
            var payload = new byte[1024 * 1024];
            await File.WriteAllBytesAsync(oldest, payload);
            await File.WriteAllBytesAsync(newest, payload);
            File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddDays(-1));

            var service = new DuplicateFileService(root, Path.Combine(root, "missing"));
            var result = await service.ScanAsync();
            Assert.True(result.IsSuccess, result.Message);
            var group = Assert.Single(result.Value!);
            Assert.Equal(oldest, group.KeepPath);
            Assert.Equal(2, group.Files.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
