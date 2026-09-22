using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;
using Xunit;

namespace SysSuite.Tests;

public class UninstallerStageTests
{
    [Fact]
    public void RegistryEnumerationFiltersRuntimeComponentsButKeepsApplications()
    {
        Assert.True(RegistryAppEnumerator.IsRuntimeComponent(
            "Microsoft Visual C++ 2022 Redistributable (x64)",
            "MsiExec.exe /I{test}", null));
        Assert.True(RegistryAppEnumerator.IsRuntimeComponent(
            "Microsoft .NET SDK 10.0.111 (x64)",
            "MsiExec.exe /I{test}", null));
        Assert.True(RegistryAppEnumerator.IsRuntimeComponent(
            "Microsoft SQL Server 2014 Setup (English)",
            "MsiExec.exe /I{test}", null));
        Assert.False(RegistryAppEnumerator.IsRuntimeComponent(
            "百度网盘",
            "uninstall.exe", null));
    }

    [Fact]
    public async Task AppChangeMonitorRaisesEventWhenListChanges()
    {
        var app = AppRecord.Create("monitor-test", "Monitor Test", AppSource.Registry);
        var firstList = Array.Empty<AppRecord>();
        var secondList = new[] { app };
        var service = new FakeEnumerationService([firstList, secondList]);
        using var monitor = new AppChangeMonitor(service, TimeSpan.FromMilliseconds(20));
        var changed = new TaskCompletionSource<IReadOnlyList<AppRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.AppListChanged += (_, apps) => changed.TrySetResult(apps);
        monitor.Start();

        var winner = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        monitor.StopMonitoring();
        Assert.True(winner == changed.Task, "监控器未在超时时间内报告列表变化。");
        Assert.Equal("monitor-test", (await changed.Task).Single().StableKey);
    }

    private sealed class FakeEnumerationService(IReadOnlyList<IReadOnlyList<AppRecord>> responses) : IUninstallEnumerationService
    {
        private int index;

        public Task<Result<IReadOnlyList<AppRecord>>> RefreshAsync(CancellationToken cancellationToken = default)
        {
            var current = index < responses.Count ? responses[index] : responses[^1];
            index++;
            return Task.FromResult(new Result<IReadOnlyList<AppRecord>>(ErrorType.None, string.Empty, current));
        }

        public Task<Result<IReadOnlyList<AppRecord>>> ListAsync(CancellationToken cancellationToken = default)
        {
            return RefreshAsync(cancellationToken);
        }
    }
}
