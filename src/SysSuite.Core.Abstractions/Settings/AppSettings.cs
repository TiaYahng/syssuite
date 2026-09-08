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

    public bool EnableDesktopOverlay { get; set; }

    public string UiLayout { get; set; } = "Sidebar";

    public string AccountEmail { get; set; } = string.Empty;

    public bool WatchdogAutoStart { get; set; } = true;
}

[JsonSerializable(typeof(AppSettings))]
public sealed partial class AppSettingsJsonContext : JsonSerializerContext;


