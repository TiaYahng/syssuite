using LibreHardwareMonitor.Hardware;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// <see cref="LibreHardwareSensorService"/> 的硬件树遍历与读数收集逻辑。
///
/// 从主文件拆出（单文件 ≤ 300 行门禁）。这里集中处理三件事：
///  1. <c>SensorType</c> → <see cref="SensorKind"/>（只保留温度/风扇/电压）；
///  2. <c>HardwareType</c> → <see cref="SensorHardwareClass"/>（让 UI 能区分 CPU/GPU）；
///  3. 无效读数与**阈值常量**的剔除。
///
/// 映射与过滤部分为纯函数，不碰硬件状态，因此可以脱离真机做单元测试。
/// </summary>
public sealed partial class LibreHardwareSensorService
{
    /// <summary>
    /// 预热一遍硬件树，让延迟挂载的传感器（CPU 核心温度、主板温度）注册进来。
    /// 失败不致命：真正读取时还会再 Update 一次。
    /// </summary>
    private static void WarmUp(Computer instance)
    {
        foreach (var hardware in instance.Hardware)
        {
            WarmUp(hardware);
        }
    }

    private static void WarmUp(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 单个硬件读失败不影响其他硬件；真正的错误在 Collect 阶段仍有兜底
            _ = exception;
        }

        foreach (var sub in hardware.SubHardware)
        {
            WarmUp(sub);
        }
    }

    private static List<SensorReading> Collect(Computer instance, CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();
        foreach (var hardware in instance.Hardware)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectFrom(hardware, readings, cancellationToken);
        }

        // 同一硬件下重名传感器（如多个 "Temperature #1"）在 UI 上无法区分，
        // 但保留原名更利于与 HWiNFO 对照，故只做稳定排序而不改名。
        readings.Sort(static (left, right) =>
        {
            var byKind = left.Kind.CompareTo(right.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            var byHardware = string.CompareOrdinal(left.Hardware, right.Hardware);
            return byHardware != 0 ? byHardware : string.CompareOrdinal(left.Name, right.Name);
        });
        return readings;
    }

    private static void CollectFrom(IHardware hardware, List<SensorReading> sink, CancellationToken cancellationToken)
    {
        // SubHardware（如 CPU 各核心、主板 SuperIO）需要显式 Update 才会刷新读数
        hardware.Update();
        var hardwareName = string.IsNullOrWhiteSpace(hardware.Name)
            ? hardware.HardwareType.ToString()
            : hardware.Name;
        var hardwareClass = MapClass(hardware.HardwareType);

        foreach (var sensor in hardware.Sensors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = MapKind(sensor.SensorType);
            if (kind is null || sensor.Value is not { } value || !IsPlausible(kind.Value, value))
            {
                continue;
            }

            // 阈值类传感器是**常量**（"Warning Temperature = 80.0" 永远 80），
            // 混进温度聚合会得到假的高温。整类剔除，只留实时读数。
            if (kind == SensorKind.Temperature && IsThresholdSensor(sensor.Name))
            {
                continue;
            }

            sink.Add(new SensorReading(
                kind.Value,
                hardwareClass,
                hardwareName,
                sensor.Name,
                Math.Round(value, 2)));
        }

        foreach (var sub in hardware.SubHardware)
        {
            CollectFrom(sub, sink, cancellationToken);
        }
    }

    private static SensorKind? MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Voltage => SensorKind.Voltage,
        _ => null,
    };

    /// <summary>
    /// 把 LHM 的 <see cref="HardwareType"/> 归一为 <see cref="SensorHardwareClass"/>。
    ///
    /// 只有服务层拿得到 <c>HardwareType</c>，所以分类必须在这里做，
    /// 不能留给 UI 去猜硬件的字符串名字 —— 早期版本正是因为在 UI 侧按名字
    /// 硬编码匹配，才导致 CPU 温度被错误地取成了 GPU 的温度。
    /// </summary>
    private static SensorHardwareClass MapClass(HardwareType type) => type switch
    {
        HardwareType.Cpu => SensorHardwareClass.Cpu,

        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel
            => SensorHardwareClass.Gpu,

        HardwareType.Storage => SensorHardwareClass.Storage,

        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController
            => SensorHardwareClass.Motherboard,

        _ => SensorHardwareClass.Unknown,
    };

    /// <summary>
    /// 过滤明显无效的读数。LibreHardwareMonitor 在读取失败时可能给出 0 或负值，
    /// 直接展示会让用户以为"CPU 温度 0℃"，因此按物理合理区间剔除。
    ///
    /// 上界给到 200℃ 而非 150℃：GPU Hot Spot / VRM 在满载下会到 100℃+，
    /// 某些笔记本 EC 的临界读数也能到 150 以上。放宽上界只是**少滤**，
    /// 没有安全风险 —— 读数仅用于展示，不触发任何动作。
    /// </summary>
    private static bool IsPlausible(SensorKind kind, double value) => kind switch
    {
        SensorKind.Temperature => value is > 0 and < 200,
        SensorKind.Fan => value is > 0 and < 30000,
        SensorKind.Voltage => value is > 0 and < 25,
        _ => false,
    };

    /// <summary>
    /// 识别"阈值/告警门限"类温度传感器。
    ///
    /// 它们报的是**配置门限**而不是当前温度 —— NVMe 健康日志里的
    /// "Warning Temperature = 80.0"、"Critical Temperature = 81.0" 是常量，
    /// 无论硬盘多凉都是这两个数。一旦混进温度聚合（尤其"取最高温"），
    /// 界面就会长期显示 80℃+ 的假高温。整类剔除。
    /// </summary>
    private static bool IsThresholdSensor(string name)
    {
        return name.Contains("Warning Temperature", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Critical Temperature", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Warning Temp", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Critical Temp", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Threshold", StringComparison.OrdinalIgnoreCase);
    }

    // --- 测试可见的薄转发 ---
    //
    // 这些映射/过滤规则是纯函数，但依赖 LHM 的枚举类型，测试项目必须引用
    // LibreHardwareMonitorLib 才能直接传入 HardwareType/SensorType。
    // 为了不把内部实现细节暴露成 public API，这里只开放给 InternalsVisibleTo 的测试程序集。

    internal static SensorHardwareClass MapClassForTests(HardwareType type) => MapClass(type);

    internal static SensorKind? MapKindForTests(SensorType type) => MapKind(type);

    internal static bool IsThresholdSensorForTests(string name) => IsThresholdSensor(name);

    internal static bool IsPlausibleForTests(SensorKind kind, double value) => IsPlausible(kind, value);
}
