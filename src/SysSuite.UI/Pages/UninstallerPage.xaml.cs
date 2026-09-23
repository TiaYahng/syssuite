using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Abstractions.Settings;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 卸载器页面（D1 后）。
///
/// 这里只剩"显示层"职责：把用户手势转成 ViewModel 调用、把 ViewModel 状态画到控件、
/// 实现 <see cref="IUninstallerInteractions"/> 的弹窗/对话框/剪贴板副作用，以及排序与搜索
/// 这类纯视图行为（它们操作的是 CollectionView，不属于业务）。没有枚举、没有卸载、没有 IO。
/// </summary>
public partial class UninstallerPage : UserControl, IDisposable
{
    private readonly UninstallerViewModel viewModel;

    public UninstallerPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        viewModel = new UninstallerViewModel(
            services.GetRequiredService<IUninstallEnumerationService>(),
            services.GetRequiredService<IIconCacheService>(),
            services.GetRequiredService<IUninstallService>(),
            services.GetRequiredService<IForceDeleteService>(),
            services.GetRequiredService<IAppChangeMonitor>(),
            services.GetRequiredService<ISettingsService>(),
            new UninstallerInteractions(this));
        viewModel.IconProgressChanged += OnIconProgressChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        await viewModel.ActivateAsync();
        SyncMonitorState();
        RefreshStatus();
        UpdateSummary();
        UpdateCommandStates();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        viewModel.IconProgressChanged -= OnIconProgressChanged;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.Dispose();
    }

    public void Dispose()
    {
        viewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UninstallerViewModel.Rows))
        {
            AppsList.ItemsSource = viewModel.Rows;
            ApplyView();
            UpdateSummary();
            UpdateCommandStates();
        }

        RefreshStatus();
    }

    private void OnIconProgressChanged(object? sender, IconProgressEventArgs args)
        => OperationProgress.Visibility = args.Current >= args.Total ? Visibility.Collapsed : Visibility.Visible;

    private void RefreshStatus()
    {
        if (StatusText.Text != viewModel.BusyText)
        {
            StatusText.Text = viewModel.BusyText;
        }

        RefreshButton.IsEnabled = !viewModel.IsBusy;
        OperationProgress.IsIndeterminate = viewModel.IsBusy && OperationProgress.Visibility == Visibility.Visible;
    }

    private void SyncMonitorState()
    {
        MonitorBadge.Text = viewModel.MonitorBadgeText;
        MonitorButton.Content = viewModel.MonitorButtonText;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs args) => viewModel.RefreshCommand.Execute(null);

    private async void OnUninstallClick(object sender, RoutedEventArgs args) => await viewModel.UninstallSelectedAsync();

    private async void OnForceClick(object sender, RoutedEventArgs args) => await viewModel.ForceDeleteSelectedAsync();

    private async void OnOpenInstallFolderClick(object sender, RoutedEventArgs args) => await viewModel.OpenInstallFolderAsync();

    private async void OnCopyRegistryPathClick(object sender, RoutedEventArgs args) => await viewModel.CopyRegistryPathAsync();

    private async void OnCopyDetailsClick(object sender, RoutedEventArgs args) => await viewModel.CopyDetailsAsync();

    private async void OnOpenOnlineSearchClick(object sender, RoutedEventArgs args) => await viewModel.OpenOnlineSearchAsync();

    private async void OnExportHtmlClick(object sender, RoutedEventArgs args) => await viewModel.ExportHtmlReportAsync();

    private void OnMonitorClick(object sender, RoutedEventArgs args)
    {
        viewModel.ToggleMonitoring();
        SyncMonitorState();
    }

    private void OnAppsListSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        viewModel.SelectApp(AppsList.SelectedItem as UninstallerViewModel.AppRow is { } row ? row.App : null);
        UpdateCommandStates();
    }

    private void OnAppsListDoubleClick(object sender, MouseButtonEventArgs args) => _ = viewModel.OpenInstallFolderAsync();

    private void UpdateCommandStates()
    {
        // 顶部已移除「卸载」按钮（右键菜单保留），这里只维护「强制删除」的可用性。
        ForceButton.IsEnabled = viewModel.SelectedApp is not null && !viewModel.IsBusy;
    }

    // ── 以下为纯视图行为：操作 CollectionView 的过滤与排序，不属于业务逻辑 ──

    private void OnSearchTextChanged(object sender, TextChangedEventArgs args)
    {
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplyView();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs args) => SearchBox.Clear();

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
        var rows = AppsList.Items.OfType<UninstallerViewModel.AppRow>().ToList();
        var totalSize = rows.Sum(row => row.SizeBytes > 0 ? row.SizeBytes : 0);
        CountText.Text = rows.Count == 0
            ? "没有程序"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{rows.Count} 个程序") + (totalSize > 0 ? $" · 共 {UninstallerViewModel.FormatSize(totalSize)}" : string.Empty);
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyView()
    {
        if (AppsList.ItemsSource is null)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(AppsList.ItemsSource);
        view.Filter = item => item is UninstallerViewModel.AppRow row && MatchesSearch(row);
        if (view.SortDescriptions.Count == 0)
        {
            view.SortDescriptions.Add(new SortDescription(nameof(UninstallerViewModel.AppRow.Name), ListSortDirection.Ascending));
        }

        view.Refresh();
    }

    private bool MatchesSearch(UninstallerViewModel.AppRow row)
    {
        var query = SearchBox.Text.Trim();
        return query.Length == 0 || row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
