using System.Globalization;
using System.Text;
using SysSuite.Interop;

namespace SysSuite.Tests;

/// <summary>
/// 真机 SMART 探针（T1.3 验证用）。
///
/// 以「不抛断言、只落盘报告」的形式存在是刻意的：它读的是本机真实硬盘，
/// 结果随机器与权限状态变化，不能作为可重复的 CI 断言；只用于人工核对
/// 是否与 CrystalDiskInfo 一致。
///
/// ⚠ 非提权进程打开 \\.\PhysicalDriveN 会被拒（ERROR_ACCESS_DENIED），
/// 因此在本机 `dotnet test` 下预期返回 AccessDenied；要读真实数据需以管理员运行。
/// </summary>
public class SmartProbeTests
{
    [Fact]
    public void DumpSmartInformation()
    {
        var status = NativeInterop.QuerySmart(out var drives);
        var report = new StringBuilder();
        Append(report, $"NativeStatus = {status} ({NativeInterop.Describe(status)})");
        Append(report, $"LibraryPath  = {NativeInterop.LibraryPath ?? "<not loaded>"}");
        Append(report, $"IsElevated   = {Environment.IsPrivilegedProcess}");
        Append(report, $"DriveCount   = {drives.Count.ToString(CultureInfo.InvariantCulture)}");

        foreach (var drive in drives)
        {
            Append(report, "---");
            Append(report, $"  DriveNumber      = {drive.DriveNumber.ToString(CultureInfo.InvariantCulture)}");
            Append(report, $"  Model            = '{drive.Model}'");
            Append(report, $"  Serial           = '{drive.Serial}'");
            Append(report, $"  Firmware         = '{drive.Firmware}'");
            Append(report, $"  IsAvailable      = {drive.IsAvailable}");
            Append(report, $"  Health           = {drive.Health}");
            Append(report, $"  Temperature      = {drive.TemperatureCelsius.ToString(CultureInfo.InvariantCulture)} C");
            Append(report, $"  RemainingLife    = {Format(drive.RemainingLifePercent)} %");
            Append(report, $"  Reallocated      = {Format(drive.ReallocatedSectors)}");
            Append(report, $"  Pending          = {Format(drive.PendingSectors)}");
            Append(report, $"  Uncorrectable    = {Format(drive.UncorrectableErrors)}");
            Append(report, $"  PowerOnHours     = {drive.PowerOnHours.ToString(CultureInfo.InvariantCulture)} ({drive.PowerOnDurationText})");
            Append(report, $"  PowerCycles      = {drive.PowerCycleCount.ToString(CultureInfo.InvariantCulture)}");
            Append(report, $"  TotalWritten     = {Format(drive.TotalBytesWritten)}");
            Append(report, $"  AtaAttrCount     = {drive.AtaSmartAttributeCount.ToString(CultureInfo.InvariantCulture)}");
        }

        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "syssuite-smart-probe.txt"),
            report.ToString(),
            new UTF8Encoding(false));
    }

    private static void Append(StringBuilder builder, string line)
        => builder.AppendLine(line);

    private static string Format(long? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
