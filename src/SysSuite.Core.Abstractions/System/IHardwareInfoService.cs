namespace SysSuite.Core.Abstractions.System;

public sealed record HardwareInfo
{
    public string ComputerName { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string CpuName { get; init; } = string.Empty;
    public int LogicalProcessors { get; init; }
    public int PhysicalProcessors { get; init; }
    public string Motherboard { get; init; } = string.Empty;
    public string BiosVersion { get; init; } = string.Empty;
    public ulong TotalPhysicalMemory { get; init; }
    public IReadOnlyList<StorageInfo> Storage { get; init; } = [];
    public IReadOnlyList<DriveInfo> Drives { get; init; } = [];
    public IReadOnlyList<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];
}

public sealed record StorageInfo(string Model, long SizeBytes);

public sealed record DriveInfo(
    string DriveLetter,
    string Label,
    long TotalBytes,
    long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedPercent => TotalBytes <= 0 ? 0 : Math.Clamp(UsedBytes * 100d / TotalBytes, 0, 100);
}

public sealed record NetworkAdapterInfo(string Name, string MacAddress, bool IsEnabled);

public interface IHardwareInfoService
{
    Task<Result<HardwareInfo>> GetHardwareInfoAsync(CancellationToken cancellationToken = default);
}
