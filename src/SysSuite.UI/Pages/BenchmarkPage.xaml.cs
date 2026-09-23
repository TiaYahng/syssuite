using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Settings;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

public partial class BenchmarkPage : UserControl
{
    private const int HistoryCapacity = 5;
    private const double MaxBarWidth = 320;

    private readonly IBenchmarkService benchmarkService;
    private readonly ISettingsService settingsService;
    private CancellationTokenSource? runCts;

    public BenchmarkPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        benchmarkService = services.GetRequiredService<IBenchmarkService>();
        settingsService = services.GetRequiredService<ISettingsService>();
        benchmarkService.ProgressChanged += OnProgressChanged;
        RefreshHistoryRows(LoadHistory());
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        benchmarkService.ProgressChanged -= OnProgressChanged;
        runCts?.Cancel();
    }

    private void OnQuickRunClick(object sender, RoutedEventArgs args) => _ = RunAsync(BenchmarkOptions.Quick);

    private void OnFullRunClick(object sender, RoutedEventArgs args) => _ = RunAsync(BenchmarkOptions.Full);

    private void OnCancelRunClick(object sender, RoutedEventArgs args)
    {
        runCts?.Cancel();
        CancelButton.IsEnabled = false;
        StageText.Text = "正在中止，等待当前项目收尾...";
    }

    private void OnProgressChanged(object? sender, BenchmarkProgress progress)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StageText.Text = progress.StageName;
            RunProgress.Value = progress.Percent;
            ProgressPercentText.Text = string.Create(CultureInfo.InvariantCulture, $"{progress.Percent:F0}%");
        });
    }

    private async Task RunAsync(BenchmarkOptions options)
    {
        SetRunningState(true);
        ResultPanel.Visibility = Visibility.Collapsed;
        RunProgress.Value = 0;
        ProgressPercentText.Text = string.Empty;
        StageText.Text = "准备中...";
        StatusText.Text = "跑分进行中，请勿关闭窗口。";

        using var cts = new CancellationTokenSource();
        runCts = cts;

        try
        {
            var effective = options with
            {
                IncludeDisk = options.IncludeDisk && IncludeDiskCheck.IsChecked == true,
                DiskScope = DiskWriteCheck.IsChecked == true
                    ? BenchmarkDiskScope.TemporaryFile
                    : BenchmarkDiskScope.ReadOnly,
            };

            var result = await benchmarkService.RunAsync(effective, null, cts.Token);
            if (!result.IsSuccess || result.Value is null)
            {
                StatusText.Text = $"跑分失败：{result.Message}";
                StageText.Text = "跑分未完成。";
                return;
            }

            ApplyReport(result.Value, cts.IsCancellationRequested);
        }
        finally
        {
            runCts = null;
            SetRunningState(false);
        }
    }

    private void SetRunningState(bool running)
    {
        QuickButton.IsEnabled = !running;
        FullButton.IsEnabled = !running;
        IncludeDiskCheck.IsEnabled = !running;
        DiskWriteCheck.IsEnabled = !running;
        CancelButton.IsEnabled = running;
    }

    private void ApplyReport(BenchmarkReport report, bool wasCancelled)
    {
        StatusText.Text = wasCancelled
            ? "跑分已中止，以下为已完成项目的结果。"
            : "跑分完成。";
        StageText.Text = wasCancelled ? "已中止。" : "全部项目已完成。";
        RunProgress.Value = wasCancelled ? RunProgress.Value : 100;
        ProgressPercentText.Text = wasCancelled ? "—" : "100%";

        MachineText.Text = report.MachineSummary;
        var composite = report.Composite;
        CompositeText.Text = composite is { } value
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : "--";

        var rows = new List<ScoreRow>(report.Scores.Count);
        var peak = 1d;
        foreach (var score in report.Scores)
        {
            peak = Math.Max(peak, score.Score);
        }

        foreach (var score in report.Scores)
        {
            rows.Add(new ScoreRow(
                score.Name,
                score.Succeeded ? score.Score.ToString("F0", CultureInfo.InvariantCulture) : "未完成",
                score.Detail,
                score.Succeeded ? new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xF6)) : new SolidColorBrush(Color.FromArgb(0x40, 0x8A, 0x8A, 0x8A)),
                score.Succeeded ? Math.Max(6, MaxBarWidth * (score.Score / peak)) : 0));
        }

        ScoreList.ItemsSource = rows;
        ResultPanel.Visibility = Visibility.Visible;

        if (!wasCancelled && composite is not null)
        {
            AppendHistory(report, composite.Value);
        }
    }

    private void AppendHistory(BenchmarkReport report, double composite)
    {
        var entries = LoadHistory();
        entries.Insert(0, new HistoryEntry(report.RunAt, report.MachineSummary, composite));

        while (entries.Count > HistoryCapacity)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        SaveHistory(entries);
        RefreshHistoryRows(entries);
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs args)
    {
        settingsService.Current.BenchmarkHistory = [];
        settingsService.SaveDebounced();
        RefreshHistoryRows([]);
    }

    private List<HistoryEntry> LoadHistory()
    {
        var entries = new List<HistoryEntry>();
        foreach (var item in settingsService.Current.BenchmarkHistory)
        {
            var parts = item.Split('|', 3);
            if (parts.Length != 3
                || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var runAt)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var composite))
            {
                continue;
            }

            entries.Add(new HistoryEntry(runAt, parts[1], composite));
        }

        return entries;
    }

    private void SaveHistory(List<HistoryEntry> entries)
    {
        var raw = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            raw.Add(string.Join('|',
                entry.RunAt.ToString("O", CultureInfo.InvariantCulture),
                entry.MachineSummary,
                entry.Composite.ToString("F0", CultureInfo.InvariantCulture)));
        }

        settingsService.Current.BenchmarkHistory = raw;
        settingsService.SaveDebounced();
    }

    private void RefreshHistoryRows(IReadOnlyList<HistoryEntry> entries)
    {
        var rows = new List<HistoryRow>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string deltaText;
            Brush deltaBrush;

            if (i == entries.Count - 1)
            {
                deltaText = "首次";
                deltaBrush = (Brush)FindResource("App.SubtleText");
            }
            else
            {
                var previous = entries[i + 1].Composite;
                var deltaPercent = previous <= 0 ? 0 : (entry.Composite - previous) / previous * 100;
                deltaText = string.Create(CultureInfo.InvariantCulture, $"{deltaPercent:+0.0;-0.0;0.0}%");
                deltaBrush = deltaPercent switch
                {
                    > 0.05 => new SolidColorBrush(Color.FromRgb(0x1E, 0x7B, 0x36)),
                    < -0.05 => new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E)),
                    _ => (Brush)FindResource("App.SubtleText"),
                };
            }

            rows.Add(new HistoryRow(
                entry.RunAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture),
                entry.MachineSummary,
                deltaText,
                entry.Composite.ToString("F0", CultureInfo.InvariantCulture),
                deltaBrush));
        }

        HistoryList.ItemsSource = rows;
    }

    private readonly record struct HistoryEntry(DateTimeOffset RunAt, string MachineSummary, double Composite);

    private sealed record ScoreRow(string Name, string ScoreText, string Detail, Brush BarBrush, double BarWidth);

    private sealed record HistoryRow(string RunAtText, string MachineText, string DeltaText, string CompositeText, Brush DeltaBrush);
}
