using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.UI.Pages;

public partial class UninstallerPage
{
    private void OnSearchTextChanged(object sender, TextChangedEventArgs args)
    {
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplyView();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs args)
    {
        SearchBox.Clear();
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs args)
    {
        if (sender is not GridViewColumnHeader { Tag: string propertyName } || AppsList.ItemsSource is null)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(AppsList.ItemsSource);
        var current = view.SortDescriptions.FirstOrDefault();
        var direction = current.PropertyName == propertyName && current.Direction == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(propertyName, direction));
        view.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var rows = AppsList.Items.OfType<AppRow>().ToList();
        var totalSize = rows.Sum(row => row.SizeBytes > 0 ? row.SizeBytes : 0);
        CountText.Text = rows.Count == 0
            ? "没有程序"
            : $"{rows.Count} 个程序" + (totalSize > 0 ? $" · 共 {FormatSize(totalSize)}" : string.Empty);
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyView()
    {
        if (AppsList.ItemsSource is null)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(AppsList.ItemsSource);
        view.Filter = item => item is AppRow row && MatchesSearch(row);
        if (view.SortDescriptions.Count == 0)
        {
            view.SortDescriptions.Add(new SortDescription(nameof(AppRow.Name), ListSortDirection.Ascending));
        }

        view.Refresh();
    }

    private bool MatchesSearch(AppRow row)
    {
        var query = SearchBox.Text.Trim();
        return query.Length == 0 || row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatInstallDate(string? value)
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

    private static string FormatSize(long? size)
    {
        if (size is not > 0)
        {
            return "—";
        }

        return size switch
        {
            >= 1024L * 1024 * 1024 => $"{size / (double)(1024L * 1024 * 1024):N1} GB",
            >= 1024L * 1024 => $"{size / (double)(1024L * 1024):N1} MB",
            >= 1024 => $"{size / 1024d:N1} KB",
            _ => $"{size} B"
        };
    }

    private sealed class AppRow : INotifyPropertyChanged
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

        private ImageSource? icon;

        public ImageSource? Icon
        {
            get => icon;
            set
            {
                if (ReferenceEquals(icon, value)) return;
                icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string AutomationText => $"{Name}，{Publisher}，{SourceLabel}";
    }
}
