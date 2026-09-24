using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace SysSuite.Core.System;

public enum SystemRestoreOutcome
{
    Created,

    /// <summary>同一天已经建过，按"单日去重"跳过。</summary>
    SkippedSameDay,

    /// <summary>系统还原被策略关闭，或当前系统（如 Server 版）不提供该能力。</summary>
    Disabled,

    Failed,
}

public sealed record SystemRestoreResult(SystemRestoreOutcome Outcome, string Message, long SequenceNumber)
{
    public bool IsCreated => Outcome == SystemRestoreOutcome.Created;
}

/// <summary>
/// 清理前的系统还原点（T3.4）。
/// </summary>
/// <remarks>
/// 三个容易踩的点：
///
/// 1. **需要管理员权限**，且受组策略控制（<c>DisableSR</c>）。失败一律降级为告警，
///    绝不阻断清理 —— 清理本身已经有逐批备份（G7），还原点是额外的一道保险，不是前提。
/// 2. **单日去重**：一天内建多个还原点既没意义又占用卷影空间。去重状态记在 HKCU，
///    跨进程与跨重启都成立；只记日期不记时间，避免"差几小时就又建一个"。
/// 3. srclient.dll 在部分 SKU（Server 版）上不存在，加载失败必须吞掉而不是崩。
/// </remarks>
public static class SystemRestoreService
{
    private const int BeginSystemChange = 100;
    private const int ModifySettings = 12;
    private const int DescriptionCapacity = 256;

    private const string PolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";
    private const string StateKeyPath = @"Software\SysSuite\SystemRestore";
    private const string LastDateValueName = "LastRestorePointDate";

    /// <summary>系统还原是否被策略或 SKU 关闭。</summary>
    public static bool IsDisabledByPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyKeyPath, writable: false);
            var disable = key?.GetValue("DisableSR");
            return disable is not null && Convert.ToInt32(disable, CultureInfo.InvariantCulture) != 0;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static SystemRestoreResult Create(string description)
    {
        return Create(description, DateTimeOffset.Now);
    }

    public static SystemRestoreResult Create(string description, DateTimeOffset now)
    {
        // 短路判定抽成纯函数：注册表与 srclient 都碰不到单测环境，但"该不该建"这个决定必须可测
        if (Decide(IsDisabledByPolicy(), TryReadLastDate(), now) is { } shortCircuit)
        {
            return shortCircuit;
        }

        var info = new RestorePointInfo
        {
            EventType = BeginSystemChange,
            RestorePointType = ModifySettings,
            SequenceNumber = 0,
            Description = Truncate(description),
        };

        bool ok;
        StateManagerStatus status;
        try
        {
            ok = SetSystemRestorePoint(ref info, out status);
        }
        catch (DllNotFoundException)
        {
            return new SystemRestoreResult(SystemRestoreOutcome.Disabled, "当前系统不提供系统还原（srclient 不可用）。", 0);
        }
        catch (EntryPointNotFoundException)
        {
            return new SystemRestoreResult(SystemRestoreOutcome.Disabled, "当前系统不提供系统还原（入口缺失）。", 0);
        }

        if (!ok)
        {
            var code = (int)status.Status;
            var message = code == 5
                ? "创建系统还原点需要管理员权限，已跳过（清理本身仍会生成备份）。"
                : $"创建系统还原点失败：{Describe(code)}";

            return new SystemRestoreResult(SystemRestoreOutcome.Failed, message, status.SequenceNumber);
        }

        WriteLastDate(now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return new SystemRestoreResult(
            SystemRestoreOutcome.Created,
            $"已创建系统还原点（序号 {status.SequenceNumber}）。",
            status.SequenceNumber);
    }

    /// <summary>
    /// 纯判定：返回非 null 表示无需调用系统 API，直接以此结果收尾。
    ///
    /// 「无记录」与「记录损坏」都返回 null（即允许创建）—— 宁可多建一个还原点，
    /// 也不因为状态值脏了就让用户失去这道保险。
    /// </summary>
    internal static SystemRestoreResult? Decide(bool disabledByPolicy, string? lastDate, DateTimeOffset now)
    {
        if (disabledByPolicy)
        {
            return new SystemRestoreResult(SystemRestoreOutcome.Disabled, "系统还原已被组策略关闭。", 0);
        }

        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return string.Equals(lastDate, today, StringComparison.Ordinal)
            ? new SystemRestoreResult(
                SystemRestoreOutcome.SkippedSameDay,
                $"今天（{today}）已创建过系统还原点，跳过。",
                0)
            : null;
    }

    /// <summary>srclient 的 Description 是 <c>MAX_PATH</c> 级别的定长缓冲，超长会截断而非报错。</summary>
    internal static string Truncate(string description)
    {
        if (description.Length <= DescriptionCapacity - 1)
        {
            return description;
        }

        return description[..(DescriptionCapacity - 1)];
    }

    private static string? TryReadLastDate()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKeyPath, writable: false);
            return key?.GetValue(LastDateValueName)?.ToString();
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void WriteLastDate(string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StateKeyPath);
            key.SetValue(LastDateValueName, value, RegistryValueKind.String);
        }
        catch (SecurityException)
        {
            // 记不住去重状态顶多是多建一个还原点，不影响清理本身
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Describe(int code)
        => code == 0 ? "未知错误" : $"{new Win32Exception(code).Message} (0x{code:X8})";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestorePointInfo
    {
        public uint EventType;
        public uint RestorePointType;
        public long SequenceNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DescriptionCapacity)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateManagerStatus
    {
        public uint Status;
        public long SequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern bool SetSystemRestorePoint(ref RestorePointInfo info, out StateManagerStatus status);
}
