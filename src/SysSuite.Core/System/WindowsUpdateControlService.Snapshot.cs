using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 快照采集、落盘、还原（T4.1 的"一键还原"基础）。
///
/// 为什么必须是三类而不是一类：只记注册表键值时，还原后服务仍是 Disabled；
/// 只记服务位时，策略键还留在注册表里继续生效。任何一类漏记都会得到"看着还原了、
/// 实际没生效"的结果，而这正是这类工具最容易翻车的地方。
///
/// 快照刻意保持**纯数据**并落 JSON：还原是跨进程、跨重启的动作。
/// </summary>
public sealed partial class WindowsUpdateControlService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private Result<string> CaptureSnapshot()
    {
        var entries = new List<UpdateSnapshotEntry>(16);

        // 策略层：连 WindowsUpdate\AU 一起记，因为系统自带的"更新设置"写在那里
        foreach (var valueName in new[] { NoAutoUpdateValue, AuOptionsValue })
        {
            entries.Add(new UpdateSnapshotEntry(
                UpdateSnapshotKind.RegistryValue,
                $"policy|{valueName}",
                Probe.ReadPolicyValue(valueName)));
            entries.Add(new UpdateSnapshotEntry(
                UpdateSnapshotKind.RegistryValue,
                $"windowsupdate|{valueName}",
                Probe.ReadWindowsUpdateValue(valueName)));
        }

        // 服务层
        foreach (var service in ControlledServices)
        {
            var startType = ServiceProbe.ReadStartType(service);
            entries.Add(new UpdateSnapshotEntry(
                UpdateSnapshotKind.ServiceStartType,
                service,
                startType is "Missing" ? null : startType));
        }

        // 计划任务层
        foreach (var task in UpdateTaskPaths)
        {
            var state = ServiceProbe.ReadTaskState(task);
            entries.Add(new UpdateSnapshotEntry(
                UpdateSnapshotKind.ScheduledTaskState,
                task,
                state is "Missing" ? null : state));
        }

        var snapshot = new UpdateControlSnapshot(
            DateTimeOffset.UtcNow,
            Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture),
            ReadStatus().Mode,
            entries);

        try
        {
            var directory = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot, SnapshotJsonOptions));
            return new Result<string>(ErrorType.None, string.Empty, snapshotPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Result<string>(ErrorType.Internal, $"快照写入失败：{exception.Message}");
        }
    }

    private Result<UpdateControlOperationResult> ApplyMode(UpdateMode mode, bool includeServiceLayer)
    {
        if (mode == UpdateMode.Unknown)
        {
            return new Result<UpdateControlOperationResult>(ErrorType.InvalidInput, "无效的更新档位。");
        }

        if (!IsElevated)
        {
            return new Result<UpdateControlOperationResult>(
                ErrorType.AccessDenied,
                "修改更新设置需要管理员权限。请以管理员身份重启本程序后重试。");
        }

        // 先落快照再改动；快照失败则整个操作中止 —— 没有退路的破坏性操作不该被执行
        var snapshot = CaptureSnapshot();
        if (!snapshot.IsSuccess)
        {
            return new Result<UpdateControlOperationResult>(snapshot.Error, snapshot.Message);
        }

        var applied = new List<string>(8);
        var policyOk = ApplyPolicyLayer(mode, applied);
        if (!policyOk)
        {
            return new Result<UpdateControlOperationResult>(
                ErrorType.AccessDenied,
                "策略层写入失败，可能被组策略锁定或权限不足。已保留操作前快照。",
                null,
                snapshot.Value);
        }

        var allOk = true;
        if (includeServiceLayer)
        {
            allOk = ApplyServiceLayer(mode, applied);
        }

        // 把"我们应用了哪一档"回写进快照。没有这一步，自检只能拿当前档位反推期望值，
        // 在从未改过的机器上会把系统默认值当成"被回滚"（实测会误报 UsoSvc）。
        MarkTargetMode(mode);

        var status = ReadStatus();
        var result = new UpdateControlOperationResult(mode, status.Scope, snapshot.Value, applied);

        return allOk
            ? new Result<UpdateControlOperationResult>(ErrorType.None, string.Empty, result)
            : new Result<UpdateControlOperationResult>(
                ErrorType.Internal,
                "策略层已应用，但部分服务或计划任务未能修改（可能被系统保护）。已保留快照。",
                result,
                snapshot.Value);
    }

    /// <summary>
    /// 给已落盘的快照补记目标档位。写失败不影响主流程 —— 最坏结果是自检不可用，
    /// 而不是用户拿不到已生效的设置。
    /// </summary>
    private void MarkTargetMode(UpdateMode mode)
    {
        try
        {
            var existing = TryLoadSnapshot();
            if (existing is null)
            {
                return;
            }

            File.WriteAllText(
                snapshotPath,
                JsonSerializer.Serialize(existing with { TargetMode = mode }, SnapshotJsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 自检元数据写不进去只影响漂移检测，不回滚已经生效的策略层改动
            _ = exception;
        }
    }

    private UpdateControlSnapshot? TryLoadSnapshot()
    {
        if (!File.Exists(snapshotPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UpdateControlSnapshot>(
                File.ReadAllText(snapshotPath), SnapshotJsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private Result<UpdateControlOperationResult> Restore()
    {
        if (!File.Exists(snapshotPath))
        {
            return new Result<UpdateControlOperationResult>(ErrorType.NotFound, "没有可用的快照，无法还原。");
        }

        if (!IsElevated)
        {
            return new Result<UpdateControlOperationResult>(
                ErrorType.AccessDenied,
                "还原更新设置需要管理员权限。请以管理员身份重启本程序后重试。");
        }

        var snapshot = TryLoadSnapshot();
        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            // 损坏的快照宁可报错，也不"尽力猜一个默认值"回写 —— 那会变成静默改配置
            return new Result<UpdateControlOperationResult>(
                ErrorType.Internal,
                "快照文件无法解析或内容为空，为避免误改配置已中止还原。");
        }

        var applied = new List<string>(16);
        var allOk = true;

        foreach (var entry in snapshot.Entries)
        {
            var ok = entry.Kind switch
            {
                UpdateSnapshotKind.RegistryValue => RestoreRegistryEntry(entry),
                UpdateSnapshotKind.ServiceStartType => entry.Value is null || ServiceProbe.SetStartType(entry.Target, entry.Value),
                UpdateSnapshotKind.ScheduledTaskState => entry.Value is null || ServiceProbe.SetTaskState(entry.Target, entry.Value == "Enabled"),
                _ => true,
            };

            if (ok)
            {
                applied.Add($"{entry.Target}={entry.Value ?? "删除"}");
            }
            else
            {
                allOk = false;
            }
        }

        var status = ReadStatus();
        var result = new UpdateControlOperationResult(
            status.Mode,
            status.Scope,
            snapshotPath,
            applied);

        return allOk
            ? new Result<UpdateControlOperationResult>(ErrorType.None, string.Empty, result)
            : new Result<UpdateControlOperationResult>(
                ErrorType.Internal,
                "部分项还原失败，可能被系统保护。",
                result,
                snapshotPath);
    }

    private bool RestoreRegistryEntry(UpdateSnapshotEntry entry)
    {
        var separator = entry.Target.IndexOf('|', StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        var layer = entry.Target[..separator];
        var valueName = entry.Target[(separator + 1)..];
        var isPolicy = layer == "policy";

        // 原值为 null 表示"当时这个键值不存在"，还原动作应是删除而非写 0
        if (entry.Value is null)
        {
            return isPolicy ? Probe.DeletePolicyValue(valueName) : true;
        }

        return int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && (isPolicy ? Probe.WritePolicyValue(valueName, parsed) : Probe.WriteWindowsUpdateValue(valueName, parsed));
    }
}
