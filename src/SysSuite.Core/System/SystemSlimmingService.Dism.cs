using System.Globalization;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// T3.5 的 DISM 部分：组件存储分析与清理。
/// </summary>
/// <remarks>
/// 分工：本文件负责**起进程、判退出码、组织结果**；输出解析在 <c>.Parse.cs</c>，
/// compact 在 <c>.Compact.cs</c>，Windows.old / 传递优化的体积测量在 <c>.Targets.cs</c>。
/// </remarks>
public sealed partial class SystemSlimmingService
{
    internal const string ComponentStoreTargetId = "component-store";

    internal static async Task<long> AnalyzeComponentStoreAsync(
        List<SlimmingTarget> targets,
        List<string> log,
        CancellationToken cancellationToken)
    {
        var outcome = await ExternalProcessRunner.RunAsync(
            DismExecutable,
            ["/Online", "/Cleanup-Image", "/AnalyzeComponentStore"],
            AnalyzeTimeout,
            cancellationToken).ConfigureAwait(false);

        var lines = SplitLines(outcome.AllOutput);
        log.Clear();
        log.AddRange(lines);

        if (outcome.RequiresElevation)
        {
            targets.Add(UnavailableTarget("需要管理员权限才能分析组件存储。请以管理员身份重启本程序。"));
            return 0;
        }

        if (outcome.ExitCode != 0)
        {
            targets.Add(UnavailableTarget($"分析失败（退出码 {outcome.ExitCode}），详情见分析日志。"));
            return 0;
        }

        var (reclaimable, packages) = ParseAnalyzeOutput(lines);
        var recommended = lines.Any(IsCleanupRecommended);

        var detail = new List<string>(3);
        detail.Add(packages is > 0
            ? string.Create(CultureInfo.InvariantCulture, $"可回收的程序包数：{packages}。")
            : "分析输出中未报告可回收程序包数。");
        detail.Add("清理会移除旧版本组件的备份，之后已安装的更新将无法卸载。");
        if (recommended)
        {
            detail.Add("DISM 建议执行清理。");
        }

        targets.Add(new SlimmingTarget(
            ComponentStoreTargetId,
            "组件存储（WinSxS）",
            reclaimable,
            string.Join(" ", detail),
            Actionable: true,
            Path: null));

        return reclaimable;
    }

    public async Task<Result<SystemSlimmingResult>> StartComponentCleanupAsync(
        SystemSlimmingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 门控与提权都是**预期内**的结果，不是调用失败 —— 它们走 SlimmingOutcome 表达，
        // 这样 UI 只需看一个字段就能决定怎么渲染（与 StorageHealthReport 的既有约定一致）
        if (!IsEnabled)
        {
            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.Disabled,
                "系统瘦身是实验性功能，需在设置页同时打开「实验性功能」与「系统瘦身」。",
                0,
                []));
        }

        if (!Environment.IsPrivilegedProcess)
        {
            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.RequiresElevation,
                "清理组件存储需要管理员权限。请以管理员身份重启本程序后重试。",
                0,
                []));
        }

        var arguments = new List<string>(4) { "/Online", "/Cleanup-Image", "/StartComponentCleanup" };
        if (options.ResetBase)
        {
            arguments.Add("/ResetBase");
        }

        var outcome = await ExternalProcessRunner.RunAsync(
            DismExecutable,
            arguments,
            CleanupTimeout,
            cancellationToken).ConfigureAwait(false);

        var lines = SplitLines(outcome.AllOutput);
        var (kept, logPath) = await PersistLogAsync("component-cleanup", lines, cancellationToken).ConfigureAwait(false);
        var suffix = logPath is null ? string.Empty : $" 完整日志：{logPath}";

        // DISM 的成功码有两个：0 与 3010（成功但需要重启）
        if (outcome.ExitCode is 0 or ErrorSuccessRebootRequired)
        {
            var resetBaseNote = options.ResetBase ? "（含 /ResetBase，已安装的更新不再可卸载）" : string.Empty;
            var rebootNote = outcome.ExitCode == ErrorSuccessRebootRequired ? " 部分清理将在重启后完成。" : string.Empty;

            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.Success,
                $"组件存储清理完成{resetBaseNote}。{rebootNote}{suffix}",
                0,
                kept));
        }

        var message = outcome.RequiresElevation
            ? "清理组件存储需要管理员权限。"
            : DescribeFailure("组件存储清理", outcome, lines);

        return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
            outcome.RequiresElevation ? SlimmingOutcome.RequiresElevation : SlimmingOutcome.Failed,
            message + suffix,
            0,
            kept));
    }

    internal const int ErrorSuccessRebootRequired = 3010;

    private static SlimmingTarget UnavailableTarget(string detail)
        => new(ComponentStoreTargetId, "组件存储（WinSxS）", -1, detail, Actionable: false, Path: null);

    private static string DescribeFailure(string operation, ProcessOutcome outcome, List<string> lines)
    {
        // 取最后几行：DISM 的错误说明总在输出末尾，前面的进度条没有诊断价值
        var tail = lines.Count > 3 ? string.Join(" ", lines.Skip(lines.Count - 3)) : string.Join(" ", lines);
        return outcome.TimedOut
            ? $"{operation}超时，已终止。"
            : $"{operation}失败（退出码 {outcome.ExitCode}）。{tail}";
    }
}
