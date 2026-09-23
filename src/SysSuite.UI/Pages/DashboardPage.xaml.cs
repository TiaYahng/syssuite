using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 仪表盘页面（D1 后）。
///
/// ViewModel 负责取数与文本化；这里负责把采样画成弧线（依赖控件几何，属展示逻辑）
/// 以及平滑滚轮这类纯视觉行为。
/// </summary>
public partial class DashboardPage : UserControl, IDisposable
{
    private static readonly DependencyProperty ContentVerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "ContentVerticalOffset",
        typeof(double),
        typeof(DashboardPage),
        new PropertyMetadata(0d, OnContentVerticalOffsetChanged));

    private readonly DashboardViewModel viewModel;

    public DashboardPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        viewModel = new DashboardViewModel(
            services.GetRequiredService<IHardwareInfoService>(),
            services.GetRequiredService<IMonitorService>());
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.SampleReady += OnSampleReady;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) => await viewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    public void Dispose()
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.SampleReady -= OnSampleReady;
        viewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(DashboardViewModel.CpuSummary):
                CpuSummaryText.Text = viewModel.CpuSummary;
                break;
            case nameof(DashboardViewModel.MemorySummary):
                MemorySummaryText.Text = viewModel.MemorySummary;
                break;
            case nameof(DashboardViewModel.DiskSummary):
                DiskSummaryText.Text = viewModel.DiskSummary;
                break;
            case nameof(DashboardViewModel.GpuSummary):
                GpuSummaryText.Text = viewModel.GpuSummary;
                break;
            case nameof(DashboardViewModel.BusyText):
                StatusText.Text = viewModel.BusyText;
                break;
        }
    }

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

    private void OnNativeCheckClick(object sender, RoutedEventArgs args) => viewModel.RunNativeSelfCheck();

    private void OnSampleReady(object? sender, MonitorSample sample)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CpuPercentText.Text = $"{sample.CpuUsagePercent:F1}%";
            MemoryPercentText.Text = $"{sample.MemoryUsedPercent:F1}%";
            SetArc(CpuArc, sample.CpuUsagePercent);
            SetArc(MemoryArc, sample.MemoryUsedPercent);

            DiskPercentText.Text = $"{DashboardViewModel.RatePercent(Math.Max(sample.DiskReadBytesPerSecond, sample.DiskWriteBytesPerSecond)):F1}%";
            SetArc(DiskArc, DashboardViewModel.RatePercent(Math.Max(sample.DiskReadBytesPerSecond, sample.DiskWriteBytesPerSecond)));

            var networkPeak = Math.Max(sample.NetworkReceivedBytesPerSecond, sample.NetworkSentBytesPerSecond);
            NetworkPercentText.Text = $"{DashboardViewModel.RatePercent(networkPeak):F1}%";
            SetArc(NetworkArc, DashboardViewModel.RatePercent(networkPeak));

            DiskReadText.Text = DashboardViewModel.FormatBytesPerSecond(sample.DiskReadBytesPerSecond);
            DiskWriteText.Text = DashboardViewModel.FormatBytesPerSecond(sample.DiskWriteBytesPerSecond);
            NetworkText.Text = $"{DashboardViewModel.FormatBytesPerSecond(sample.NetworkReceivedBytesPerSecond)} / {DashboardViewModel.FormatBytesPerSecond(sample.NetworkSentBytesPerSecond)}";
        });
    }

    private static void SetArc(System.Windows.Shapes.Ellipse arc, double percent)
    {
        var ratio = Math.Clamp(percent / 100d, 0d, 1d);
        arc.StrokeDashArray = new System.Windows.Media.DoubleCollection([ratio * 1.57, 10]);
    }
}
