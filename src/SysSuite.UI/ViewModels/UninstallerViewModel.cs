using System.IO;
using System.Net;
using System.Text;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Abstractions.Settings;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 卸载器页面 ViewModel（D1：原先业务逻辑全部堆在 <c>UninstallerPage.*.cs</c> 里）。
///
/// 迁移原则：ViewModel 负责"做什么"（枚举、加载图标、卸载、强制删除、导出、剪贴板），
/// Page 只保留"怎么显示"（依赖属性、动画、对话框宿主、Popup 定位）。
/// 需要弹窗/文件对话框的场合走 <see cref="IUninstallerInteractions"/>，
/// 由 Page 实现，这样这一层不引 WPF 控件、可以直接单测。
/// </summary>
public sealed partial class UninstallerViewModel : PageViewModelBase
{
    private readonly IUninstallEnumerationService enumerationService;
    private readonly IIconCacheService iconCacheService;
    private readonly IUninstallService uninstallService;
    private readonly IForceDeleteService forceDeleteService;
    private readonly IAppChangeMonitor changeMonitor;
    private readonly ISettingsService settingsService;
    private readonly IUninstallerInteractions interactions;

    private CancellationTokenSource? iconLoadSource;

    public UninstallerViewModel(
        IUninstallEnumerationService enumerationService,
        IIconCacheService iconCacheService,
        IUninstallService uninstallService,
        IForceDeleteService forceDeleteService,
        IAppChangeMonitor changeMonitor,
        ISettingsService settingsService,
        IUninstallerInteractions interactions)
    {
        this.enumerationService = enumerationService;
        this.iconCacheService = iconCacheService;
        this.uninstallService = uninstallService;
        this.forceDeleteService = forceDeleteService;
        this.changeMonitor = changeMonitor;
        this.settingsService = settingsService;
        this.interactions = interactions;
        changeMonitor.AppListChanged += OnAppListChanged;
    }

    /// <summary>图标加载进度。Page 订阅以更新状态栏与进度环。</summary>
    public event EventHandler<IconProgressEventArgs>? IconProgressChanged;

    /// <summary>应用列表变化时抛给 Page，用于提示并自动刷新。</summary>
    public event EventHandler<int>? AppListChanged;

    public IReadOnlyList<AppRow> Rows => rows;

    public string MonitorBadgeText => changeMonitor.IsRunning ? "监控：开启" : "监控：关闭";

    public string MonitorButtonText => changeMonitor.IsRunning ? "关闭监控" : "启动监控";

    public bool CanForceDelete => settingsService.Current.EnableExperimentalFeatures
        && settingsService.Current.EnableForceDelete;

    public string? SelectedRegistryPath => SelectedApp?.KeyPath;

    public string? SelectedInstallDirectory =>
        SelectedApp?.InstallDir is { } dir && Directory.Exists(dir) ? dir : null;
    public string DescribeSelection()
    {
        if (SelectedApp is not { } app)
        {
            return string.Empty;
        }

        var publisher = string.IsNullOrWhiteSpace(app.Publisher) ? "—" : app.Publisher;
        var version = string.IsNullOrWhiteSpace(app.Version) ? "—" : app.Version;
        var directory = string.IsNullOrWhiteSpace(app.InstallDir) ? "—" : app.InstallDir;
        var keyPath = string.IsNullOrWhiteSpace(app.KeyPath) ? "—" : app.KeyPath;
        return $"{app.Name}\n{publisher}\n{version}\n{directory}\n{keyPath}";
    }

    /// <summary>首次进入页面：启动变更监控并枚举。</summary>
    public async Task ActivateAsync()
    {
        changeMonitor.Start();
        OnPropertyChanged(nameof(MonitorBadgeText));
        OnPropertyChanged(nameof(MonitorButtonText));
        await RefreshCoreAsync(ignoreBusy: false);
    }

    public override void Dispose()
    {
        changeMonitor.AppListChanged -= OnAppListChanged;
        changeMonitor.StopMonitoring();
        iconLoadSource?.Cancel();
        iconLoadSource?.Dispose();
        iconLoadSource = null;
        base.Dispose();
    }

    protected override Task RefreshAsync() => RefreshCoreAsync(ignoreBusy: false);

    protected override void Cancel()
    {
        iconLoadSource?.Cancel();
    }

    private async Task RefreshCoreAsync(bool ignoreBusy)
    {
        if (IsBusy && !ignoreBusy)
        {
            return;
        }

        // 新一轮刷新作废上一轮的图标加载：老的图标任务往往正卡在 COM/磁盘 IO 上，
        // 不取消就会与新任务抢线程池与磁盘，刷新越频繁越慢。
        var iconLoad = new CancellationTokenSource();
        var previous = iconLoadSource;
        iconLoadSource = iconLoad;
        previous?.Cancel();
        previous?.Dispose();

        await RunBusyAsync(
            async () =>
            {
                var result = await enumerationService.RefreshAsync();
                if (!result.IsSuccess || result.Value is null)
                {
                    var cached = await enumerationService.ListAsync();
                    if (cached is not { IsSuccess: true, Value: { Count: > 0 } cachedApps })
                    {
                        SetError(string.IsNullOrWhiteSpace(result.Message) ? "刷新失败。" : result.Message);
                        return;
                    }

                    result = new Result<IReadOnlyList<AppRecord>>(ErrorType.None, result.Message, cachedApps);
                }

                var apps = result.Value ?? [];
                var newRows = apps.Select(app => new AppRow(app)).ToList();
                SetRows(newRows);

                // 先把列表呈现出来，再后台补图标 —— 列表本身不依赖图标，
                // 等图标全部就绪才结束会让用户盯着空列表。
                SetStatus($"已加载 {newRows.Count} 个应用，正在载入图标...");
                await LoadIconsAsync(newRows, iconLoad.Token);
            },
            "正在枚举应用...");
    }

    private void OnAppListChanged(object? sender, IReadOnlyList<AppRecord> apps)
    {
        // 监控线程回调，切回 UI 线程：刷新要动绑定集合。
        _ = uiContext.InvokeAsync(() =>
        {
            SetStatus($"检测到应用列表变化，共 {apps.Count} 个应用。");
            AppListChanged?.Invoke(this, apps.Count);
            _ = RefreshCoreAsync(ignoreBusy: true);
        });
    }

    internal static string BuildHtmlReport(IEnumerable<AppRow> rows)
    {
        var list = rows.ToList();
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>SysSuite 卸载器报告</title>");
        builder.AppendLine("<style>body{font-family:'Segoe UI',sans-serif;margin:24px;color:#111}");
        builder.AppendLine("table{border-collapse:collapse;width:100%}th,td{border:1px solid #ddd;padding:6px 10px;text-align:left;font-size:13px}th{background:#f3f3f3}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine("<h2>已安装程序报告</h2>");
        builder.AppendLine(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"<p>生成时间：{DateTime.Now:yyyy-MM-dd HH:mm} · 共 {list.Count} 个程序</p>"));
        builder.AppendLine("<table><tr><th>程序</th><th>发布者</th><th>安装日期</th><th>大小</th><th>版本</th><th>来源</th></tr>");
        foreach (var row in list)
        {
            builder.AppendLine("<tr>"
                + $"<td>{WebUtility.HtmlEncode(row.Name)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.Publisher)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.InstalledOn)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.SizeText)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.Version)}</td>"
                + $"<td>{WebUtility.HtmlEncode(row.SourceLabel)}</td>"
                + "</tr>");
        }

        builder.AppendLine("</table></body></html>");
        return builder.ToString();
    }
}
