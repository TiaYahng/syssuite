using System.Diagnostics;
using System.Globalization;
using System.Text;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;

namespace SysSuite.Tests;

/// <summary>
/// 卸载器枚举性能基准（M2 效率优化验证用）。
///
/// 刻意不做时间断言 —— CI 机器负载不可控，断言必然抖动导致假失败。
/// 只把耗时落盘到 %TEMP%\syssuite-enum-bench.txt 供人工比对。
/// </summary>
public class UninstallerBenchmarkTests
{
    [Fact]
    public async Task MeasureEnumerationAndIconResolution()
    {
        var report = new StringBuilder();
        var sw = Stopwatch.StartNew();

        var registry = await Task.Run(() => new RegistryAppEnumerator(false).Enumerate());
        var registryMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var store = await Task.Run(() => StoreAppEnumerator.Enumerate());
        var storeMs = sw.ElapsedMilliseconds;

        var registryApps = registry.IsSuccess ? registry.Value ?? [] : [];
        var storeApps = store.IsSuccess ? store.Value ?? [] : [];
        var merged = UninstallCatalogMerger.Merge([.. registryApps, .. storeApps]);

        Append(report, $"RegistryApps = {registryApps.Count.ToString(CultureInfo.InvariantCulture)} ({registryMs} ms)");
        Append(report, $"StoreApps    = {storeApps.Count.ToString(CultureInfo.InvariantCulture)} ({storeMs} ms)");
        Append(report, $"MergedApps   = {merged.Count.ToString(CultureInfo.InvariantCulture)}");
        Append(report, string.Empty);

        using var icons = new IconCacheService(
            Path.Combine(Path.GetTempPath(), "syssuite-icon-bench"));

        // 第一轮：全部图标都没缓存，走完整解析链路（注册表 + COM + 可能的目录枚举）
        sw.Restart();
        var coldLoaded = await LoadAllAsync(icons, merged);
        var coldMs = sw.ElapsedMilliseconds;
        Append(report, $"ColdIconLoad = {coldLoaded.ToString(CultureInfo.InvariantCulture)}/{merged.Count.ToString(CultureInfo.InvariantCulture)} in {coldMs} ms");

        // 第二轮：命中缓存，应当快一个数量级
        sw.Restart();
        var warmLoaded = await LoadAllAsync(icons, merged);
        var warmMs = sw.ElapsedMilliseconds;
        Append(report, $"WarmIconLoad = {warmLoaded.ToString(CultureInfo.InvariantCulture)}/{merged.Count.ToString(CultureInfo.InvariantCulture)} in {warmMs} ms");

        if (coldMs > 0)
        {
            Append(report, string.Create(
                CultureInfo.InvariantCulture,
                $"Speedup      = {(double)coldMs / Math.Max(warmMs, 1):F1}x"));
        }

        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "syssuite-enum-bench.txt"),
            report.ToString(),
            new UTF8Encoding(false));
    }

    private static async Task<int> LoadAllAsync(IconCacheService icons, IReadOnlyList<AppRecord> apps)
    {
        var loaded = 0;
        await Parallel.ForEachAsync(
            apps,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (app, token) =>
            {
                try
                {
                    var result = await icons.GetIconPathAsync(app, token);
                    if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
                    {
                        Interlocked.Increment(ref loaded);
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    _ = exception;
                }
            });
        return loaded;
    }

    private static void Append(StringBuilder builder, string line) => builder.AppendLine(line);
}
