using System.Diagnostics;

namespace SysSuite.Core.System;

/// <summary>
/// T1.5 的测量内核。所有方法都是热点循环，刻意写成静态且不分配：
/// 一旦在测量循环内分配对象，GC 停顿会直接污染分数。
/// </summary>
public sealed partial class BenchmarkService
{
    /// <summary>CPU 工作负载：整型（位运算/乘加）+ 浮点（三角函数近似）混合。</summary>
    private static int CpuWorkload()
    {
        const int Iterations = 4096;
        var accumulator = 0u;
        var floating = 1.0000001d;

        for (var i = 0; i < Iterations; i++)
        {
            // 整型：可预测的乘加链，主要压 ALU
            accumulator = (accumulator * 1664525u) + 1013904223u;
            accumulator ^= accumulator >> 13;

            // 浮点：sqrt + 累加，压 FPU 并避免被优化掉
            floating += Math.Sqrt(i + floating) * 0.0000001d;

            // 除法与取模混入，避免整数流水线被单一指令吞吐掩盖
            if ((i & 3) == 0 && accumulator != 0)
            {
                accumulator /= (accumulator % 97) + 1;
            }
        }

        return Iterations + (accumulator == 0 ? 0 : (int)(floating * 0) & 0);
    }

    private static void WarmUpCpu()
    {
        // 两轮预热：首轮触发 JIT 与内联决策，次轮让频率调节器升到高频档
        for (var round = 0; round < 2; round++)
        {
            _ = CpuWorkload();
        }

        // 缓存未命中场景也预热一次，避免测量期首次触碰大数组产生页错误
        var spike = new double[64 * 1024];
        for (var i = 0; i < spike.Length; i += 64)
        {
            spike[i] = i;
        }

        GC.KeepAlive(spike);
    }

    private static double MeasureMemoryBandwidth(TimeSpan duration, CancellationToken token)
    {
        // 每个块 32 MB：大于典型 L3，保证真的落到内存
        const int BlockBytes = 32 * 1024 * 1024;
        var source = new byte[BlockBytes];
        var destination = new byte[BlockBytes];
        source.AsSpan().Fill(0xA5);

        var totalBytes = 0L;
        var overall = Stopwatch.StartNew();
        while (overall.Elapsed < duration && !token.IsCancellationRequested)
        {
            source.AsSpan(0, BlockBytes).CopyTo(destination);
            totalBytes += BlockBytes * 2L; // 读 + 写：带宽按有效流量计
        }

        var elapsed = overall.Elapsed;
        GC.KeepAlive(source);
        GC.KeepAlive(destination);
        return elapsed.TotalSeconds <= 0 ? 0 : totalBytes / elapsed.TotalSeconds;
    }

    private static void WarmUpMemory()
    {
        var probe = new byte[1024 * 1024];
        probe.AsSpan().Fill(1);
        GC.KeepAlive(probe);
    }

    /// <summary>
    /// 指针追逐法测延迟：每次访问都依赖上一次的结果，
    /// 硬件预取器无法提前拉取，因此测到的是真实内存延迟而非预取后的吞吐。
    /// </summary>
    private static double MeasureMemoryLatency(CancellationToken token)
    {
        // 8 MB 步进表，完全溢出 L2，逼近主存延迟
        const int Slots = 1024 * 1024;
        var hops = new int[Slots];
        for (var i = 0; i < Slots; i++)
        {
            hops[i] = (i * 16777619) % Slots;
        }

        const long TargetHops = 8_000_000;
        var index = 0;
        var completed = 0L;
        var watch = new Stopwatch();

        // 预热一轮，让表进入缓存附近状态
        for (var i = 0; i < Slots; i++)
        {
            index = hops[index];
        }

        watch.Start();
        while (completed < TargetHops && !token.IsCancellationRequested)
        {
            // 每轮 1024 跳，减少循环边界判断对测量的干扰
            for (var i = 0; i < 1024; i++)
            {
                index = hops[index];
            }

            completed += 1024;
        }

        watch.Stop();
        GC.KeepAlive(hops);
        if (completed == 0 || watch.Elapsed.TotalSeconds <= 0)
        {
            return 0;
        }

        return watch.Elapsed.TotalNanoseconds / completed;
    }
}
