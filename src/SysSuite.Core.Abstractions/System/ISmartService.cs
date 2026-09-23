namespace SysSuite.Core.Abstractions.System;

/// <summary>数值不可用时的占位语义：null 表示"没读出来"，0 表示"读到 0"。</summary>
public sealed record StorageDeviceHealth(
    int DriveNumber,
    string Model,
    string Serial,
    string Firmware,
    bool IsAvailable,
    StorageHealthLevel Level,
    int TemperatureCelsius,
    int? RemainingLifePercent,
    long? ReallocatedSectors,
    long? PendingSectors,
    long? UncorrectableErrors,
    long PowerOnHours,
    long PowerCycleCount,
    long? TotalBytesWritten,
    int AtaSmartAttributeCount)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? $"物理盘 {DriveNumber}" : Model;

    public string PowerOnDurationText => PowerOnHours <= 0
        ? "未知"
        : $"{PowerOnHours / 24} 天 {PowerOnHours % 24} 小时";

    /// <summary>健康徽章的 UI 文案。</summary>
    public string LevelText => Level switch
    {
        StorageHealthLevel.Good => "良好",
        StorageHealthLevel.Warning => "警告",
        StorageHealthLevel.Critical => "危险",
        _ => "未知",
    };
}

/// <summary>健康分级。阈值常量集中在 <see cref="SmartHealthEvaluator"/>，便于按体检标准调整。</summary>
public enum StorageHealthLevel
{
    Unknown = 0,
    Good = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>一次磁盘健康查询的结果；<see cref="IsElevated"/> 用于区分"没盘"与"没权限"。</summary>
public sealed record StorageHealthReport(
    bool IsElevated,
    string? UnavailableReason,
    IReadOnlyList<StorageDeviceHealth> Devices)
{
    /// <summary>是否因为缺少管理员权限而读不到任何硬盘。</summary>
    public bool RequiresElevation => !IsElevated && Devices.Count == 0;

    public static StorageHealthReport Unavailable(string reason, bool isElevated)
        => new(isElevated, reason, []);
}

/// <summary>
/// 硬盘健康服务（T1.3）。读取 SMART / NVMe 健康日志，只读不改。
/// 非提权或平台不支持时必须给出明确原因，不得假装"硬盘都好"。
/// </summary>
public interface ISmartService
{
    /// <summary>当前进程是否具有管理员权限；无权限时 SMART 无法读取。</summary>
    bool IsElevated { get; }

    /// <summary>查询所有物理盘的健康状态。</summary>
    Task<Result<StorageHealthReport>> GetStorageHealthAsync(CancellationToken cancellationToken = default);
}
