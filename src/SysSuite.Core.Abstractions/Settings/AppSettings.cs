using System.Text.Json.Serialization;

namespace SysSuite.Core.Abstractions.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string Theme { get; set; } = "System";

    public string Language { get; set; } = "zh-CN";

    public string StartupPage { get; set; } = "Dashboard";

    public bool EnableExperimentalFeatures { get; set; }

    public bool EnableDefenderControl { get; set; }

    public bool EnableUpdateServiceControl { get; set; }

    public bool EnableForceDelete { get; set; }

    public bool EnableSystemSlimming { get; set; }

    /// <summary>
    /// 清理前创建系统还原点（T3.4）。默认关闭：需要管理员权限、耗时数十秒，
    /// 且清理本身已有逐批备份（G7）。失败只降级为告警，绝不阻断清理。
    /// </summary>
    public bool CreateRestorePointBeforeClean { get; set; }

    public bool EnableDesktopOverlay { get; set; }

    public string UiLayout { get; set; } = "Sidebar";

    public string AccountEmail { get; set; } = string.Empty;

    public bool WatchdogAutoStart { get; set; } = true;

    /// <summary>
    /// 跑分历史（T1.5），每项格式为 "UTC时间|机器摘要|综合分"，最多 5 条。
    /// 用字符串而非嵌套对象，避免引入新的 JSON 可序列化类型。
    /// </summary>
    public List<string> BenchmarkHistory { get; set; } = [];
}

[JsonSerializable(typeof(AppSettings))]
public sealed partial class AppSettingsJsonContext : JsonSerializerContext;


