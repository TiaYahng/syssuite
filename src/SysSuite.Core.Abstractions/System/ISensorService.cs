namespace SysSuite.Core.Abstractions.System;

/// <summary>传感器读数类别。UI 按类别分组展示，不需要知道具体硬件来源。</summary>
public enum SensorKind
{
    /// <summary>温度（摄氏度）。</summary>
    Temperature = 0,

    /// <summary>风扇转速（RPM）。</summary>
    Fan = 1,

    /// <summary>电压（伏特）。</summary>
    Voltage = 2,
}

/// <summary>
/// 读数所属的硬件大类。
///
/// 存在的意义：只有 <see cref="SensorReading.Hardware"/> 这个原始名称字符串时，
/// UI 无法区分「CPU 包温」与「SSD 临界温度阈值」—— 后者是**常量阈值**而非实时温度，
/// 一旦混入"最高温度"聚合就会显示成假的高温告警。分类必须在服务层完成，
/// 因为只有服务层知道 <c>HardwareType</c>。
/// </summary>
public enum SensorHardwareClass
{
    /// <summary>无法归类（保留原始名称，不参与温度聚合）。</summary>
    Unknown = 0,

    /// <summary>CPU（含核心、封装、主板侧 CPU 温度）。</summary>
    Cpu = 1,

    /// <summary>GPU（独显 / 核显）。</summary>
    Gpu = 2,

    /// <summary>存储（SSD / HDD）。</summary>
    Storage = 3,

    /// <summary>主板 / 超级 IO（含机箱温度、风扇）。</summary>
    Motherboard = 4,
}

/// <summary>单个传感器读数。名称保留硬件原始命名，便于与 HWiNFO 等工具对照。</summary>
public sealed record SensorReading(
    SensorKind Kind,
    SensorHardwareClass HardwareClass,
    string Hardware,
    string Name,
    double Value);

/// <summary>
/// 传感器能力快照。<see cref="IsAvailable"/> 为 false 时 UI 必须显示"不可用"而不是报错。
/// </summary>
public sealed record SensorSnapshot(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<SensorReading> Readings)
{
    public static SensorSnapshot Unavailable(string reason) => new(false, reason, []);

    /// <summary>
    /// CPU 温度是否因缺少内核态访问驱动（PawnIO）而读不到。
    ///
    /// 这是 LHM 0.9.6 起的一个**常见且极易被误判为程序缺陷**的状态：
    /// 没装 PawnIO 时 GPU/NVMe 温度照常可读，只有 CPU 温度全为 null。
    /// UI 应据此给出可操作的提示，而不是静默显示 "--"。
    /// </summary>
    public bool CpuTemperatureNeedsKernelDriver { get; init; }

    /// <summary>
    /// 指定类别中所有读数的最大值，用于"CPU 包温"这类聚合展示。
    ///
    /// **调用方应优先用 <see cref="MaxTemperatureOf"/>**：本方法跨硬件聚合，
    /// 会把 SSD 的 Warning/Critical Temperature 阈值常量当成实时温度。
    /// 保留它只是为了兼容不区分硬件的场景（如风扇/电压）。
    /// </summary>
    public double MaxOf(SensorKind kind)
    {
        var max = double.NegativeInfinity;
        foreach (var reading in Readings)
        {
            if (reading.Kind == kind && reading.Value > max)
            {
                max = reading.Value;
            }
        }

        return double.IsNegativeInfinity(max) ? 0 : max;
    }

    /// <summary>
    /// 指定硬件大类的最高温度。无该类读数时返回 0（UI 据此显示 "--"）。
    ///
    /// CPU 与 GPU 各取各的，不再共用同一个"全局最高温"，避免拿 GPU 热点温度冒充 CPU 温度。
    /// </summary>
    public double MaxTemperatureOf(SensorHardwareClass hardwareClass)
    {
        var max = double.NegativeInfinity;
        foreach (var reading in Readings)
        {
            if (reading.Kind == SensorKind.Temperature
                && reading.HardwareClass == hardwareClass
                && reading.Value > max)
            {
                max = reading.Value;
            }
        }

        return double.IsNegativeInfinity(max) ? 0 : max;
    }
}

/// <summary>
/// 硬件传感器服务（T1.2）。只读、按需节流轮询；
/// 虚拟机与缺少传感器的主板必须优雅降级为 <see cref="SensorSnapshot.IsAvailable"/> = false。
/// </summary>
public interface ISensorService : IDisposable
{
    /// <summary>读取当前传感器快照。内部按节流间隔决定是否真正查询硬件。</summary>
    Task<Result<SensorSnapshot>> GetSensorsAsync(CancellationToken cancellationToken = default);

    /// <summary>最近一次成功快照；从未成功读取时为 null。用于 UI 立即渲染历史值。</summary>
    SensorSnapshot? LastSnapshot { get; }
}
