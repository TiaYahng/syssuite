using System.Management;
using System.Globalization;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed class PerformanceMonitorService : IMonitorService
{
    private const int SampleIntervalMilliseconds = 2000;
    private Timer? timer;

    public event EventHandler<MonitorSample>? SampleReady;

    public void Start()
    {
        if (timer is not null)
        {
            return;
        }

        timer = new Timer(_ => Capture(), null, 0, SampleIntervalMilliseconds);
    }

    public void StopMonitoring()
    {
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
        timer?.Dispose();
        timer = null;
    }

    private void Capture()
    {
        try
        {
            SampleReady?.Invoke(this, new MonitorSample
            {
                CpuUsagePercent = GetCpuUsage(),
                MemoryUsedPercent = GetMemoryUsage(),
                DiskReadBytesPerSecond = GetDiskValue("DiskReadBytesPersec"),
                DiskWriteBytesPerSecond = GetDiskValue("DiskWriteBytesPersec"),
                NetworkSentBytesPerSecond = GetNetworkValue("BytesSentPersec"),
                NetworkReceivedBytesPerSecond = GetNetworkValue("BytesReceivedPersec")
            });
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static double GetCpuUsage()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
        var value = searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["PercentProcessorTime"]?.ToString();
        return Math.Clamp(double.TryParse(value, out var usage) ? usage : 0, 0, 100);
    }

    private static double GetMemoryUsage()
    {
        using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
        var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        if (item is null)
        {
            return 0;
        }

        var total = Convert.ToDouble(item["TotalVisibleMemorySize"], CultureInfo.InvariantCulture);
        var free = Convert.ToDouble(item["FreePhysicalMemory"], CultureInfo.InvariantCulture);
        return total <= 0 ? 0 : Math.Clamp((total - free) * 100 / total, 0, 100);
    }

    private static double GetDiskValue(string propertyName)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT {propertyName} FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
        var value = searcher.Get().Cast<ManagementObject>().FirstOrDefault()?[propertyName]?.ToString();
        return double.TryParse(value, out var rate) && rate > 0 ? rate : 0;
    }

    private static double GetNetworkValue(string propertyName)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT {propertyName} FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
        return searcher.Get().Cast<ManagementObject>().Sum(item =>
        {
            var value = item[propertyName]?.ToString();
            return double.TryParse(value, out var rate) && rate > 0 ? rate : 0;
        });
    }

    public void Dispose() => StopMonitoring();
}
