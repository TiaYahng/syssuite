using LibreHardwareMonitor.Hardware;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;

namespace SysSuite.Tests;

/// <summary>
/// 传感器映射与过滤的纯函数测试（T1.2）。
///
/// 这些规则此前是隐式的、没有测试覆盖，导致两个真实缺陷：
///  1. GPU 温度被当成 CPU 温度（缺 <c>HardwareType</c> 分类）；
///  2. SSD 的 Warning/Critical Temperature 阈值（80/81℃）被当成实时温度。
/// 这里把规则固化成断言，防止回归。
/// </summary>
public class SensorMappingTests
{
    [Theory]
    [InlineData(HardwareType.Cpu, SensorHardwareClass.Cpu)]
    [InlineData(HardwareType.GpuNvidia, SensorHardwareClass.Gpu)]
    [InlineData(HardwareType.GpuAmd, SensorHardwareClass.Gpu)]
    [InlineData(HardwareType.GpuIntel, SensorHardwareClass.Gpu)]
    [InlineData(HardwareType.Storage, SensorHardwareClass.Storage)]
    [InlineData(HardwareType.Motherboard, SensorHardwareClass.Motherboard)]
    [InlineData(HardwareType.SuperIO, SensorHardwareClass.Motherboard)]
    [InlineData(HardwareType.EmbeddedController, SensorHardwareClass.Motherboard)]
    [InlineData(HardwareType.Memory, SensorHardwareClass.Unknown)]
    [InlineData(HardwareType.Network, SensorHardwareClass.Unknown)]
    [InlineData(HardwareType.Psu, SensorHardwareClass.Unknown)]
    public void MapClassMapsHardwareTypesToUiCategories(HardwareType type, SensorHardwareClass expected)
    {
        Assert.Equal(expected, LibreHardwareSensorService.MapClassForTests(type));
    }

    [Theory]
    [InlineData(SensorType.Temperature, SensorKind.Temperature)]
    [InlineData(SensorType.Fan, SensorKind.Fan)]
    [InlineData(SensorType.Voltage, SensorKind.Voltage)]
    public void MapKindKeepsSupportedSensorTypes(SensorType type, SensorKind expected)
    {
        Assert.Equal(expected, LibreHardwareSensorService.MapKindForTests(type));
    }

    [Theory]
    [InlineData(SensorType.Load)]
    [InlineData(SensorType.Clock)]
    [InlineData(SensorType.Power)]
    [InlineData(SensorType.Data)]
    [InlineData(SensorType.Level)]
    public void MapKindDropsUnsupportedSensorTypes(SensorType type)
    {
        Assert.Null(LibreHardwareSensorService.MapKindForTests(type));
    }

    [Theory]
    [InlineData("Warning Temperature")]
    [InlineData("Critical Temperature")]
    [InlineData("Warning Temp")]
    [InlineData("Critical Temp")]
    [InlineData("Composite Temperature Threshold")]
    public void ThresholdSensorsAreExcluded(string name)
    {
        // 这些名字报的是配置门限常量（如 NVMe 的 80/81℃），不是实时温度
        Assert.True(LibreHardwareSensorService.IsThresholdSensorForTests(name));
    }

    [Theory]
    [InlineData("Composite Temperature")]
    [InlineData("Temperature #1")]
    [InlineData("CPU Package")]
    [InlineData("GPU Hot Spot")]
    [InlineData("GPU Core")]
    [InlineData("Core Max")]
    public void RealTimeSensorsAreKept(string name)
    {
        Assert.False(LibreHardwareSensorService.IsThresholdSensorForTests(name));
    }

    [Fact]
    public void ThresholdDetectionIsCaseInsensitive()
    {
        Assert.True(LibreHardwareSensorService.IsThresholdSensorForTests("CRITICAL TEMPERATURE"));
        Assert.True(LibreHardwareSensorService.IsThresholdSensorForTests("warning temperature"));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(25.0, true)]
    [InlineData(55.0, true)]
    [InlineData(90.0, true)]
    [InlineData(190.0, true)]
    [InlineData(0.0, false)]
    [InlineData(-5.0, false)]
    [InlineData(250.0, false)]
    public void TemperaturePlausibilityUsesPhysicalRange(double value, bool expected)
    {
        Assert.Equal(expected, LibreHardwareSensorService.IsPlausibleForTests(SensorKind.Temperature, value));
    }

    [Fact]
    public void GpuHotSpotAbove150IsNotFiltered()
    {
        // 上界放宽到 200℃ 的原因：旧实现按 <150 过滤，会误删满载下的 GPU Hot Spot
        Assert.True(LibreHardwareSensorService.IsPlausibleForTests(SensorKind.Temperature, 165.0));
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(1.2, true)]
    [InlineData(12.0, true)]
    [InlineData(0.0, false)]
    [InlineData(30.0, false)]
    public void VoltagePlausibilityUsesPhysicalRange(double value, bool expected)
    {
        Assert.Equal(expected, LibreHardwareSensorService.IsPlausibleForTests(SensorKind.Voltage, value));
    }

    [Theory]
    [InlineData(800.0, true)]
    [InlineData(2500.0, true)]
    [InlineData(0.0, false)]
    [InlineData(40000.0, false)]
    public void FanPlausibilityUsesPhysicalRange(double value, bool expected)
    {
        Assert.Equal(expected, LibreHardwareSensorService.IsPlausibleForTests(SensorKind.Fan, value));
    }
}
