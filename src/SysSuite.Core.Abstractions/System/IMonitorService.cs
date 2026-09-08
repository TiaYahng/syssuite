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

public interface IMonitorService : IDisposable
{
    event EventHandler<MonitorSample>? SampleReady;

    void Start();

    void StopMonitoring();
}
