using System.Globalization;
using LibreHardwareMonitor.Hardware;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 传感器服务（T1.2）：基于 LibreHardwareMonitorLib 的只读封装。
///
/// 设计约束（对应 T1.2 验收）：
///  1. **只读** —— 只实例化 <see cref="Computer"/> 并订阅传感器，不写入任何硬件寄存器。
///  2. **节流** —— 底层硬件轮询有真实开销（部分主板走 SMBus/EC，读取会阻塞毫秒级），
///     因此按 <see cref="ThrottleInterval"/> 缓存结果；UI 可以高频调用本方法。
///  3. **优雅降级** —— 虚拟机、无传感器主板、被安全软件拦截等情况一律返回
///     <see cref="SensorSnapshot.IsAvailable"/> = false 并附带原因，绝不抛异常给 UI。
///
/// 线程模型：LibreHardwareMonitor 的 <c>Update()</c> 不是线程安全的，全部访问串行化在
/// <see cref="gate"/> 之下，避免 UI 定时器与后台刷新并发调用造成硬件访问冲突。
/// </summary>
public sealed partial class LibreHardwareSensorService : ISensorService
{
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(2);

    private readonly object gate = new();
    private readonly TimeSpan throttleInterval;

    private Computer? computer;
    private bool isSupported = true;
    private string? unavailableReason;
    private SensorSnapshot? lastSnapshot;
    private DateTimeOffset lastReadUtc = DateTimeOffset.MinValue;
    private bool disposed;

    public LibreHardwareSensorService()
        : this(ThrottleInterval)
    {
    }

    /// <summary>测试用：允许注入更短的节流间隔。</summary>
    internal LibreHardwareSensorService(TimeSpan throttleInterval)
    {
        this.throttleInterval = throttleInterval;
    }

    public SensorSnapshot? LastSnapshot
    {
        get
        {
            lock (gate)
            {
                return lastSnapshot;
            }
        }
    }

    public Task<Result<SensorSnapshot>> GetSensorsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadCore(cancellationToken), cancellationToken);
    }

    private Result<SensorSnapshot> ReadCore(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (disposed)
            {
                return new Result<SensorSnapshot>(ErrorType.Internal, "传感器服务已释放。");
            }

            if (!isSupported)
            {
                return BuildUnavailableResult();
            }

            // 节流命中：直接返回缓存，不打硬件
            if (lastSnapshot is not null && DateTimeOffset.UtcNow - lastReadUtc < throttleInterval)
            {
                return lastSnapshot.Success();
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureComputerInitialized();
                if (computer is null)
                {
                    return BuildUnavailableResult();
                }

                var readings = Collect(computer, cancellationToken);
                lastReadUtc = DateTimeOffset.UtcNow;

                if (readings.Count == 0)
                {
                    // 能打开硬件树但一个可读传感器都没有：与"完全不支持"区分开，原因文案不同
                    lastSnapshot = SensorSnapshot.Unavailable("未检测到可读传感器（虚拟机或驱动未暴露）。");
                    return lastSnapshot.Success();
                }

                lastSnapshot = new SensorSnapshot(true, null, readings)
                {
                    CpuTemperatureNeedsKernelDriver = NeedsKernelDriverNotice(readings),
                    CpuTemperatureNeedsElevation = NeedsElevationNotice(readings),
                };
                return lastSnapshot.Success();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // 驱动/权限/ABI 不匹配都收敛为"不可用"，并只降级一次，避免每 2 秒重复失败
                MarkUnsupported($"传感器初始化失败：{exception.GetType().Name}");
                return BuildUnavailableResult();
            }
        }
    }

    /// <summary>
    /// 判断是否该提示用户"CPU 温度读不到是因为缺内核驱动"。
    ///
    /// 条件：一条 CPU 温度都没有 **且** PawnIO 确实不在。
    /// 如果 PawnIO 在、但依然读不到 CPU 温度，那是别的原因（CPU 型号未支持等），
    /// 不应误报成驱动缺失 —— 所以两个条件必须同时满足。
    /// </summary>
    private static bool NeedsKernelDriverNotice(List<SensorReading> readings)
        => !HasCpuTemperature(readings) && !SensorDiagnostics.IsPawnIoAvailable();

    /// <summary>
    /// 判断是否该提示"PawnIO 已装，但需要管理员权限才能读 CPU 温度"。
    ///
    /// 2026-09-23 实测确认这是**另一种独立的失败模式**：装好 PawnIO 后，
    /// 非提权进程依然读不到任何 CPU 温度/倍频/功耗（LHM 要通过 PawnIO 加载内核模块，
    /// 该动作需要管理员）；提权后同一份代码读到 47 个传感器而不是 39 个。
    /// 若不单独识别，用户会以为是 PawnIO 没装好而反复重装。
    /// </summary>
    private static bool NeedsElevationNotice(List<SensorReading> readings)
        => !HasCpuTemperature(readings) && SensorDiagnostics.IsPawnIoInstalledButNotElevated();

    private static bool HasCpuTemperature(List<SensorReading> readings)
    {
        foreach (var reading in readings)
        {
            if (reading.HardwareClass == SensorHardwareClass.Cpu && reading.Kind == SensorKind.Temperature)
            {
                return true;
            }
        }

        return false;
    }

    private Result<SensorSnapshot> BuildUnavailableResult()
    {
        lastSnapshot ??= SensorSnapshot.Unavailable(unavailableReason ?? "传感器不可用。");
        return lastSnapshot.Success();
    }

    private void MarkUnsupported(string reason)
    {
        isSupported = false;
        unavailableReason = reason;
        lastSnapshot = SensorSnapshot.Unavailable(reason);
    }

    private void EnsureComputerInitialized()
    {
        if (computer is not null)
        {
            return;
        }

        var instance = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            // 刻意不启用内存与网络控制器：本任务只要温度/风扇/电压，
            // 少开一类硬件就少一次驱动调用与潜在的冲突面。
            IsMemoryEnabled = false,
            IsNetworkEnabled = false,
            IsControllerEnabled = false,
            IsPsuEnabled = false,
            IsBatteryEnabled = false,
        };

        instance.Open();
        computer = instance;

        if (instance.Hardware.Count == 0)
        {
            MarkUnsupported("当前环境未暴露任何硬件传感器。");
            return;
        }

        // CPU 的核心/封装温度传感器是**延迟挂载**的：Open() 之后立刻读
        // 往往只有 CPU 总负载与频率，温度要到第一次 Update() 之后才出现在
        // hardware.Sensors 里（SuperIO 侧的主板温度同理）。
        // 这里做一次预热 Update，否则首个快照会"看起来没有 CPU 温度"。
        WarmUp(instance);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                computer?.Close();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // 关闭失败不影响进程退出；驱动可能在睡眠唤醒后已失效
            }

            computer = null;
        }
    }

    /// <summary>把指定硬件大类的最高温格式化为 UI 文案；无数据时统一显示"不可用"。</summary>
    internal static string DescribeTemperature(SensorSnapshot snapshot, SensorHardwareClass hardwareClass)
    {
        var best = snapshot.MaxTemperatureOf(hardwareClass);
        return best <= 0
            ? "不可用"
            : string.Create(CultureInfo.InvariantCulture, $"{best:F1} ℃");
    }
}