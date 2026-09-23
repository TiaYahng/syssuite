namespace SysSuite.Core.Abstractions.System;

/// <summary>单项基准的结果。分数经归一化，基准机 = 10000 分。</summary>
public sealed record BenchmarkScore(
    string Id,
    string Name,
    double Score,
    string Detail,
    TimeSpan Duration,
    bool Succeeded)
{
    public static BenchmarkScore Failed(string id, string name, string reason)
        => new(id, name, 0, reason, TimeSpan.Zero, false);
}

/// <summary>整套基准结果。<see cref="Composite"/> 只在 CPU 与内存均成功时给出。</summary>
public sealed record BenchmarkReport(
    DateTimeOffset RunAt,
    string MachineSummary,
    IReadOnlyList<BenchmarkScore> Scores)
{
    /// <summary>综合分：CPU 与内存各占一半权重；磁盘不参与（受介质类型影响过大，不可比）。</summary>
    public double? Composite
    {
        get
        {
            var cpu = Find("cpu");
            var memory = Find("memory");
            if (cpu is not { Succeeded: true } || memory is not { Succeeded: true })
            {
                return null;
            }

            return Math.Round((cpu.Score + memory.Score) / 2, 0);
        }
    }

    public BenchmarkScore? Find(string id)
    {
        foreach (var score in Scores)
        {
            if (string.Equals(score.Id, id, StringComparison.Ordinal))
            {
                return score;
            }
        }

        return null;
    }
}

/// <summary>磁盘基准的写入范围，必须在 UI 上显式提示用户。</summary>
public enum BenchmarkDiskScope
{
    /// <summary>只跑顺序读（无写入，绝对安全）。</summary>
    ReadOnly = 0,

    /// <summary>在临时目录写入测试文件，结束后删除（默认）。</summary>
    TemporaryFile = 1,
}

public sealed record BenchmarkOptions
{
    /// <summary>CPU 与内存各自的计时长度。</summary>
    public TimeSpan CpuDuration { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MemoryDuration { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan DiskDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>是否包含磁盘项目。磁盘测试有写入影响，UI 必须让用户显式选择。</summary>
    public bool IncludeDisk { get; init; }

    public BenchmarkDiskScope DiskScope { get; init; } = BenchmarkDiskScope.ReadOnly;

    /// <summary>磁盘测试文件的落盘目录；为空时使用系统临时目录。</summary>
    public string? DiskTestDirectory { get; init; }

    public static BenchmarkOptions Quick { get; } = new();

    public static BenchmarkOptions Full { get; } = new BenchmarkOptions
    {
        CpuDuration = TimeSpan.FromSeconds(6),
        MemoryDuration = TimeSpan.FromSeconds(4),
        DiskDuration = TimeSpan.FromSeconds(8),
        IncludeDisk = true,
    };
}

/// <summary>基准测试进度。UI 跑分向导据此驱动进度条与阶段文案。</summary>
public sealed record BenchmarkProgress(string StageId, string StageName, double Percent);

public interface IBenchmarkService
{
    /// <summary>进度回调在后台线程触发，UI 需自行封送。</summary>
    event EventHandler<BenchmarkProgress>? ProgressChanged;

    /// <summary>
    /// 执行基准测试。整体 CPU 密集，默认应在后台线程调用。
    /// 取消时返回已完成的那些项目的部分结果，而不是失败。
    /// </summary>
    Task<Result<BenchmarkReport>> RunAsync(
        BenchmarkOptions options,
        string? machineSummary = null,
        CancellationToken cancellationToken = default);
}
