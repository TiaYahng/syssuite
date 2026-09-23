using System.Diagnostics;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 基准测试服务（T1.5，托管实现）。
///
/// 计划原本要求 C++ 内核（bench/ 子项目）；这里用托管实现落地，原因与偏差登记见 docs/PLAN.md §11 D13。
/// 关键点在于**两次空跑校准**：JIT 会显著影响首轮吞吐，若不预热，同一台机器两次跑分方差可达 20%，
/// 无法满足"双跑方差 &lt; 3%"的验收要求。
///
/// 评分归一：以 <see cref="BaselineCpuScore"/> 等基准机参考值为分母，缩放至 10000 分制。
/// </summary>
public sealed partial class BenchmarkService : IBenchmarkService
{
    // 基准机参考吞吐（约等于 2020 年主流桌面 CPU / 双通道 DDR4 的实测值），
    // 只用于把原始吞吐映射到 10000 分制；数值变更会改变历史分数可比性，故集中在此处。
    private const double BaselineCpuOperationsPerSecond = 420_000_000d;
    private const double BaselineMemoryBytesPerSecond = 18_000_000_000d;
    private const double BaselineMemoryLatencyNanoseconds = 95d;
    private const double BaselineDiskSequentialBytesPerSecond = 1_500_000_000d;
    private const double BaselineDiskRandomIops = 45_000d;

    private const double CompositeScale = 10000d;

    public event EventHandler<BenchmarkProgress>? ProgressChanged;

    public async Task<Result<BenchmarkReport>> RunAsync(
        BenchmarkOptions options,
        string? machineSummary = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var runAt = DateTimeOffset.Now;
        var scores = new List<BenchmarkScore>();

        // 全部项目在后台线程执行：CPU 项目是纯计算，放 UI 线程会直接卡死界面
        await Task.Run(
            () => Execute(options, scores, cancellationToken),
            CancellationToken.None).ConfigureAwait(false);

        return new BenchmarkReport(runAt, machineSummary ?? Environment.MachineName, scores).Success();
    }

    private void Execute(BenchmarkOptions options, List<BenchmarkScore> scores, CancellationToken cancellationToken)
    {
        var stages = BuildStageList(options);
        var index = 0;

        foreach (var stage in stages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // 取消返回部分结果：已完成的分数仍然有价值，不做全盘丢弃
                scores.Add(BenchmarkScore.Failed(stage.Id, stage.Name, "已取消"));
                continue;
            }

            index++;
            var percent = index * 100d / stages.Count;
            ProgressChanged?.Invoke(this, new BenchmarkProgress(stage.Id, stage.Name, percent));

            try
            {
                scores.Add(stage.Run(options, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                scores.Add(BenchmarkScore.Failed(stage.Id, stage.Name, "已取消"));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // 单项失败不拖垮整套：磁盘测试尤其容易因权限/介质问题失败
                scores.Add(BenchmarkScore.Failed(stage.Id, stage.Name, $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        ProgressChanged?.Invoke(this, new BenchmarkProgress("done", "完成", 100));
    }

    private static List<BenchmarkStage> BuildStageList(BenchmarkOptions options)
    {
        var stages = new List<BenchmarkStage>
        {
            new("cpu", "CPU 整型 + 浮点", (opt, token) => RunCpuStage(opt, token)),
            new("memory", "内存带宽 + 延迟", (opt, token) => RunMemoryStage(opt, token)),
        };

        if (options.IncludeDisk)
        {
            stages.Add(new BenchmarkStage("disk", "磁盘顺序 + 随机", RunDiskStage));
        }

        return stages;
    }

    private sealed record BenchmarkStage(
        string Id,
        string Name,
        Func<BenchmarkOptions, CancellationToken, BenchmarkScore> Run);

    private static BenchmarkScore RunCpuStage(BenchmarkOptions options, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();

        // 空跑校准：预热 JIT 并让 CPU 进入高频状态，否则首轮分数系统性偏低
        WarmUpCpu();

        var operations = 0L;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < options.CpuDuration && !token.IsCancellationRequested)
        {
            operations += CpuWorkload();
        }

        var elapsed = deadline.Elapsed;
        watch.Stop();
        if (operations == 0 || elapsed <= TimeSpan.Zero)
        {
            return BenchmarkScore.Failed("cpu", "CPU 整型 + 浮点", "计时过短，未采集到有效样本。");
        }

        var perSecond = operations / elapsed.TotalSeconds;
        var score = Math.Round(perSecond / BaselineCpuOperationsPerSecond * CompositeScale, 0);
        return new BenchmarkScore(
            "cpu",
            "CPU 整型 + 浮点",
            score,
            $"{perSecond / 1_000_000:N1} M 运算/秒",
            elapsed,
            true);
    }

    private static BenchmarkScore RunMemoryStage(BenchmarkOptions options, CancellationToken token)
    {
        WarmUpMemory();

        var bytesPerSecond = MeasureMemoryBandwidth(options.MemoryDuration, token);
        var latency = MeasureMemoryLatency(token);
        if (bytesPerSecond <= 0)
        {
            return BenchmarkScore.Failed("memory", "内存带宽 + 延迟", "未采集到有效带宽样本。");
        }

        var bandwidthScore = bytesPerSecond / BaselineMemoryBytesPerSecond * CompositeScale;
        // 延迟越低越好，因此基准值 / 实测值
        var latencyScore = latency > 0
            ? BaselineMemoryLatencyNanoseconds / latency * CompositeScale
            : CompositeScale;

        var score = Math.Round((bandwidthScore * 0.75) + (latencyScore * 0.25), 0);
        return new BenchmarkScore(
            "memory",
            "内存带宽 + 延迟",
            score,
            $"{bytesPerSecond / 1024d / 1024d / 1024d:N1} GB/s · {latency:N0} ns",
            TimeSpan.FromSeconds(0),
            true);
    }

    private static BenchmarkScore RunDiskStage(BenchmarkOptions options, CancellationToken token)
    {
        var directory = ResolveDiskTestDirectory(options);
        if (options.DiskScope == BenchmarkDiskScope.ReadOnly)
        {
            return RunDiskReadOnlyStage(directory, options, token);
        }

        return RunDiskWriteThroughStage(directory, options, token);
    }

    private static string ResolveDiskTestDirectory(BenchmarkOptions options)
    {
        var directory = string.IsNullOrWhiteSpace(options.DiskTestDirectory)
            ? Path.Combine(Path.GetTempPath(), "SysSuite-Benchmark")
            : options.DiskTestDirectory;
        Directory.CreateDirectory(directory);
        return directory;
    }
}
