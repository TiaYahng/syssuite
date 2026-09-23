using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Interop;

namespace SysSuite.Core.System;

/// <summary>
/// 硬盘健康服务（T1.3）：把 <see cref="NativeInterop.QuerySmart"/> 的 ABI 结果翻译为业务语义。
///
/// 分词职责：原生层只负责"拿到原始 SMART 数值"，健康分级（良好/警告/危险）在这里完成，
/// 阈值集中在本类，调体检标准不需要动 C++。
/// </summary>
public sealed partial class SmartService : ISmartService
{
    // 健康阈值常量。改动会影响所有用户看到的徽章颜色，集中在此并附理由。
    private const int WarningTemperatureCelsius = 55;   // 机械盘长期 >55℃ 显著缩短寿命
    private const int CriticalTemperatureCelsius = 65;  // 多数硬盘厂商的过温告警线
    private const int WarningLifePercent = 20;          // NVMe 剩余寿命 <20% 应提示备份
    private const int CriticalLifePercent = 10;

    public bool IsElevated => Environment.IsPrivilegedProcess;

    public Task<Result<StorageHealthReport>> GetStorageHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Query(cancellationToken), cancellationToken);
    }

    private Result<StorageHealthReport> Query(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = NativeInterop.QuerySmart(out var native);

        if (status == NativeStatus.LibraryNotLoaded)
        {
            return StorageHealthReport.Unavailable(
                "原生模块未构建，无法读取硬盘健康（请执行 rules/Build-Native.ps1）。", IsElevated).Success();
        }

        if (status == NativeStatus.AbiMismatch)
        {
            return StorageHealthReport.Unavailable("原生模块 ABI 版本不匹配，请重新构建。", IsElevated).Success();
        }

        if (status == NativeStatus.AccessDenied)
        {
            // 通常出现在 ATA/SATA 盘上：非管理员进程打不开 \\.\PhysicalDriveN。
            // 注意 NVMe 健康日志走 IOCTL_STORAGE_QUERY_PROPERTY，**不需要提权**也能读到，
            // 所以这句话只在确实全是 ATA 盘且被拒时才出现。
            return StorageHealthReport.Unavailable(
                "需要管理员权限才能读取该硬盘的 SMART 数据（ATA 直通命令需要提权）。", IsElevated).Success();
        }

        if (status != NativeStatus.Ok && status != NativeStatus.Cancelled)
        {
            return new Result<StorageHealthReport>(
                ErrorType.Internal, $"硬盘健康查询失败：{NativeInterop.Describe(status)}");
        }

        if (native.Count == 0)
        {
            return StorageHealthReport.Unavailable("未检测到物理硬盘。", IsElevated).Success();
        }

        var devices = new List<StorageDeviceHealth>(native.Count);
        foreach (var drive in native)
        {
            devices.Add(Translate(drive));
        }

        return new StorageHealthReport(IsElevated, null, devices).Success();
    }

    private static StorageDeviceHealth Translate(SmartDriveInfo drive)
    {
        var level = Evaluate(
            (StorageHealthLevel)drive.Health,
            drive.IsAvailable,
            drive.TemperatureCelsius,
            drive.RemainingLifePercent,
            drive.PendingSectors,
            drive.UncorrectableErrors);

        return new StorageDeviceHealth(
            drive.DriveNumber,
            drive.Model,
            drive.Serial,
            drive.Firmware,
            drive.IsAvailable,
            level,
            drive.TemperatureCelsius,
            drive.RemainingLifePercent,
            drive.ReallocatedSectors,
            drive.PendingSectors,
            drive.UncorrectableErrors,
            drive.PowerOnHours,
            drive.PowerCycleCount,
            drive.TotalBytesWritten,
            drive.AtaSmartAttributeCount);
    }

    /// <summary>
    /// 健康分级。原生层已按 ATA 属性给出初判，这里补充温度与寿命两项原生层不掌握的阈值判定，
    /// 并确保"读不到"永远是 Unknown 而不是 Good（避免误报健康）。
    /// </summary>
    internal static StorageHealthLevel Evaluate(
        StorageHealthLevel nativeLevel,
        bool isAvailable,
        int temperatureCelsius,
        int? remainingLifePercent,
        long? pendingSectors,
        long? uncorrectableErrors)
    {
        if (!isAvailable)
        {
            return StorageHealthLevel.Unknown;
        }

        // 危险：待处理扇区或不可纠正错误存在，说明已经有数据读不出来了
        if (pendingSectors is > 0 || uncorrectableErrors is > 0)
        {
            return StorageHealthLevel.Critical;
        }

        if (nativeLevel == StorageHealthLevel.Critical || temperatureCelsius >= CriticalTemperatureCelsius)
        {
            return StorageHealthLevel.Critical;
        }

        if (remainingLifePercent is { } life && life <= CriticalLifePercent)
        {
            return StorageHealthLevel.Critical;
        }

        if (nativeLevel == StorageHealthLevel.Warning
            || temperatureCelsius >= WarningTemperatureCelsius
            || (remainingLifePercent is { } warnLife && warnLife <= WarningLifePercent))
        {
            return StorageHealthLevel.Warning;
        }

        return StorageHealthLevel.Good;
    }
}
