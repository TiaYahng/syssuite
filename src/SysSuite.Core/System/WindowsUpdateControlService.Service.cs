using Microsoft.Win32;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 服务与计划任务层（L2 风险）。
///
/// 三个服务各自影响不同环节，**禁用的后果不一样**，因此不能笼统地"全禁"：
///   wuauserv  Windows Update 服务本体 —— 停了就完全不检查更新
///   UsoSvc    Update Orchestrator —— 负责编排下载与安装；停它会让更新卡在"待安装"
///   DoSvc     Delivery Optimization —— 只负责 P2P 分发，停它不影响更新的功能性
///
/// 因此默认只动 wuauserv 与 UsoSvc；DoSvc 仅记录不断言，避免把"省流量"当成"停更新"。
/// </summary>
public sealed partial class WindowsUpdateControlService
{
    /// <summary>参与控制的服务。DoSvc 不参与，理由见类注释。</summary>
    internal static readonly string[] ControlledServices = ["wuauserv", "UsoSvc"];

    /// <summary>Windows Update 相关计划任务。路径为任务计划库中的相对路径。</summary>
    internal static readonly string[] UpdateTaskPaths =
    [
        @"Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
        @"Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task",
        @"Microsoft\Windows\WindowsUpdate\Scheduled Start",
    ];

    /// <summary>
    /// 不参与控制的计划任务。
    ///
    /// <c>UsoSvc</c> 的维护任务（如 <c>Reboot_AC</c>）负责在需要重启时提醒用户，
    /// 禁掉它不会"停止更新"，只会让用户永远不知道要重启 —— 这属于把系统改坏，
    /// 不属于"控制更新"。列在这里是为了让后续维护者知道是**有意排除**而非遗漏。
    /// </summary>
    internal static readonly string[] ExcludedTaskPaths =
    [
        @"Microsoft\Windows\UpdateOrchestrator\Reboot_AC",
        @"Microsoft\Windows\UpdateOrchestrator\Reboot_Battery",
        @"Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
    ];

    private IUpdateServiceProbe ServiceProbe => ServiceProbeOverride ?? new ServiceUpdateProbe();

    /// <summary>服务层是否可用。某些精简版/Server Core 上 wuauserv 不存在。</summary>
    internal bool IsServiceLayerAvailable()
        => ControlledServices.Any(name => !string.Equals(ServiceProbe.ReadStartType(name), "Missing", StringComparison.Ordinal));

    /// <summary>
    /// 写入服务层：Disabled 档停止并禁用服务、禁用计划任务；其余档恢复为手动并启用任务。
    ///
    /// wuauserv 的正确"可更新"启动类型是 **Manual（手动/触发启动）**，不是 Automatic。
    /// Win10 1709 起 Windows Update 改为由 UsoSvc 按需触发，把 wuauserv 设成 Automatic
    /// 反而偏离系统默认状态，还原时会被判定为"没还原干净"。
    /// </summary>
    private bool ApplyServiceLayer(UpdateMode mode, List<string> applied)
    {
        var disable = mode == UpdateMode.Disabled;
        var allOk = true;

        foreach (var service in ControlledServices)
        {
            if (disable)
            {
                ServiceProbe.StopService(service);
            }

            var target = disable ? "Disabled" : "Manual";
            if (ServiceProbe.SetStartType(service, target))
            {
                applied.Add($"{service}={target}");
            }
            else
            {
                allOk = false;
            }
        }

        foreach (var task in UpdateTaskPaths)
        {
            if (ServiceProbe.SetTaskState(task, enabled: !disable))
            {
                applied.Add($"{task}={(disable ? "Disabled" : "Enabled")}");
            }
            else
            {
                allOk = false;
            }
        }

        return allOk;
    }

    private List<UpdateControlItem> ReadServiceItems()
    {
        var items = new List<UpdateControlItem>(ControlledServices.Length + UpdateTaskPaths.Length);
        foreach (var service in ControlledServices)
        {
            var actual = ServiceProbe.ReadStartType(service);
            items.Add(new UpdateControlItem(
                $"service.{service}",
                $"服务：{service}",
                actual is "Missing" ? null : actual,
                null));
        }

        foreach (var task in UpdateTaskPaths)
        {
            var actual = ServiceProbe.ReadTaskState(task);
            items.Add(new UpdateControlItem(
                $"task.{task}",
                $"计划任务：{task}",
                actual is "Missing" ? null : actual,
                null));
        }

        return items;
    }
}
