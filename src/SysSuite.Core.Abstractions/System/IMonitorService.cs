namespace SysSuite.Core.Abstractions.System;

public sealed record MonitorSample
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public double CpuUsagePercent { get; init; }
    public double MemoryUsedPercent { get; init; }
    public double DiskReadBytesPerSecond { get; init; }
    public double DiskWriteBytesPerSecond { get; init; }
    public double NetworkSentBytesPerSecond { get; init; }
    public double NetworkReceivedBytesPerSecond { get; init; }
}

/// <summary>采样时间窗。UI 只需在 1 分钟 / 5 分钟之间切换。</summary>
public enum MonitorWindow
{
    OneMinute = 0,
    FiveMinutes = 1,
}

/// <summary>
/// 实时性能监控（T1.4）。
/// 服务本身只管采样与历史留存，暂停/恢复与时间窗切换是状态操作，不改变底层采样线程生命周期。
/// </summary>
public interface IMonitorService : IDisposable
{
    /// <summary>新样本就绪。回调在采样线程触发，UI 侧必须自行封送到 Dispatcher。</summary>
    event EventHandler<MonitorSample>? SampleReady;

    /// <summary>采集是否已启动（与是否暂停无关）。</summary>
    bool IsRunning { get; }

    /// <summary>是否处于暂停状态；暂停期间不产生新样本，历史保留。</summary>
    bool IsPaused { get; }

    /// <summary>当前采样间隔（毫秒）。</summary>
    int SampleIntervalMilliseconds { get; }

    /// <summary>当前时间窗。</summary>
    MonitorWindow Window { get; }

    /// <summary>启动采集。重复调用是幂等的。</summary>
    void Start();

    /// <summary>停止采集并清空历史。</summary>
    void StopMonitoring();

    /// <summary>暂停采集，保留已积累的历史，供恢复后继续绘制曲线。</summary>
    void Pause();

    /// <summary>从暂停中恢复；未启动时等价于 <see cref="Start"/>。</summary>
    void ResumeMonitoring();

    /// <summary>切换时间窗。已有历史不重建，仅改变返回给 UI 的裁剪范围。</summary>
    void SetWindow(MonitorWindow window);

    /// <summary>取当前时间窗内的样本副本，按时间升序。返回的是快照，调用方可安全持有。</summary>
    IReadOnlyList<MonitorSample> GetHistory();
}
