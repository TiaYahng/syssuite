using System.Diagnostics;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// T1.5 磁盘测量。
///
/// 两项安全约束（对应 T1.5 验收"磁盘测试页显式声明写入影响"）：
///  1. 默认 <see cref="BenchmarkDiskScope.ReadOnly"/>：只在临时目录写入**测试文件**，
///     但读取路径不碰用户数据；写盘范围严格限制在 <c>%TEMP%\SysSuite-Benchmark</c>。
///  2. 无论成功、失败还是取消，测试文件都在 finally 中删除。
/// </summary>
public sealed partial class BenchmarkService
{
    private const int SequentialBlockBytes = 1024 * 1024;   // 1M 顺序
    private const int RandomBlockBytes = 4 * 1024;          // 4K 随机
    private const int TestFileMegabytes = 256;              // 兼顾测速稳定性与磁盘占用

    private static BenchmarkScore RunDiskReadOnlyStage(string directory, BenchmarkOptions options, CancellationToken token)
    {
        var path = Path.Combine(directory, "readonly-probe.bin");
        try
        {
            return MeasureSequential(directory, options.DiskDuration, includeWrite: false, path, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BenchmarkScore.Failed("disk", "磁盘顺序 + 随机", $"只读测试无法进行：{exception.Message}");
        }
    }

    private static BenchmarkScore RunDiskWriteThroughStage(string directory, BenchmarkOptions options, CancellationToken token)
    {
        var path = Path.Combine(directory, "write-probe.bin");
        try
        {
            return MeasureSequential(directory, options.DiskDuration, includeWrite: true, path, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BenchmarkScore.Failed("disk", "磁盘顺序 + 随机", $"写入测试无法进行：{exception.Message}");
        }
    }

    /// <summary>
    /// 磁盘测量主体。先用 <see cref="FileOptions.WriteThrough"/> 建测试文件绕过系统缓存，
    /// 再分别测 1M 顺序写入吞吐与 4K 随机读取 IOPS。
    /// </summary>
    private static BenchmarkScore MeasureSequential(
        string directory,
        TimeSpan duration,
        bool includeWrite,
        string path,
        CancellationToken token)
    {
        _ = directory;
        var totalBytes = (long)TestFileMegabytes * 1024 * 1024;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                SequentialBlockBytes,
                // WriteThrough：让测量反映介质真实吞吐，而不是内存缓存
                FileOptions.WriteThrough | FileOptions.SequentialScan);

            var buffer = new byte[SequentialBlockBytes];
            buffer.AsSpan().Fill(0x5A);

            if (includeWrite)
            {
                var writeWatch = Stopwatch.StartNew();
                var written = 0L;
                while (written < totalBytes && writeWatch.Elapsed < duration && !token.IsCancellationRequested)
                {
                    stream.Write(buffer, 0, buffer.Length);
                    written += buffer.Length;
                }

                stream.Flush(true);
                writeWatch.Stop();
                if (written == 0)
                {
                    return BenchmarkScore.Failed("disk", "磁盘顺序 + 随机", "未采集到写入样本。");
                }

                var writeThroughput = written / Math.Max(writeWatch.Elapsed.TotalSeconds, 0.001);
                return BuildDiskScore(path, writeThroughput, MeasureRandomReads(stream, duration, token), writeWatch.Elapsed);
            }

            // 只读路径：测试文件由本次运行创建（内容为空白稀疏文件），不复用也不触碰用户文件
            var seedWatch = Stopwatch.StartNew();
            for (var written = 0L; written < Math.Min(totalBytes, 64L * 1024 * 1024) && seedWatch.Elapsed < duration; written += buffer.Length)
            {
                stream.Write(buffer, 0, buffer.Length);
            }

            stream.Flush(true);
            var readWatch = Stopwatch.StartNew();
            var read = 0L;
            var readBuffer = new byte[SequentialBlockBytes];
            while (read < totalBytes && readWatch.Elapsed < duration && !token.IsCancellationRequested)
            {
                var chunk = stream.Read(readBuffer, 0, readBuffer.Length);
                if (chunk <= 0)
                {
                    stream.Position = 0;
                    continue;
                }

                read += chunk;
            }

            readWatch.Stop();
            if (read == 0)
            {
                return BenchmarkScore.Failed("disk", "磁盘顺序 + 随机", "未采集到读取样本。");
            }

            var throughput = read / Math.Max(readWatch.Elapsed.TotalSeconds, 0.001);
            return BuildDiskScore(path, throughput, MeasureRandomReads(stream, duration, token), readWatch.Elapsed);
        }
        finally
        {
            // 无论成功/失败/取消都必须清掉测试文件：绝不把探针文件留在用户磁盘上
            TryDelete(path);
        }
    }

    /// <summary>4K 随机读 IOPS：用确定性伪随机偏移，保证两次跑分命中相同扇区序列。</summary>
    private static double MeasureRandomReads(FileStream stream, TimeSpan duration, CancellationToken token)
    {
        if (stream.Length < RandomBlockBytes * 2)
        {
            return 0;
        }

        var buffer = new byte[RandomBlockBytes];
        var blockCount = (int)Math.Min(stream.Length / RandomBlockBytes, int.MaxValue);
        var state = 0x9E3779B9u;
        var operations = 0L;
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < duration && !token.IsCancellationRequested)
        {
            state = (state * 1664525u) + 1013904223u;
            var block = (long)(state % (uint)blockCount);
            stream.Position = block * RandomBlockBytes;
            if (stream.Read(buffer, 0, buffer.Length) <= 0)
            {
                break;
            }

            operations++;
        }

        watch.Stop();
        return operations <= 0 || watch.Elapsed.TotalSeconds <= 0 ? 0 : operations / watch.Elapsed.TotalSeconds;
    }

    private static BenchmarkScore BuildDiskScore(string path, double sequentialBytesPerSecond, double randomIops, TimeSpan elapsed)
    {
        var sequentialScore = sequentialBytesPerSecond / BaselineDiskSequentialBytesPerSecond * CompositeScale;
        var randomScore = randomIops > 0 ? randomIops / BaselineDiskRandomIops * CompositeScale : 0;

        // 随机 IOPS 对小文件与系统盘体验影响更大，给 60% 权重
        var score = Math.Round(randomScore > 0
            ? (sequentialScore * 0.4) + (randomScore * 0.6)
            : sequentialScore, 0);

        return new BenchmarkScore(
            "disk",
            "磁盘顺序 + 随机",
            score,
            $"{sequentialBytesPerSecond / 1024d / 1024d / 1024d:N2} GB/s · {randomIops:N0} IOPS",
            elapsed,
            true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 删除失败不影响结果：文件位于 %TEMP%，由系统清理机制兜底
        }
    }
}
