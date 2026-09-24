using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 磁盘清理页面（D1 后）。
///
/// 只剩显示层：把 ViewModel 的进度/状态/列表画到控件，把点击转成 ViewModel 调用，
/// 以及实现 <see cref="ICleanerInteractions"/> 的确认框。扫描、清理、撤销、勾选状态全在 ViewModel。
/// </summary>
public partial class DiskCleanerView : UserControl, IDisposable
{
    private readonly CleanerViewModel viewModel;

    public DiskCleanerView()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        viewModel = new CleanerViewModel(
            services.GetRequiredService<IDiskInspectionService>(),
            new CleanerInteractions(),
            services.GetService<ISettingsService>());
        viewModel.StatusChanged += OnStatusChanged;
        viewModel.ListsChanged += OnListsChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        InitializeSlimming(services);
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) => await viewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    public void Dispose()
    {
        viewModel.StatusChanged -= OnStatusChanged;
        viewModel.ListsChanged -= OnListsChanged;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.Dispose();
        DisposeSlimming();
        GC.SuppressFinalize(this);
    }

    private void OnListsChanged(object? sender, EventArgs args)
    {
        DriveList.ItemsSource = viewModel.Drives;
        CategoryList.ItemsSource = viewModel.Categories;
        ItemList.ItemsSource = viewModel.VisibleItems;

        // 重建分类后 ViewModel 会把选中项回落到「全部」，这里同步 ListBox 以免显示错位
        if (!ReferenceEquals(CategoryList.SelectedItem, viewModel.SelectedCategory))
        {
            CategoryList.SelectedItem = viewModel.SelectedCategory;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(CleanerViewModel.ScanPercent):
                ScanProgress.Value = viewModel.ScanPercent;
                ScanProgress.IsIndeterminate = viewModel.IsScanIndeterminate;
                break;
            case nameof(CleanerViewModel.ScanPhaseText):
                ProgressPhaseText.Text = viewModel.ScanPhaseText;
                break;
            case nameof(CleanerViewModel.ScanDetailText):
                ProgressDetailText.Text = viewModel.ScanDetailText;
                break;
            case nameof(CleanerViewModel.SelectAllButtonText):
                SelectAllButton.Content = viewModel.SelectAllButtonText;
                break;
            case nameof(CleanerViewModel.CanClean):
                CleanButton.IsEnabled = viewModel.CanClean;
                break;
            case nameof(CleanerViewModel.CanSelectAll):
                SelectAllButton.IsEnabled = viewModel.CanSelectAll;
                break;
            case nameof(CleanerViewModel.BusyText):
                SetStatusText(viewModel.BusyText);
                break;
        }

        ScanButton.IsEnabled = !viewModel.IsBusy;
        CancelScanButton.Visibility = viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStatusChanged(object? sender, CleanerStatusEventArgs args)
    {
        StatusText.Inlines.Clear();
        foreach (var (text, resourceKey) in args.Segments)
        {
            AddStatusRun(text, resourceKey);
        }
    }

    private void SetStatusText(string text)
    {
        StatusText.Inlines.Clear();
        AddStatusRun(text, "App.SubtleText");
    }

    private void AddStatusRun(string text, string resourceKey)
    {
        StatusText.Inlines.Add(new Run(text)
        {
            Foreground = TryFindResource(resourceKey) as Brush ?? Foreground
        });
    }

    private async void OnScanClick(object sender, RoutedEventArgs args) => await viewModel.ScanAsync();

    private async void OnCleanClick(object sender, RoutedEventArgs args) => await viewModel.CleanAsync();

    private async void OnRestoreClick(object sender, RoutedEventArgs args) => await viewModel.RestoreLatestAsync();

    private void OnCancelScanClick(object sender, RoutedEventArgs args) => viewModel.CancelCommand.Execute(null);

    private void OnSelectAllClick(object sender, RoutedEventArgs args) => viewModel.ToggleSelectAll();

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs args)
        => viewModel.SelectCategory(CategoryList.SelectedItem as DiskCategoryRow);

    private void OnGroupChecked(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox { Tag: DiskGroup group })
        {
            viewModel.ToggleGroup(group);
        }
    }

    private void OnOpenPathClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: DiskCleanerRow row })
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(row.Path)!);
        }
        catch (Win32Exception exception)
        {
            SetStatusText(exception.Message);
        }
    }
}
