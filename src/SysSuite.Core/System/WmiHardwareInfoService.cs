using System.Management;
using System.Globalization;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed class WmiHardwareInfoService : IHardwareInfoService
{
    public async Task<Result<HardwareInfo>> GetHardwareInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var computer = new HardwareInfo
                {
                    ComputerName = Environment.MachineName,
                    OperatingSystem = Environment.OSVersion.VersionString,
                    OsVersion = Environment.OSVersion.Version.ToString(),
                    LogicalProcessors = Environment.ProcessorCount,
                    TotalPhysicalMemory = (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
                };

                using (var processor = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    var items = processor.Get().Cast<ManagementObject>().ToArray();
                    computer = computer with
                    {
                        CpuName = items.FirstOrDefault()?["Name"]?.ToString() ?? "未知处理器",
                        PhysicalProcessors = items.Length,
                        LogicalProcessors = items.Sum(item => Convert.ToInt32(item["NumberOfLogicalProcessors"], CultureInfo.InvariantCulture))
                    };
                }

                using (var board = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    var item = board.Get().Cast<ManagementObject>().FirstOrDefault();
                    computer = computer with { Motherboard = $"{item?["Manufacturer"]} {item?["Product"]}".Trim() };
                }

                using (var bios = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS"))
                {
                    var item = bios.Get().Cast<ManagementObject>().FirstOrDefault();
                    computer = computer with { BiosVersion = $"{item?["Manufacturer"]} {item?["SMBIOSBIOSVersion"]}".Trim() };
                }

                using (var disks = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive"))
                {
                    computer = computer with
                    {
                        Storage = disks.Get().Cast<ManagementObject>()
                            .Select(item => new StorageInfo(
                                item["Model"]?.ToString() ?? "未知磁盘",
                                item["Size"] is null ? 0 : Convert.ToInt64(item["Size"], CultureInfo.InvariantCulture)))
                            .ToArray()
                    };
                }

                using (var logicalDisks = new ManagementObjectSearcher("SELECT DeviceID, VolumeName, Size, FreeSpace FROM Win32_LogicalDisk"))
                {
                    computer = computer with
                    {
                        Drives = logicalDisks.Get().Cast<ManagementObject>()
                            .Select(item => new global::SysSuite.Core.Abstractions.System.DriveInfo(
                                item["DeviceID"]?.ToString() ?? string.Empty,
                                item["VolumeName"]?.ToString() ?? "本地磁盘",
                                Convert.ToInt64(item["Size"], CultureInfo.InvariantCulture),
                                Convert.ToInt64(item["FreeSpace"], CultureInfo.InvariantCulture)))
                            .ToArray()
                    };
                }

                using (var adapters = new ManagementObjectSearcher("SELECT Name, MACAddress, NetEnabled FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
                {
                    computer = computer with { GraphicsCards = GetGraphicsCards() };
                    computer = computer with
                    {
                        NetworkAdapters = adapters.Get().Cast<ManagementObject>()
                            .Select(item => new NetworkAdapterInfo(
                                item["Name"]?.ToString() ?? "未知适配器",
                                item["MACAddress"]?.ToString() ?? string.Empty,
                                item["NetEnabled"] is bool enabled && enabled))
                            .ToArray()
                    };
                }

                return new Result<HardwareInfo>(ErrorType.None, string.Empty, computer);
            }, cancellationToken);
        }
        catch (ManagementException exception)
        {
            return new Result<HardwareInfo>(ErrorType.DependencyMissing, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new Result<HardwareInfo>(ErrorType.AccessDenied, exception.Message);
        }
    }

    private static global::SysSuite.Core.Abstractions.System.GraphicsCardInfo[] GetGraphicsCards()
    {
        using (var videoControllers = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion, VideoModeDescription FROM Win32_VideoController"))
        {
            return videoControllers.Get().Cast<ManagementObject>()
                .Select(item =>
                {
                    var name = item["Name"]?.ToString() ?? "未知显卡";
                    var manufacturer = item["AdapterCompatibility"]?.ToString() ?? "未知厂商";
                    return new global::SysSuite.Core.Abstractions.System.GraphicsCardInfo(
                        name,
                        manufacturer,
                        Convert.ToUInt64(item["AdapterRAM"] ?? 0UL, CultureInfo.InvariantCulture),
                        item["DriverVersion"]?.ToString() ?? string.Empty,
                        item["VideoModeDescription"]?.ToString() ?? string.Empty,
                        GetGraphicsCategory(name, manufacturer));
                })
                .ToArray();
        }
    }

    private static string GetGraphicsCategory(string name, string manufacturer)
    {
        var text = $"{manufacturer} {name}";
        if (ContainsAny(text, "Virtual", "Hyper-V", "Basic Display"))
        {
            return "虚拟 / 基本显示适配器";
        }

        if (text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return "独立显卡";
        }

        if (text.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? "独立显卡" : "集成显卡";
        }

        if (text.Contains("AMD", StringComparison.OrdinalIgnoreCase) || text.Contains("ATI", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(text, "Radeon RX", "Radeon Pro", "FirePro") ? "独立显卡" : "集成显卡";
        }

        return "显示适配器";
    }

    private static bool ContainsAny(string value, params string[] values)
    {
        return values.Any(value.Contains);
    }
}
