using System.IO;
using System.Windows.Threading;

namespace SysSuite.UI.ViewModels;

public sealed partial class UninstallerViewModel
{
    /// <summary>图标加载并发上限。</summary>
    private const int IconLoadConcurrency = 4;

    /// <summary>进度回报间隔，避免高频 Dispatcher 调度反成瓶颈。</summary>
    private const int IconProgressInterval = 25;

    /// <summary>
    /// 图标加载（M2 效率优化）。
    ///
    /// 图标解析是整条刷新链路里最贵的一段：要读注册表拿 ProductIcon、用 COM 解析
    /// <c>.lnk</c> 快捷方式、必要时还要枚举安装目录里的候选文件。因此刻意与列表渲染解耦：
    /// 先把行渲染出来，再并发补图标，让用户立刻看到内容而不是盯着空列表。
    /// </summary>
    private async Task LoadIconsAsync(List<AppRow> targetRows, CancellationToken cancellationToken)
    {
        if (targetRows.Count == 0)
        {
            return;
        }

        var loaded = 0;
        var total = targetRows.Count;

        try
        {
            await Parallel.ForEachAsync(
                targetRows,
                new ParallelOptions { MaxDegreeOfParallelism = IconLoadConcurrency, CancellationToken = cancellationToken },
                async (row, token) =>
                {
                    try
                    {
                        var iconResult = await iconCacheService.GetIconPathAsync(row.App, token);
                        if (!iconResult.IsSuccess || string.IsNullOrWhiteSpace(iconResult.Value))
                        {
                            return;
                        }

                        // BitmapFrame.Create 会真的去解码文件，必须在后台线程做；
                        // 直接赋值给 row.Icon 即可，绑定层会切回 UI 线程。
                        var frame = await Task.Run(
                            () => System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconResult.Value)),
                            token);
                        row.Icon = frame;

                        var current = Interlocked.Increment(ref loaded);
                        if (current % IconProgressInterval == 0 || current == total)
                        {
                            ReportIconProgress(current, total);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // 图标是装饰性信息，单个失败不该影响整表加载
                        _ = exception;
                    }
                });
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            SetStatus($"已加载 {total} 个应用");
        }
    }

    private void ReportIconProgress(int current, int total)
    {
        _ = uiContext.InvokeAsync(() =>
        {
            // 用户可能已经点了刷新并作废本轮，这时别再改状态栏文案
            if (iconLoadSource is { IsCancellationRequested: false })
            {
                BusyText = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"已加载 {current}/{total} 个图标...");
                IconProgressChanged?.Invoke(this, new IconProgressEventArgs(current, total));
            }
        });
    }
}

/// <summary>图标加载进度事件参数。</summary>
public sealed class IconProgressEventArgs(int current, int total) : EventArgs
{
    public int Current { get; } = current;

    public int Total { get; } = total;
}
