using System.Management;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 实时性能监控实现（T1.4）。
///
/// 设计要点：
///  1. WMI 查询对象在首次采样时**只创建一次**并全程复用。旧实现每次采样都新建
///     <c>ManagementObjectSearcher</c>，2 秒一次意味着每小时 1800 次 COM 连接建立/销毁，
///     在低配机上会明显拖慢系统——这也是 T1.4 验收里"连续 1 小时内存无增长"的主要风险点。
///  2. 采样与历史分离：环形缓冲只保留当前时间窗所需的最多样本数，不随运行时长增长。
///  3. 暂停通过短路采样实现，而不是销毁/重建 WMI 连接（重建成本远高于空转一次）。
/// </summary>
public sealed partial class PerformanceMonitorService : IMonitorService
{
    // 5 分钟窗 @2s 采样 = 150 个样本；环形缓冲按最大窗口分配，永不增长
    internal const int MaxHistoryCapacity = 512;

    private readonly object sync = new();
    private readonly MonitorSample?[] history = new MonitorSample?[MaxHistoryCapacity];

    private Timer? timer;
    private MonitorCollector? collector;
    private int historyCount;
    private int historyNext;
    private bool disposed;

    public event EventHandler<MonitorSample>? SampleReady;

    public bool IsRunning { get; private set; }

    public bool IsPaused { get; private set; }

    public int SampleIntervalMilliseconds => SampleIntervalMs;

    public MonitorWindow Window { get; private set; } = MonitorWindow.OneMinute;

    private const int SampleIntervalMs = 2000;

    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;
            IsPaused = false;
            timer = new Timer(_ => Capture(), null, 0, SampleIntervalMs);
        }
    }

    public void StopMonitoring()
    {
        lock (sync)
        {
            timer?.Change(Timeout.Infinite, Timeout.Infinite);
            timer?.Dispose();
            timer = null;
            IsRunning = false;
            IsPaused = false;
            ClearHistory();
            collector?.Dispose();
            collector = null;
        }
    }

    public void Pause()
    {
        lock (sync)
        {
            if (!IsRunning)
            {
                return;
            }

            IsPaused = true;
        }
    }

    public void ResumeMonitoring()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!IsRunning)
            {
                IsRunning = true;
                timer ??= new Timer(_ => Capture(), null, 0, SampleIntervalMs);
            }

            IsPaused = false;
        }
    }

    public void SetWindow(MonitorWindow window)
    {
        lock (sync)
        {
            Window = window;
            // 切到更小的窗口时立即裁剪，避免 UI 拿到超窗样本
            TrimToWindow();
        }
    }

    public IReadOnlyList<MonitorSample> GetHistory()
    {
        lock (sync)
        {
            return Snapshot();
        }
    }

    private void Capture()
    {
        MonitorSample sample;
        lock (sync)
        {
            if (!IsRunning || IsPaused || disposed)
            {
                return;
            }
        }

        try
        {
            // 连接复用：collector 懒创建且在服务生命周期内保持
            collector ??= new MonitorCollector();
            var reading = collector.Read();
            sample = new MonitorSample
            {
                CpuUsagePercent = reading.CpuPercent,
                MemoryUsedPercent = reading.MemoryPercent,
                DiskReadBytesPerSecond = reading.DiskReadBytesPerSecond,
                DiskWriteBytesPerSecond = reading.DiskWriteBytesPerSecond,
                NetworkSentBytesPerSecond = reading.NetworkSentBytesPerSecond,
                NetworkReceivedBytesPerSecond = reading.NetworkReceivedBytesPerSecond
            };
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            // 采样失败不得中断监控循环；单次异常直接丢弃该周期
            return;
        }

        lock (sync)
        {
            if (!IsRunning || IsPaused || disposed)
            {
                return;
            }

            Append(sample);
        }

        SampleReady?.Invoke(this, sample);
    }

    private void Append(MonitorSample sample)
    {
        history[historyNext] = sample;
        historyNext = (historyNext + 1) % MaxHistoryCapacity;
        if (historyCount < MaxHistoryCapacity)
        {
            historyCount++;
        }

        TrimToWindow();
    }

    /// <summary>丢弃时间窗之外的样本，保证 GetHistory 返回量恒定为窗口内的样本数。</summary>
    private void TrimToWindow()
    {
        var cutoff = DateTimeOffset.UtcNow - WindowDuration;
        while (historyCount > 0)
        {
            var oldestIndex = (historyNext - historyCount + MaxHistoryCapacity) % MaxHistoryCapacity;
            if (history[oldestIndex] is { } oldest && oldest.TimestampUtc < cutoff)
            {
                history[oldestIndex] = null;
                historyCount--;
            }
            else
            {
                break;
            }
        }
    }

    private List<MonitorSample> Snapshot()
    {
        if (historyCount == 0)
        {
            return [];
        }

        var result = new List<MonitorSample>(historyCount);
        for (var offset = 0; offset < historyCount; offset++)
        {
            var index = (historyNext - historyCount + offset + MaxHistoryCapacity) % MaxHistoryCapacity;
            if (history[index] is { } sample)
            {
                result.Add(sample);
            }
        }

        return result;
    }

    private void ClearHistory()
    {
        Array.Clear(history);
        historyCount = 0;
        historyNext = 0;
    }

    private TimeSpan WindowDuration => Window == MonitorWindow.FiveMinutes
        ? TimeSpan.FromMinutes(5)
        : TimeSpan.FromMinutes(1);

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        StopMonitoring();
    }
}
