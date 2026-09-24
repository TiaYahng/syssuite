using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.Pages;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 系统瘦身页面的交互副作用（D1 约定：确认框/文件对话框留在 Page，ViewModel 保持可单测）。
/// </summary>
public interface ISystemSlimmingInteractions
{
    /// <summary>清理组件存储前的确认。开启 /ResetBase 时必须把不可回退这一点讲清楚。</summary>
    Task<bool> ConfirmComponentCleanupAsync(bool resetBase);

    /// <summary>选择要压缩的目录。取消返回 null。</summary>
    Task<string?> PickCompactDirectoryAsync();

    /// <summary>压缩前的确认（会改变文件属性，不可逆）。</summary>
    Task<bool> ConfirmCompactAsync(string directory, bool executableOnly);

    /// <summary>把分析日志写到磁盘并回报最终路径。取消返回 null。</summary>
    Task<string?> ExportLogAsync(string content);
}

/// <summary>
/// 系统瘦身（T3.5）ViewModel。
/// </summary>
/// <remarks>
/// 全部动作为手动触发，不注册任何后台计时器 —— 这是本套件里唯一会改动系统组件存储的能力，
/// 让它在后台自己跑是不可接受的。
/// </remarks>
public sealed class SystemSlimmingViewModel : PageViewModelBase
{
    private readonly ISystemSlimmingService service;
    private readonly ISystemSlimmingInteractions interactions;

    private string reportText = "尚未分析。点击「分析」查看可回收空间。";
    private string logText = string.Empty;
    private bool resetBase;
    private bool executableOnly = true;

    public SystemSlimmingViewModel(ISystemSlimmingService service, ISystemSlimmingInteractions interactions)
    {
        this.service = service;
        this.interactions = interactions;
    }

    public string ReportText
    {
        get => reportText;
        private set => SetProperty(ref reportText, value);
    }

    public string LogText
    {
        get => logText;
        private set => SetProperty(ref logText, value);
    }

    /// <summary>
    /// /ResetBase：清理后**已安装的更新无法卸载**。默认关闭，且页面必须给出红字警告。
    /// </summary>
    public bool ResetBase
    {
        get => resetBase;
        set
        {
            if (SetProperty(ref resetBase, value))
            {
                OnPropertyChanged(nameof(ResetBaseWarning));
            }
        }
    }

    public bool ExecutableOnly
    {
        get => executableOnly;
        set => SetProperty(ref executableOnly, value);
    }

    public string ResetBaseWarning => resetBase
        ? "已启用 /ResetBase：清理后所有已安装的更新将无法卸载，且此操作不可回退。"
        : string.Empty;

    /// <summary>开关未打开时页面隐藏整个区块，而不是让按钮点了没反应。</summary>
    public bool IsAvailable => service.IsEnabled;

    public string AvailabilityText => service.IsEnabled
        ? string.Empty
        : "系统瘦身是实验性功能。请在「设置」页同时打开「实验性功能」与「系统瘦身」。";

    public async Task AnalyzeAsync()
    {
        await RunBusyAsync(
            async () =>
            {
                var result = await service.AnalyzeAsync();
                if (!result.IsSuccess || result.Value is null)
                {
                    SetError(result.Message);
                    return;
                }

                var report = result.Value;
                ReportText = BuildReportText(report);
                LogText = report.ComponentStoreLog.Count == 0
                    ? "（本次分析没有产生输出）"
                    : string.Join(Environment.NewLine, report.ComponentStoreLog);

                SetStatus(report.IsElevated
                    ? report.Summary
                    : report.Summary + " 当前非提权，组件存储数据可能缺失。");
            },
            "正在分析系统占用（DISM 可能需要数分钟）...");
    }

    public async Task CleanupAsync()
    {
        if (!service.IsEnabled)
        {
            SetError(AvailabilityText);
            return;
        }

        if (!await interactions.ConfirmComponentCleanupAsync(ResetBase).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var result = await service.StartComponentCleanupAsync(new SystemSlimmingOptions(ResetBase));
                ApplyOperationResult(result, "组件存储清理已完成，但没有产生输出。");
            },
            "正在清理组件存储（可能耗时数十分钟，请勿关闭程序）...");
    }

    public async Task CompactAsync()
    {
        if (!service.IsEnabled)
        {
            SetError(AvailabilityText);
            return;
        }

        var directory = await interactions.PickCompactDirectoryAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (!await interactions.ConfirmCompactAsync(directory, ExecutableOnly).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var result = await service.CompactAsync(directory, ExecutableOnly);
                ApplyOperationResult(result, "压缩已完成，但没有产生输出。");
            },
            "正在压缩目标目录...");
    }

    public async Task ExportLogAsync()
    {
        if (string.IsNullOrWhiteSpace(LogText))
        {
            SetStatus("当前没有可导出的日志。");
            return;
        }

        var path = await interactions.ExportLogAsync(LogText).ConfigureAwait(true);
        SetStatus(path is null ? "已取消导出。" : $"日志已导出到 {path}");
    }

    protected override Task RefreshAsync() => Task.CompletedTask;

    /// <summary>
    /// 统一渲染动作结果：门控未开 / 需提权 / 被拒都是**预期内**的档位，
    /// 走 <see cref="SlimmingOutcome"/> 表达，所以这里只按 Outcome 分流，
    /// 不再依赖 <c>Result.IsSuccess</c> —— 后者只反映"调用本身有没有炸"。
    /// </summary>
    private void ApplyOperationResult(Result<SystemSlimmingResult> result, string emptyOutputNote)
    {
        if (result.Value is null)
        {
            SetError(result.Message);
            return;
        }

        var value = result.Value;
        LogText = value.Log.Count == 0 ? emptyOutputNote : JoinLog(value.Log);

        if (value.IsSuccess)
        {
            SetStatus(value.Message);
        }
        else
        {
            SetError(value.Message);
        }
    }

    private static string JoinLog(IReadOnlyList<string> lines)
        => lines.Count == 0 ? "（本次操作没有产生输出）" : string.Join(Environment.NewLine, lines);

    private static string BuildReportText(SystemSlimmingReport report)
    {
        var builder = new global::System.Text.StringBuilder(256);

        foreach (var target in report.Targets)
        {
            var size = target.IsSizeKnown && target.SizeBytes > 0
                ? DiskCleanerFormat.Bytes(target.SizeBytes)
                : "未知";
            _ = builder.Append(target.Title).Append('：').Append(size).AppendLine();
            _ = builder.Append("    ").AppendLine(target.Detail);
        }

        _ = builder.AppendLine();
        _ = builder.Append(report.IsElevated ? "权限：管理员" : "权限：普通用户（组件存储数据可能缺失）");
        return builder.ToString();
    }
}
