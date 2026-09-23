using System.Security;
using Microsoft.Win32;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 策略层（L1 风险）：通过注册表控制 Windows 更新行为。
///
/// 键值语义（这两个值必须成对理解，单独看任一个都会误判）：
///   NoAutoUpdate  1 = 不自动更新，0/缺省 = 允许自动更新
///   AUOptions     1 = 保持更新但不自动安装
///                 2 = 通知下载并通知安装  ← 本服务"仅通知"档
///                 3 = 自动下载并通知安装
///                 4 = 自动下载并按计划安装（系统默认）
///
/// 兼容性：Home 版没有组策略编辑器，但**仍会读取这些 Policies 键**，所以注册表方案在
/// Home 上同样有效；这一点常被误认为"Home 版不支持"。
/// </summary>
public sealed partial class WindowsUpdateControlService
{
    private const string NoAutoUpdateValue = "NoAutoUpdate";
    private const string AuOptionsValue = "AUOptions";

    private IUpdateRegistryProbe Probe => RegistryProbeOverride ?? new RegistryUpdateProbe();

    /// <summary>目标档位 → (NoAutoUpdate, AUOptions) 的期望值对。</summary>
    internal static (int NoAutoUpdate, int AuOptions) ExpectedValues(UpdateMode mode) => mode switch
    {
        UpdateMode.Automatic => (0, 4),
        UpdateMode.NotifyOnly => (1, 2),
        UpdateMode.Disabled => (1, 1),
        _ => (0, 4),
    };

    /// <summary>
    /// 读取当前状态。
    ///
    /// 刻意**不返回 <c>Result</c>**：读不到注册表恰恰是本方法要如实表达的常态之一
    /// （键不存在 / 非提权），用失败包起来会让调用方把"读不到"当成"操作失败"。
    /// 每个探针读数以 <c>null</c> 记入条目，由 <see cref="UpdateControlItem.IsReadable"/> 区分。
    /// </summary>
    internal UpdateControlStatus ReadStatus()
    {
        var items = new List<UpdateControlItem>(8);

        var policyNoAuto = Probe.ReadPolicyValue(NoAutoUpdateValue);
        var policyOptions = Probe.ReadPolicyValue(AuOptionsValue);
        items.Add(new UpdateControlItem("policy.NoAutoUpdate", "策略：不自动更新", policyNoAuto, policyNoAuto));
        items.Add(new UpdateControlItem("policy.AUOptions", "策略：更新方式", policyOptions, policyOptions));

        var auNoAuto = Probe.ReadWindowsUpdateValue(NoAutoUpdateValue);
        var auOptions = Probe.ReadWindowsUpdateValue(AuOptionsValue);
        items.Add(new UpdateControlItem("windowsupdate.NoAutoUpdate", "更新设置：不自动更新", auNoAuto, auNoAuto));
        items.Add(new UpdateControlItem("windowsupdate.AUOptions", "更新设置：更新方式", auOptions, auOptions));

        var status = new UpdateControlStatus(UpdateMode.Unknown, UpdateModeScope.Unknown, IsElevated, items);
        var mode = DeriveMode(status);
        var scope = mode == UpdateMode.Unknown
            ? UpdateModeScope.Unknown
            : (policyNoAuto is not null || policyOptions is not null ? UpdateModeScope.Policy : UpdateModeScope.Service);

        return status with { Mode = mode, Scope = scope };
    }

    /// <summary>
    /// 写入策略层。<paramref name="mode"/> 为 Automatic 时用**删除键值**而非写 0 ——
    /// 写 0 会让这个键长期停留在注册表里，在部分版本上与后续系统策略产生歧义。
    /// </summary>
    private bool ApplyPolicyLayer(UpdateMode mode, List<string> applied)
    {
        if (mode == UpdateMode.Automatic)
        {
            var removedAuto = Probe.DeletePolicyValue(NoAutoUpdateValue);
            var removedOptions = Probe.DeletePolicyValue(AuOptionsValue);
            if (removedAuto)
            {
                applied.Add("policy.NoAutoUpdate=删除");
            }

            if (removedOptions)
            {
                applied.Add("policy.AUOptions=删除");
            }

            return removedAuto || removedOptions || !Probe.PolicyKeyExists;
        }

        var (noAuto, auOptions) = ExpectedValues(mode);
        var okNoAuto = Probe.WritePolicyValue(NoAutoUpdateValue, noAuto);
        var okOptions = Probe.WritePolicyValue(AuOptionsValue, auOptions);
        if (okNoAuto)
        {
            applied.Add($"policy.NoAutoUpdate={noAuto}");
        }

        if (okOptions)
        {
            applied.Add($"policy.AUOptions={auOptions}");
        }

        return okNoAuto && okOptions;
    }

    /// <summary>
    /// 真实注册表探针。所有写入都走 64 位视图，避免 WOW64 重定向写错位置。
    ///
    /// 读取返回 <c>null</c> 只表示"读不到"（键不存在或没权限），**不抛异常**：
    /// <c>OpenSubKey</c> 在键存在但无读权限时抛 <see cref="SecurityException"/>，
    /// 必须在这里吞掉，否则非提权运行时整个状态探测会直接崩掉。
    /// </summary>
    private sealed class RegistryUpdateProbe : IUpdateRegistryProbe
    {
        public bool PolicyKeyExists => OpenPolicyKey(writable: false) is not null;

        public string? ReadPolicyValue(string valueName) => ReadValue(PolicyKeyPath, valueName);

        public string? ReadWindowsUpdateValue(string valueName) => ReadValue(WindowsUpdateKeyPath, valueName);

        public bool WritePolicyValue(string valueName, int value) => WriteValue(PolicyKeyPath, valueName, value);

        public bool DeletePolicyValue(string valueName)
        {
            using var key = OpenPolicyKey(writable: true);
            if (key is null)
            {
                return false;
            }

            try
            {
                // 键值本来就不存在时不算失败：目标状态（无该值）已经达成
                if (key.GetValue(valueName) is null)
                {
                    return true;
                }

                key.DeleteValue(valueName, throwOnMissingValue: false);
                return true;
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool WriteWindowsUpdateValue(string valueName, int value)
            => WriteValue(WindowsUpdateKeyPath, valueName, value);

        private static string? ReadValue(string path, string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
                return key?.GetValue(valueName)?.ToString();
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool WriteValue(string path, string valueName, int value)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
                if (key is null)
                {
                    return false;
                }

                key.SetValue(valueName, value, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static RegistryKey? OpenPolicyKey(bool writable)
        {
            try
            {
                return Registry.LocalMachine.OpenSubKey(PolicyKeyPath, writable);
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
