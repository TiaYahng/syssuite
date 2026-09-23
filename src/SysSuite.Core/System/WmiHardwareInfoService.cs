using System.Management;
using System.Globalization;
using System.Security;
using Microsoft.Win32;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed class WmiHardwareInfoService : IHardwareInfoService
{
    /// <summary>显示适配器类 GUID 的注册表路径，显卡驱动在此登记 64 位显存容量。</summary>
    private const string DisplayClassRegistryPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

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

                // 产品名优先取 WMI 的 Caption（“Microsoft Windows 11 家庭中文版”）；Environment.OSVersion 只给出 “Microsoft Windows NT 10.0.x”
                using (var operatingSystem = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem"))
                {
                    var item = operatingSystem.Get().Cast<ManagementObject>().FirstOrDefault();
                    computer = computer with
                    {
                        OperatingSystem = Describe(item?["Caption"], computer.OperatingSystem),
                        OsVersion = Describe(item?["Version"], computer.OsVersion)
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
                    var wmiMemory = item["AdapterRAM"] is null
                        ? 0UL
                        : Convert.ToUInt64(item["AdapterRAM"], CultureInfo.InvariantCulture);
                    return new global::SysSuite.Core.Abstractions.System.GraphicsCardInfo(
                        name,
                        manufacturer,
                        ResolveGraphicsMemory(name, wmiMemory),
                        item["DriverVersion"]?.ToString() ?? string.Empty,
                        NormalizeVideoMode(item["VideoModeDescription"]?.ToString()),
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

    private static string Describe(object? value, string fallback)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    /// <summary>
    /// WMI 的 <c>VideoModeDescription</c> 形如 “1920 x 1080 x 4294967296 种颜色”，其中色深字段
    /// 常为 uint32 溢出值，直接展示无意义；此处只保留 “宽 x 高”。
    /// </summary>
    internal static string NormalizeVideoMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[1].Equals("x", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(parts[0], " x ", parts[2])
            : raw;
    }

    /// <summary>
    /// <c>Win32_VideoController.AdapterRAM</c> 是 32 位字段，显存 ≥ 4GB 时必然溢出为错误值
    /// （实测 RTX 2070 8GB 被报成 4.0 GB）。这里改读显示类驱动登记的 64 位
    /// <c>HardwareInformation.qwMemorySize</c>，取不到才退回 WMI 值。
    /// </summary>
    private static ulong ResolveGraphicsMemory(string adapterName, ulong wmiValue)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassRegistryPath);
            if (classKey is null)
            {
                return wmiValue;
            }

            var resolved = wmiValue;
            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                var declaredBytes = ReadDeclaredGraphicsMemory(classKey, subKeyName, adapterName);
                if (declaredBytes > 0)
                {
                    resolved = Math.Max(resolved, declaredBytes);
                }
            }

            return resolved;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return wmiValue;
        }
    }

    /// <summary>
    /// 读取单个适配器子键登记的 64 位显存。驱动把它写成名为
    /// <c>HardwareInformation.qwMemorySize</c> 的 REG_QWORD 值（而不是子键）；
    /// 同一子键下还有 32 位截断的 <c>HardwareInformation.MemorySize</c>，不可用。
    /// 名称不匹配或子键不可访问时返回 0（例如类键下的 <c>Properties</c> 需要更高权限）。
    /// </summary>
    private static ulong ReadDeclaredGraphicsMemory(RegistryKey classKey, string subKeyName, string adapterName)
    {
        try
        {
            using var adapterKey = classKey.OpenSubKey(subKeyName);
            if (adapterKey?.GetValue("DriverDesc") is not string description || !MatchesAdapter(description, adapterName))
            {
                return 0;
            }

            return adapterKey.GetValue("HardwareInformation.qwMemorySize") is long declaredBytes && declaredBytes > 0
                ? (ulong)declaredBytes
                : 0;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }

    internal static bool MatchesAdapter(string description, string adapterName)
        => !string.IsNullOrWhiteSpace(adapterName)
            && (description.Equals(adapterName, StringComparison.OrdinalIgnoreCase)
                || description.Contains(adapterName, StringComparison.OrdinalIgnoreCase)
                || adapterName.Contains(description, StringComparison.OrdinalIgnoreCase));
}
