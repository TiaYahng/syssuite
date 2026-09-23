using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

/// <summary>
/// 实时监控折线图（T1.4）。手绘 Canvas 而非引入图表库：
/// 依赖零增加，且能精确控制中文字体与主题色。
///
/// 右侧纵轴固定 0~100（CPU/内存百分比）；磁盘与网络量级差异过大，只以曲线形状示意，
/// 不标数值，避免同一张图上出现两套不可比的刻度。
/// </summary>
public partial class SystemInfoPage
{
    private static readonly string[] TrendColors =
    [
        "#4C8DF6", // CPU
        "#E8A33D", // 内存
        "#5FB878", // 磁盘读取
        "#C678DD", // 磁盘写入
    ];

    private const double TrendLeftPadding = 4;
    private const double TrendRightPadding = 46;
    private const double TrendTopPadding = 6;
    private const double TrendBottomPadding = 14;

    private void OnTrendCanvasSizeChanged(object sender, SizeChangedEventArgs args) => RedrawTrend();

    private void RedrawTrend()
    {
        var width = TrendCanvas.ActualWidth;
        var height = TrendCanvas.ActualHeight;
        if (width <= TrendLeftPadding + TrendRightPadding + 4 || height <= TrendTopPadding + TrendBottomPadding + 4)
        {
            return;
        }

        TrendCanvas.Children.Clear();

        var plotWidth = width - TrendLeftPadding - TrendRightPadding;
        var plotHeight = height - TrendTopPadding - TrendBottomPadding;
        var top = TrendTopPadding;
        var bottom = TrendTopPadding + plotHeight;

        var gridBrush = TryFindResource("App.Border") as Brush ?? Brushes.LightGray;
        var labelBrush = TryFindResource("App.SubtleText") as Brush ?? Brushes.Gray;

        // 0% / 50% / 100% 横网格
        for (var i = 0; i <= 2; i++)
        {
            var ratio = i / 2d;
            var y = bottom - (plotHeight * ratio);
            TrendCanvas.Children.Add(new Line
            {
                X1 = TrendLeftPadding,
                X2 = TrendLeftPadding + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = i == 0 ? null : [3, 3],
            });

            var label = new TextBlock
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"{ratio * 100:F0}%"),
                FontSize = 9,
                Foreground = labelBrush,
            };
            Canvas.SetLeft(label, TrendLeftPadding + plotWidth + 4);
            Canvas.SetTop(label, y - 6);
            TrendCanvas.Children.Add(label);
        }

        var samples = viewModel.GetHistory();
        if (samples.Count < 2)
        {
            var hint = new TextBlock
            {
                Text = viewModel.IsMonitoringPaused ? "已暂停" : "正在采集数据...",
                FontSize = 10,
                Foreground = labelBrush,
            };
            Canvas.SetLeft(hint, TrendLeftPadding + 6);
            Canvas.SetTop(hint, top + (plotHeight / 2) - 8);
            TrendCanvas.Children.Add(hint);
            return;
        }

        // 横轴按真实时间跨度映射，这样暂停造成的空档会体现为曲线上的断点位置偏移
        var first = samples[0].TimestampUtc;
        var last = samples[^1].TimestampUtc;
        var spanSeconds = Math.Max((last - first).TotalSeconds, 1);

        DrawSeries(samples, sample => sample.CpuUsagePercent, 100, TrendColors[0], first, spanSeconds, plotWidth, plotHeight);
        DrawSeries(samples, sample => sample.MemoryUsedPercent, 100, TrendColors[1], first, spanSeconds, plotWidth, plotHeight);

        // 磁盘与网络共用一个自适应峰值，只画形状
        var diskPeak = 0d;
        foreach (var sample in samples)
        {
            diskPeak = Math.Max(diskPeak, Math.Max(sample.DiskReadBytesPerSecond, sample.DiskWriteBytesPerSecond));
        }

        if (diskPeak > 0)
        {
            DrawSeries(samples, sample => sample.DiskReadBytesPerSecond, diskPeak, TrendColors[2], first, spanSeconds, plotWidth, plotHeight);
            DrawSeries(samples, sample => sample.DiskWriteBytesPerSecond, diskPeak, TrendColors[3], first, spanSeconds, plotWidth, plotHeight);
        }

        DrawLegend(labelBrush, top);
    }

    private void DrawSeries(
        IReadOnlyList<MonitorSample> samples,
        Func<MonitorSample, double> selector,
        double maxValue,
        string color,
        DateTimeOffset first,
        double spanSeconds,
        double plotWidth,
        double plotHeight)
    {
        if (maxValue <= 0)
        {
            return;
        }

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();

        var figure = new PathFigure { IsClosed = false, IsFilled = false };
        var added = false;

        foreach (var sample in samples)
        {
            var x = TrendLeftPadding + (plotWidth * ((sample.TimestampUtc - first).TotalSeconds / spanSeconds));
            var ratio = Math.Clamp(selector(sample) / maxValue, 0, 1);
            var y = TrendTopPadding + (plotHeight * (1 - ratio));

            if (added)
            {
                figure.Segments.Add(new LineSegment(new Point(x, y), true));
            }
            else
            {
                figure.StartPoint = new Point(x, y);
                added = true;
            }
        }

        if (!added)
        {
            return;
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        TrendCanvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = 1.4,
            StrokeLineJoin = PenLineJoin.Round,
        });
    }

    private void DrawLegend(Brush labelBrush, double top)
    {
        string[] names = ["CPU", "内存", "读", "写"];
        var offset = TrendLeftPadding;

        for (var i = 0; i < names.Length; i++)
        {
            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(TrendColors[i])),
                RadiusX = 1.5,
                RadiusY = 1.5,
            };
            Canvas.SetLeft(swatch, offset);
            Canvas.SetTop(swatch, top);
            TrendCanvas.Children.Add(swatch);

            var label = new TextBlock
            {
                Text = names[i],
                FontSize = 9,
                Foreground = labelBrush,
            };
            Canvas.SetLeft(label, offset + 10);
            Canvas.SetTop(label, top - 3);
            TrendCanvas.Children.Add(label);

            offset += 10 + (names[i].Length * 10) + 8;
        }
    }
}
