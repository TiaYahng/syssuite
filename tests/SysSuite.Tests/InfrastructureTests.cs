using System.IO;
using SysSuite.Core;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Diagnostics;
using SysSuite.Core.Settings;
using SysSuite.Core.Abstractions.Settings;
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
        await using var queue = new TaskCoordinator();
        var counter = 0;
        var maximum = 0;
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
        await Task.Delay(80);
        Assert.Equal(1, maximum);
    }
}
