using System.IO;
using System.Security;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 更新控制兼容性矩阵（M4 T4.2）。
///
/// 存在的理由：这台工具在 Win10 19045 / Win11 22621 / 26100 上的行为**不完全一致**，
/// 而用户看到的只是"能不能改"。把差异记录成可查询的数据，UI 就能在设置页提示
/// "你的系统上这一档可能不生效"，而不是让用户改完发现没反应再来报 bug。
///
/// 特别澄清一个常见误解：**Home 版没有组策略编辑器（gpedit.msc），但仍会读取
/// <c>HKLM\SOFTWARE\Policies\...\WindowsUpdate\AU</c> 下的策略键**。
/// 所以注册表方案在 Home 上一样有效 —— 只是用户没有图形界面去手改而已。
/// </summary>
public sealed partial class WindowsUpdateControlService
{
    /// <summary>服务层不可用的系统（精简版 / Server Core）上，Disable 档只能靠策略层。</summary>
    internal UpdateCompatibilityNote DescribeCompatibility()
    {
        var build = Environment.OSVersion.Version.Build;
        var (family, hasGpedit) = build switch
        {
            >= 22000 => ("Windows 11", IsEditionWithGroupPolicyEditor()),
            _ => ("Windows 10", IsEditionWithGroupPolicyEditor()),
        };

        var serviceAvailable = IsServiceLayerAvailable();
        return new UpdateCompatibilityNote(
            family,
            hasGpedit,
            PolicyRegistryHonored: true,
            serviceAvailable,
            Note: BuildNote(build, serviceAvailable));
    }

    private static string? BuildNote(int build, bool serviceAvailable)
    {
        // 只记录**已知会改变行为**的版本差异，不堆版本号
        var notes = new List<string>(2);
        if (build is >= 17763 and < 19041)
        {
            notes.Add("该版本 wuauserv 仍可能以 Automatic 启动，还原时按快照原值回写。");
        }

        if (!serviceAvailable)
        {
            notes.Add("本机未检测到 wuauserv / UsoSvc，服务层不可用，仅策略层生效。");
        }

        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    /// <summary>
    /// 是否属于自带组策略编辑器的版本（Pro / Enterprise / Education）。
    /// 用注册表探测 <c>gpedit.msc</c> 的存在与否，不做版本号猜测。
    /// </summary>
    private static bool IsEditionWithGroupPolicyEditor()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", writable: false);
            var edition = key?.GetValue("Edition")?.ToString();
            if (string.IsNullOrEmpty(edition))
            {
                // 没这个值不代表 Home —— 很多机器根本不写。用 gpedit 的实际文件存在性兜底。
                var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return File.Exists(Path.Combine(system32, "gpedit.msc"));
            }

            return edition.Contains("Home", StringComparison.OrdinalIgnoreCase) is false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }
}
