using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;

namespace SysSuite.Tests;

/// <summary>
/// M4 T4.1 快照与还原部分（与 <see cref="UpdateControlServiceTests"/> 拆开守住 300 行门禁）。
/// 这里全部用假探针：真实写入需要管理员权限且会改本机更新配置，不能进单测。
/// </summary>
public class UpdateControlSnapshotTests
{
    [Fact]
    public void VerifyReportsNothingWhenServiceLayerMatchesTargetMode()
    {
        var registry = new FakeRegistryProbe { PolicyNoAutoUpdate = "1", PolicyAuOptions = "2" };
        var service = new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Manual", ["UsoSvc"] = "Manual" },
        };

        var drifts = RunVerify(registry, service, UpdateMode.NotifyOnly);

        Assert.Empty(drifts);
    }

    [Fact]
    public void VerifyReportsDriftWhenServiceWasRolledBack()
    {
        // WaaSMedic 把服务改回 Auto 的形态
        var registry = new FakeRegistryProbe { PolicyNoAutoUpdate = "1", PolicyAuOptions = "2" };
        var service = new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Auto", ["UsoSvc"] = "Manual" },
        };

        var drifts = RunVerify(registry, service, UpdateMode.NotifyOnly);

        Assert.Single(drifts);
        Assert.Equal("service.wuauserv", drifts[0].Id);
        Assert.Equal("Manual", drifts[0].Expected);
    }

    [Fact]
    public void VerifyIgnoresMissingServiceInsteadOfReportingDrift()
    {
        // 精简版系统上没有 UsoSvc：读不到不该变成"被回滚"
        var registry = new FakeRegistryProbe { PolicyNoAutoUpdate = "1", PolicyAuOptions = "2" };
        var service = new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Manual" },
        };

        var drifts = RunVerify(registry, service, UpdateMode.NotifyOnly);

        Assert.Empty(drifts);
    }

    [Fact]
    public void VerifyCatchesPolicyValueRollback()
    {
        // 策略键被系统/域管改回自动更新：NoAutoUpdate 变成 0
        var registry = new FakeRegistryProbe { PolicyNoAutoUpdate = "0", PolicyAuOptions = "4" };
        var service = new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Manual", ["UsoSvc"] = "Manual" },
        };

        var drifts = RunVerify(registry, service, UpdateMode.NotifyOnly);

        Assert.Equal(2, drifts.Count);
        Assert.All(drifts, item => Assert.StartsWith("policy.", item.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void VerifySkipsPolicyCheckOnAutomaticBecauseAbsenceIsUnverifiable()
    {
        // Automatic 档靠"删除键值"表达，无法与"读不到"区分，所以只校验服务层
        var registry = new FakeRegistryProbe();
        var service = new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Manual", ["UsoSvc"] = "Manual" },
        };

        var drifts = RunVerify(registry, service, UpdateMode.Automatic);

        Assert.Empty(drifts);
    }

    [Fact]
    public async Task VerifyIsSilentOnNeverTouchedMachine()
    {
        // 本程序从未改过这台机器时，系统默认值就是正常状态，不该被当成"被回滚"
        var path = Path.Combine(Path.GetTempPath(), "syssuite-tests", $"none-{Guid.NewGuid():N}.json");
        var service = CreateService(new FakeRegistryProbe(), new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Auto", ["UsoSvc"] = "Auto" },
        }, path);

        var result = await service.VerifyAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task VerifyReportsDriftOnlyAfterTargetModeWasApplied()
    {
        var path = Path.Combine(Path.GetTempPath(), "syssuite-tests", $"applied-{Guid.NewGuid():N}.json");
        var registry = new FakeRegistryProbe { PolicyNoAutoUpdate = "1", PolicyAuOptions = "2" };
        var probe = new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Manual", ["UsoSvc"] = "Manual" },
        };

        try
        {
            var service = CreateService(registry, probe, path);

            // 应用前：无快照 → 静默
            Assert.Empty((await service.VerifyAsync()).Value!);

            await service.CaptureSnapshotAsync();
            var instance = (WindowsUpdateControlService)service;
            MarkTarget(instance, UpdateMode.NotifyOnly);

            // 应用后：目标档位已记录，此时服务被改回才算漂移
            Assert.Empty((await service.VerifyAsync()).Value!);

            probe.StartTypes["wuauserv"] = "Auto";
            var drifts = (await service.VerifyAsync()).Value!;
            Assert.Single(drifts);
            Assert.Equal("service.wuauserv", drifts[0].Id);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>模拟 ApplyMode 的"回写目标档位"这一步（真实路径需要提权，不可进单测）。</summary>
    private static void MarkTarget(WindowsUpdateControlService service, UpdateMode mode)
        => typeof(WindowsUpdateControlService)
            .GetMethod("MarkTargetMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(service, [mode]);

    [Fact]
    public async Task CaptureSnapshotPersistsAllThreeKindsOfEntries()
    {
        // 漏记任一类都会得到"看着还原了、实际没生效"的结果，所以三类都要落盘
        var registry = new FakeRegistryProbe { PolicyNoAutoUpdate = "1", PolicyAuOptions = "2" };
        var service = new FakeServiceProbe
        {
            StartTypes = { ["wuauserv"] = "Disabled", ["UsoSvc"] = "Disabled" },
            TaskStates = { [WindowsUpdateControlService.UpdateTaskPaths[0]] = "Disabled" },
        };

        var path = Path.Combine(Path.GetTempPath(), "syssuite-tests", $"snap-{Guid.NewGuid():N}.json");
        try
        {
            var result = await CreateService(registry, service, path).CaptureSnapshotAsync();

            Assert.True(result.IsSuccess);

            // 反序列化回来断言，而不是匹配 JSON 文本 —— Kind 是枚举，默认序列化成数字，
            // 匹配字面量会把"序列化格式"这个无关细节焊进测试
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<UpdateControlSnapshot>(
                await File.ReadAllTextAsync(path));

            Assert.NotNull(snapshot);
            var kinds = snapshot.Entries.Select(entry => entry.Kind).Distinct().ToList();
            Assert.Contains(UpdateSnapshotKind.RegistryValue, kinds);
            Assert.Contains(UpdateSnapshotKind.ServiceStartType, kinds);
            Assert.Contains(UpdateSnapshotKind.ScheduledTaskState, kinds);
            Assert.Contains(snapshot.Entries, entry => entry.Target == "policy|NoAutoUpdate");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RestoreFailsWhenSnapshotIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "syssuite-tests", $"absent-{Guid.NewGuid():N}.json");
        var service = CreateService(new FakeRegistryProbe(), new FakeServiceProbe(), path);

        var result = await service.RestoreAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error);
    }

    [Fact]
    public async Task SetModeRejectsUnknownMode()
    {
        var service = CreateService(new FakeRegistryProbe(), new FakeServiceProbe(), TempSnapshotPath());

        var result = await service.SetModeAsync(UpdateMode.Unknown, includeServiceLayer: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.InvalidInput, result.Error);
    }

    [Fact]
    public async Task SetModeRefusesWithoutElevation()
    {
        // 非提权时必须直接拒，而不是"试一下然后失败"—— 部分写入比不写更糟
        var service = CreateService(new FakeRegistryProbe(), new FakeServiceProbe(), TempSnapshotPath());
        if (service.IsElevated)
        {
            return; // 提权环境下这条断言不适用
        }

        var result = await service.SetModeAsync(UpdateMode.Disabled, includeServiceLayer: false);

        Assert.Equal(ErrorType.AccessDenied, result.Error);
    }

    private static string TempSnapshotPath()
        => Path.Combine(Path.GetTempPath(), "syssuite-tests", $"snap-{Guid.NewGuid():N}.json");

    private static IReadOnlyList<UpdateControlItem> RunVerify(
        FakeRegistryProbe registry,
        FakeServiceProbe service,
        UpdateMode target)
    {
        var result = CreateService(registry, service, TempSnapshotPath())
            .Verify(target);

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static WindowsUpdateControlService CreateService(
        FakeRegistryProbe registry,
        FakeServiceProbe service,
        string snapshotPath)
    {
        var instance = new WindowsUpdateControlService(snapshotPath);
        typeof(WindowsUpdateControlService)
            .GetProperty("RegistryProbeOverride", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .SetValue(instance, registry);
        typeof(WindowsUpdateControlService)
            .GetProperty("ServiceProbeOverride", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .SetValue(instance, service);
        return instance;
    }

    private static UpdateControlStatus StatusWithItems(params UpdateControlItem[] items)
        => new(UpdateMode.Unknown, UpdateModeScope.Unknown, IsElevated: true, items);
}
