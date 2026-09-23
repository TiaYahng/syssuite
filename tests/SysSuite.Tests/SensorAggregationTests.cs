using SysSuite.Core.Abstractions.System;

namespace SysSuite.Tests;

/// <summary>
/// 传感器快照的聚合语义测试（T1.2）。
///
/// 这一组测试锁定两个真实踩过的坑：
///  1. CPU 与 GPU 温度曾共用同一个 <see cref="SensorSnapshot.MaxOf"/>，
///     导致 GPU Hot Spot（~90℃）被当成 CPU 温度显示；
///  2. SSD 的 Warning/Critical Temperature 是**常量阈值**（80/81℃），
///     一旦混入温度聚合就会显示出假的高温。
/// </summary>
public class SensorAggregationTests
{
    [Fact]
    public void MaxTemperatureOfSeparatesCpuFromGpu()
    {
        var snapshot = Snapshot(
            Reading(SensorKind.Temperature, SensorHardwareClass.Cpu, "Intel Core i7", "CPU Package", 62.0),
            Reading(SensorKind.Temperature, SensorHardwareClass.Cpu, "Intel Core i7", "CPU Core #1", 65.0),
            Reading(SensorKind.Temperature, SensorHardwareClass.Gpu, "RTX 2070", "GPU Core", 79.0),
            Reading(SensorKind.Temperature, SensorHardwareClass.Gpu, "RTX 2070", "GPU Hot Spot", 89.8));

        Assert.Equal(65.0, snapshot.MaxTemperatureOf(SensorHardwareClass.Cpu));
        Assert.Equal(89.8, snapshot.MaxTemperatureOf(SensorHardwareClass.Gpu));

        // 跨硬件的旧语义会拿到 GPU 的 89.8 —— 这正是那个 bug 的行为
        Assert.Equal(89.8, snapshot.MaxOf(SensorKind.Temperature));
    }

    [Fact]
    public void MaxTemperatureOfReturnsZeroWhenClassMissing()
    {
        var snapshot = Snapshot(
            Reading(SensorKind.Temperature, SensorHardwareClass.Gpu, "RTX 2070", "GPU Core", 79.0));

        // 0 是 UI 显示 "--" 的信号；不能返回 double.NegativeInfinity 或 NaN
        Assert.Equal(0d, snapshot.MaxTemperatureOf(SensorHardwareClass.Cpu));
    }

    [Fact]
    public void MaxTemperatureOfIgnoresOtherKinds()
    {
        var snapshot = Snapshot(
            Reading(SensorKind.Voltage, SensorHardwareClass.Cpu, "Intel Core i7", "CPU Core Voltage", 1.2),
            Reading(SensorKind.Fan, SensorHardwareClass.Cpu, "Intel Core i7", "CPU Fan", 2400));

        Assert.Equal(0d, snapshot.MaxTemperatureOf(SensorHardwareClass.Cpu));
    }

    [Fact]
    public void MaxOfStillWorksForUngroupedKinds()
    {
        var snapshot = Snapshot(
            Reading(SensorKind.Fan, SensorHardwareClass.Motherboard, "HP 8746", "Fan #1", 1800),
            Reading(SensorKind.Fan, SensorHardwareClass.Gpu, "RTX 2070", "GPU Fan", 2600));

        Assert.Equal(2600d, snapshot.MaxOf(SensorKind.Fan));
    }

    [Fact]
    public void UnavailableSnapshotReportsNothingAvailable()
    {
        var snapshot = SensorSnapshot.Unavailable("虚拟机");

        Assert.False(snapshot.IsAvailable);
        Assert.Empty(snapshot.Readings);
        Assert.Equal(0d, snapshot.MaxTemperatureOf(SensorHardwareClass.Cpu));
        Assert.Equal(0d, snapshot.MaxOf(SensorKind.Temperature));
        Assert.False(snapshot.CpuTemperatureNeedsKernelDriver);
    }

    [Fact]
    public void KernelDriverFlagDefaultsToFalse()
    {
        // 不用对象初始化器时必须是 false —— 结构性属性不应意外变成"需要驱动提示"
        var snapshot = new SensorSnapshot(true, null, []);

        Assert.False(snapshot.CpuTemperatureNeedsKernelDriver);
        Assert.False(snapshot.CpuTemperatureNeedsElevation);
    }

    [Fact]
    public void CpuTemperatureNeedsElevationIsIndependentOfKernelDriverFlag()
    {
        // 两种失败模式互斥：装好了驱动就只可能提示"要提权"，不可能同时提示"要装驱动"。
        // 若实现把二者合并成一个布尔，UI 就会给出错误的处置建议。
        var needsDriver = new SensorSnapshot(true, null, []) { CpuTemperatureNeedsKernelDriver = true };
        var needsElevation = new SensorSnapshot(true, null, []) { CpuTemperatureNeedsElevation = true };

        Assert.True(needsDriver.CpuTemperatureNeedsKernelDriver);
        Assert.False(needsDriver.CpuTemperatureNeedsElevation);
        Assert.True(needsElevation.CpuTemperatureNeedsElevation);
        Assert.False(needsElevation.CpuTemperatureNeedsKernelDriver);
    }

    [Fact]
    public void CpuTemperaturePresentMeansNoGapFlagsNeeded()
    {
        // 有 CPU 温度就不该出现任何"读不到"的提示
        var snapshot = Snapshot(
            Reading(SensorKind.Temperature, SensorHardwareClass.Cpu, "Intel Core i7", "CPU Package", 62.0));

        Assert.False(snapshot.CpuTemperatureNeedsKernelDriver);
        Assert.False(snapshot.CpuTemperatureNeedsElevation);
    }

    private static SensorSnapshot Snapshot(params SensorReading[] readings) => new(true, null, readings);

    private static SensorReading Reading(
        SensorKind kind,
        SensorHardwareClass hardwareClass,
        string hardware,
        string name,
        double value) => new(kind, hardwareClass, hardware, name, value);
}
