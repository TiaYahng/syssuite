using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 系统信息页面（D1 后）。
///
/// ViewModel 负责取数、传感器与健康判定；这里负责把结果画到控件、
/// 以及导出对话框与滚动动画这类纯视图行为。传感器与健康的绘制拆到
/// <c>SystemInfoPage.Monitor.cs</c> 与 <c>SystemInfoPage.Health.cs</c>，避免单文件超行数门禁。
/// </summary>
public partial class SystemInfoPage : UserControl, IDisposable
{
    private static readonly DependencyProperty ContentVerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "ContentVerticalOffset",
        typeof(double),
        typeof(SystemInfoPage),
        new PropertyMetadata(0d, OnContentVerticalOffsetChanged));

    private readonly SystemInfoViewModel viewModel;

    public SystemInfoPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        viewModel = new SystemInfoViewModel(
            services.GetRequiredService<IHardwareInfoService>(),
            services.GetRequiredService<IMonitorService>(),
            services.GetRequiredService<IReportExporter>(),
            services.GetRequiredService<ISensorService>(),
            services.GetRequiredService<ISmartService>(),
            services.GetRequiredService<IElevationService>());
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.HardwareInfoUpdated += OnHardwareInfoUpdated;
        viewModel.SensorsUpdated += OnSensorsUpdated;
        viewModel.DriveHealthUpdated += OnDriveHealthUpdated;
        viewModel.SampleReady += OnSampleReady;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        await viewModel.ActivateAsync();
        UpdatePauseResumeButton();
        UpdateWindowButtons();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    public void Dispose()
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.HardwareInfoUpdated -= OnHardwareInfoUpdated;
        viewModel.SensorsUpdated -= OnSensorsUpdated;
        viewModel.DriveHealthUpdated -= OnDriveHealthUpdated;
        viewModel.SampleReady -= OnSampleReady;
        viewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemInfoViewModel.BusyText))
        {
            StatusText.Text = viewModel.BusyText;
        }
    }

    private void OnHardwareInfoUpdated(object? sender, EventArgs args)
    {
        CpuText.Text = viewModel.CpuText;
        MotherboardText.Text = viewModel.MotherboardText;
        BiosText.Text = viewModel.BiosText;
        MemoryText.Text = viewModel.MemoryText;
        OsText.Text = viewModel.OsText;
        GraphicsList.ItemsSource = viewModel.GraphicsCards;
        DriveList.ItemsSource = viewModel.Drives;

        StorageList.Items.Clear();
        foreach (var item in viewModel.StorageItems)
        {
            StorageList.Items.Add(item);
        }

        NetworkList.Items.Clear();
        foreach (var item in viewModel.NetworkItems)
        {
            NetworkList.Items.Add(item);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await viewModel.RefreshHardwareAsync();

    private void OnContentScrollPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (ContentScroll.ScrollableHeight <= 0)
        {
            return;
        }

        args.Handled = true;
        var currentTarget = (double)ContentScroll.GetValue(ContentVerticalOffsetProperty);
        var target = Math.Clamp(currentTarget - args.Delta, 0, ContentScroll.ScrollableHeight);
        var animation = new DoubleAnimation(ContentScroll.VerticalOffset, target, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ContentScroll.BeginAnimation(ContentVerticalOffsetProperty, animation);
    }

    private static void OnContentVerticalOffsetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)args.NewValue);
        }
    }

    private void OnOpenEnvironmentVariablesClick(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = "sysdm.cpl,EditEnvironmentVariables",
                UseShellExecute = true
            });
        }
        catch (Win32Exception exception)
        {
            MessageBox.Show(exception.Message, "无法打开环境变量", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnExportReportClick(object sender, RoutedEventArgs args)
    {
        // Filter 顺序必须与 ReportFormats.All 保持一致，才能直接由 FilterIndex 映射到格式
        var dialog = new SaveFileDialog
        {
            Title = "导出系统信息报告",
            FileName = SystemInfoViewModel.BuildDefaultReportName(ReportFormat.Html),
            Filter = "文本报告 (*.txt)|*.txt|网页报告 (*.html)|*.html|JSON 数据 (*.json)|*.json",
            FilterIndex = 2,
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var format = ReportFormats.All[Math.Clamp(dialog.FilterIndex - 1, 0, ReportFormats.All.Count - 1)];
        await viewModel.ExportReportAsync(format, dialog.FileName);
    }

    private async void OnSampleReady(object? sender, MonitorSample sample)
    {
        CpuUsageText.Text = $"{sample.CpuUsagePercent:F1}%";
        MemoryUsageText.Text = $"{sample.MemoryUsedPercent:F1}%";
        DiskReadText.Text = SystemInfoViewModel.FormatBytesPerSecond(sample.DiskReadBytesPerSecond);
        DiskWriteText.Text = SystemInfoViewModel.FormatBytesPerSecond(sample.DiskWriteBytesPerSecond);
        NetworkText.Text = $"{SystemInfoViewModel.FormatBytesPerSecond(sample.NetworkReceivedBytesPerSecond)} / {SystemInfoViewModel.FormatBytesPerSecond(sample.NetworkSentBytesPerSecond)}";

        RedrawTrend();

        // 采样回调不在 UI 线程，节流计数与传感器刷新都要切回来
        await Dispatcher.InvokeAsync(async () =>
        {
            if (viewModel.ShouldRefreshSensorsOnTick())
            {
                await viewModel.RefreshSensorsAsync();
            }
        });
    }
}
