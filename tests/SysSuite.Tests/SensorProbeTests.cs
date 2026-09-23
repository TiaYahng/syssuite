using System.Globalization;
using System.Text;
using LibreHardwareMonitor.Hardware;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;

namespace SysSuite.Tests;

/// <summary>
/// 真机传感器探针（T1.2 验证用）。
///
/// 与 <see cref="SmartProbeTests"/> 同样刻意不做断言：温度读数随机器、负载、
/// 环境温度实时变化，无法作为可重复的 CI 断言，只用于人工与 HWiNFO 对照。
/// 结果落盘到 %TEMP%\syssuite-sensor-probe.txt。
///
/// 探针分两段：
///  1. 走 <see cref="LibreHardwareSensorService"/> 的生产路径（受 IsPlausible 与阈值过滤影响）；
///  2. 直连 LibreHardwareMonitor，枚举**原始**硬件树与全部传感器（不过滤），
///     用于区分"硬件没暴露"与"被我们的过滤规则挡掉了"。
/// </summary>
public class SensorProbeTests
{
    [Fact]
    public async Task DumpSensorInformation()
    {
        using var service = new LibreHardwareSensorService();
        var report = new StringBuilder();
        Append(report, $"IsElevated = {Environment.IsPrivilegedProcess}");

        var result = await service.GetSensorsAsync();
        Append(report, $"ResultOk   = {result.IsSuccess}");
        if (!result.IsSuccess || result.Value is null)
        {
            Append(report, $"Message    = {result.Message}");
        }
        else
        {
            var snapshot = result.Value;
            Append(report, $"Available  = {snapshot.IsAvailable}");
            Append(report, $"Reason     = {snapshot.UnavailableReason ?? "-"}");
            Append(report, $"Readings   = {snapshot.Readings.Count.ToString(CultureInfo.InvariantCulture)}");
            Append(report, $"MaxTempCpu = {snapshot.MaxTemperatureOf(SensorHardwareClass.Cpu).ToString("F1", CultureInfo.InvariantCulture)} C");
            Append(report, $"MaxTempGpu = {snapshot.MaxTemperatureOf(SensorHardwareClass.Gpu).ToString("F1", CultureInfo.InvariantCulture)} C");
            Append(report, $"MaxFan     = {snapshot.MaxOf(SensorKind.Fan).ToString("F0", CultureInfo.InvariantCulture)} RPM");
            Append(report, $"NeedsPawnIO= {snapshot.CpuTemperatureNeedsKernelDriver}");
            Append(report, $"PawnIO     = {SensorDiagnostics.IsPawnIoAvailable()}");

            Append(report, string.Empty);
            Append(report, "--- 生产路径读数（已过滤）---");
            foreach (var reading in snapshot.Readings)
            {
                Append(report, string.Create(
                    CultureInfo.InvariantCulture,
                    $"  [{reading.Kind}/{reading.HardwareClass}] {reading.Hardware} / {reading.Name} = {reading.Value:F1}"));
            }
        }

        AppendRawHardwareTree(report);

        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "syssuite-sensor-probe.txt"),
            report.ToString(),
            new UTF8Encoding(false));
    }

    /// <summary>
    /// 直连 LHM 枚举原始硬件树。**这是诊断 CPU 温度缺失的关键** ——
    /// 生产路径只看得到过滤后的结果，无法区分"读不到"与"被过滤"。
    /// </summary>
    private static void AppendRawHardwareTree(StringBuilder report)
    {
        Append(report, string.Empty);
        Append(report, "--- 原始硬件树（未过滤）---");

        Computer? computer = null;
        try
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
            };
            computer.Open();
            Append(report, $"Hardware.Count = {computer.Hardware.Count.ToString(CultureInfo.InvariantCulture)}");

            foreach (var hardware in computer.Hardware)
            {
                AppendRawHardware(report, hardware, 0);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Append(report, $"原始枚举失败：{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            try
            {
                computer?.Close();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _ = exception;
            }
        }
    }

    private static void AppendRawHardware(StringBuilder report, IHardware hardware, int depth)
    {
        var indent = new string(' ', depth * 2);
        Append(report, string.Create(
            CultureInfo.InvariantCulture,
            $"{indent}{hardware.HardwareType} '{hardware.Name}' sensors={hardware.Sensors.Length} sub={hardware.SubHardware.Length}"));

        try
        {
            hardware.Update();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Append(report, $"{indent}  Update 失败：{exception.GetType().Name}");
        }

        foreach (var sensor in hardware.Sensors)
        {
            Append(report, string.Create(
                CultureInfo.InvariantCulture,
                $"{indent}  - {sensor.SensorType} '{sensor.Name}' = {sensor.Value?.ToString("F2", CultureInfo.InvariantCulture) ?? "null"}"));
        }

        foreach (var sub in hardware.SubHardware)
        {
            AppendRawHardware(report, sub, depth + 1);
        }
    }

    private static void Append(StringBuilder builder, string line) => builder.AppendLine(line);
}
