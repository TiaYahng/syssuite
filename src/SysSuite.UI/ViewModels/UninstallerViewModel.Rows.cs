using System.Globalization;
using System.Windows.Media;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.UI.ViewModels;

public sealed partial class UninstallerViewModel
{
    private readonly List<AppRow> rows = [];
    private readonly System.Windows.Threading.Dispatcher uiContext = System.Windows.Threading.Dispatcher.CurrentDispatcher;

    public AppRecord? SelectedApp { get; private set; }

    public void SelectApp(AppRecord? app)
    {
        SelectedApp = app;
        OnPropertyChanged(nameof(SelectedApp));
        OnPropertyChanged(nameof(SelectedRegistryPath));
        OnPropertyChanged(nameof(SelectedInstallDirectory));
    }

    private void SetRows(List<AppRow> newRows)
    {
        rows.Clear();
        rows.AddRange(newRows);
        OnPropertyChanged(nameof(Rows));
    }

    internal static string FormatInstallDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
            || (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value;
    }

    internal static string FormatSize(long? size)
    {
        if (size is not > 0)
        {
            return "—";
        }

        return size switch
        {
            >= 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{size / (double)(1024L * 1024 * 1024):N1} GB"),
            >= 1024L * 1024 => string.Create(CultureInfo.InvariantCulture, $"{size / (double)(1024L * 1024):N1} MB"),
            >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{size / 1024d:N1} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{size} B")
        };
    }

    /// <summary>
    /// 列表行。既做展示（字符串已格式化）又持有原始 <see cref="AppRecord"/>，
    /// 这样命令可以直接拿到域对象而不必反查。
    /// </summary>
    public sealed class AppRow : ObservableObject
    {
        public AppRow(AppRecord app)
        {
            App = app;
            Name = app.Name;
            Publisher = string.IsNullOrWhiteSpace(app.Publisher) ? "—" : app.Publisher;
            Version = string.IsNullOrWhiteSpace(app.Version) ? "—" : app.Version;
            InstalledOn = FormatInstallDate(app.InstallDate);
            SizeText = FormatSize(app.Size);
            SizeBytes = app.Size ?? -1;
            SourceLabel = app.Source switch
            {
                AppSource.Msi => "MSI",
                AppSource.Registry => "注册表",
                AppSource.Store => "Store",
                _ => "未知"
            };
            SearchText = string.Join(' ', app.Name, Publisher, Version, InstalledOn, app.InstallDir, SourceLabel);
        }

        public AppRecord App { get; }

        public string Name { get; }

        public string Publisher { get; }

        public string Version { get; }

        public string InstalledOn { get; }

        public string SizeText { get; }

        public long SizeBytes { get; }

        public string SourceLabel { get; }

        public string SearchText { get; }

        public string AutomationText => $"{Name}，{Publisher}，{SourceLabel}";

        private ImageSource? icon;

        public ImageSource? Icon
        {
            get => icon;
            set => SetProperty(ref icon, value);
        }
    }
}
