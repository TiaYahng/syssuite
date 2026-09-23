namespace SysSuite.Interop;

/// <summary>单块物理盘的 SMART 状态（T1.3），已从 ABI 结构体翻译为托管语义。</summary>
public sealed record SmartDriveInfo(
    int DriveNumber,
    string Model,
    string Serial,
    string Firmware,
    bool IsAvailable,
    SmartHealthStatus Health,
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
    /// <summary>用于 UI 的简短标识，型号为空时退回盘号。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? $"物理盘 {DriveNumber}" : Model;

    /// <summary>通电时间的可读形式（天 + 小时）。</summary>
    public string PowerOnDurationText => PowerOnHours <= 0
        ? "未知"
        : $"{PowerOnHours / 24} 天 {PowerOnHours % 24} 小时";
}

/// <summary>健康度分级。阈值常量集中在 <see cref="SmartHealthStatus"/> 的映射逻辑里，便于按需调整。</summary>
public enum SmartHealthStatus
{
    /// <summary>未能读取（不支持 SMART / RAID 虚拟盘 / 权限不足）。</summary>
    Unknown = 0,

    /// <summary>良好。</summary>
    Good = 1,

    /// <summary>警告：存在重映射或寿命偏低，建议备份。</summary>
    Warning = 2,

    /// <summary>危险：存在待处理扇区或不可纠正错误，应立即备份。</summary>
    Critical = 3,
}
