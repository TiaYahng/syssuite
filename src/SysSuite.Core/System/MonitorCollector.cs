using System.Globalization;
using System.Management;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 监控采集器：持有并复用 WMI 查询对象。
///
/// 与旧实现的关键区别是 <see cref="ManagementObjectSearcher"/> 只创建一次。
/// 每次采样新建 searcher 意味着一次完整的 COM 连接 + WQL 解析 + 集合释放，
/// 2 秒一次在高频路径上开销可观；复用后单次采样只剩 <c>Get()</c> 的对象枚举。
///
/// 所有 WMI 类型在此处集中封装，Core 其余代码不直接接触 <c>System.Management</c>。
/// </summary>
internal sealed class MonitorCollector : IDisposable
{
    private readonly ManagementObjectSearcher cpuSearcher = new(
        "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");

    private readonly ManagementObjectSearcher memorySearcher = new(
        "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");

    private readonly ManagementObjectSearcher diskSearcher = new(
        "SELECT DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");

    private readonly ManagementObjectSearcher networkSearcher = new(
        "SELECT BytesReceivedPersec, BytesSentPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");

    public MonitorReading Read()
    {
        return new MonitorReading(
            ReadCpu(),
            ReadMemory(),
            ReadDiskRead(),
            ReadDiskWrite(),
            ReadNetworkReceived(),
            ReadNetworkSent());
    }

    private double ReadCpu()
    {
        using var results = cpuSearcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var raw = item["PercentProcessorTime"]?.ToString();
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var usage))
                {
                    return Math.Clamp(usage, 0, 100);
                }
            }
        }

        return 0;
    }

    private double ReadMemory()
    {
        using var results = memorySearcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var total = ToDouble(item["TotalVisibleMemorySize"]);
                var free = ToDouble(item["FreePhysicalMemory"]);
                return total <= 0 ? 0 : Math.Clamp((total - free) * 100 / total, 0, 100);
            }
        }

        return 0;
    }

    private double ReadDiskRead() => ReadDiskProperty("DiskReadBytesPersec");

    private double ReadDiskWrite() => ReadDiskProperty("DiskWriteBytesPersec");

    private double ReadDiskProperty(string propertyName)
    {
        using var results = diskSearcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                return Math.Max(0, ToDouble(item[propertyName]));
            }
        }

        return 0;
    }

    private double ReadNetworkReceived() => ReadNetworkProperty("BytesReceivedPersec");

    private double ReadNetworkSent() => ReadNetworkProperty("BytesSentPersec");

    private double ReadNetworkProperty(string propertyName)
    {
        using var results = networkSearcher.Get();
        var sum = 0d;
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                sum += Math.Max(0, ToDouble(item[propertyName]));
            }
        }

        return sum;
    }

    private static double ToDouble(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    public void Dispose()
    {
        cpuSearcher.Dispose();
        memorySearcher.Dispose();
        diskSearcher.Dispose();
        networkSearcher.Dispose();
    }
}

/// <summary>单次采样的原始读数；不含时间戳，时间戳由服务层统一打。</summary>
internal readonly record struct MonitorReading(
    double CpuPercent,
    double MemoryPercent,
    double DiskReadBytesPerSecond,
    double DiskWriteBytesPerSecond,
    double NetworkReceivedBytesPerSecond,
    double NetworkSentBytesPerSecond);
