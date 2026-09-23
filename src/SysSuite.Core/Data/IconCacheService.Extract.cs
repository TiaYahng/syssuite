using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SysSuite.Core.Abstractions;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService
{
    /// <summary>
    /// 单个图标抽取的时间上限。
    ///
    /// <c>Icon.ExtractAssociatedIcon</c> / <c>ExtractIconEx</c> 会走 Windows shell 的
    /// 图标解析管线，对某些畸形或老式 PE 可能陷入漫长的内部回退 ——
    /// 实测 `IsUninst.exe` 单个耗时 **32 秒**（见 <see cref="IsLegacyInstallerHost"/> 的注释）。
    /// 图标只是装饰，绝不能让它拖住整表刷新，因此**必须**有硬上限。
    ///
    /// 取值权衡（不要把 2 秒当"够用"）：
    ///  - 正常大体积 exe（如 .NET 宿主的 testhost.exe）在**机器负载高时**可能接近 1 秒，
    ///    阈值设太紧会在并发刷新时误杀正常图标（实测踩过：单独跑 671ms，并行下超 2s 被判超时）；
    ///  - 真正病态的样本在 30 秒量级，与正常样本差一到两个数量级，
    ///    因此 5 秒既能挡住病态样本，又给正常样本留足余量。
    /// </summary>
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(5);

    private static Result<string?> ExtractFirstAvailableIcon(string stableKey, IReadOnlyList<IconSource> sourcePaths, string destinationPath)
    {
        Result<string?>? lastResult = null;
        foreach (var sourcePath in sourcePaths)
        {
            if (!TryExtractIconWithinBudget(stableKey, sourcePath, destinationPath, out var result))
            {
                // 超时：换下一个候选源；若全部超时，最终会返回 NotFound 走占位图标
                lastResult = new Result<string?>(ErrorType.Cancelled, "图标抽取超时，已跳过。", null);
                continue;
            }

            if (result.IsSuccess && result.Value is not null)
            {
                return result;
            }

            lastResult = result;
        }

        return lastResult ?? new Result<string?>(ErrorType.NotFound, "未找到可提取的图标。", null);
    }

    /// <summary>
    /// 在时间预算内抽取图标。超时返回 false（此时后台线程可能仍在跑，
    /// 但它是 <see cref="Task.Run(Action, CancellationToken)"/> 的独立任务，
    /// 不会阻塞调用方；进程退出时随之结束）。
    /// </summary>
    private static bool TryExtractIconWithinBudget(
        string stableKey,
        IconSource source,
        string destinationPath,
        out Result<string?> result)
    {
        // PNG 只是拷文件，无需超时保护
        if (Path.GetExtension(source.Path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            && source.IconIndex == 0)
        {
            result = ExtractIcon(stableKey, source, destinationPath);
            return true;
        }

        using var cts = new CancellationTokenSource(ExtractionTimeout);
        var task = Task.Run(() => ExtractIcon(stableKey, source, destinationPath), cts.Token);
        try
        {
            if (!task.Wait(ExtractionTimeout))
            {
                cts.Cancel();
                result = new Result<string?>(ErrorType.Cancelled, "图标抽取超时，已跳过。", null);
                return false;
            }

            result = task.Result;
            return true;
        }
        catch (AggregateException exception) when (exception.InnerException is OperationCanceledException)
        {
            result = new Result<string?>(ErrorType.Cancelled, "图标抽取超时，已跳过。", null);
            return false;
        }
    }

    private static Result<string?> ExtractIcon(string stableKey, IconSource source, string destinationPath)
    {
        try
        {
            var extension = Path.GetExtension(source.Path);
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                if (source.IconIndex != 0)
                {
                    return new Result<string?>(ErrorType.NotFound, "PNG 图标不支持图标索引。", null);
                }

                File.Copy(source.Path, destinationPath, true);
                return new Result<string?>(ErrorType.None, string.Empty, destinationPath);
            }

            using var bitmap = extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? new Icon(source.Path).ToBitmap()
                : ExtractIconBitmap(source.Path, source.IconIndex) ?? ExtractAssociatedIconBitmap(source.Path);
            if (bitmap is null)
            {
                return new Result<string?>(ErrorType.NotFound, "未找到可提取的图标。", null);
            }

            var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
            bitmap.Save(temporaryPath, ImageFormat.Png);
            File.Move(temporaryPath, destinationPath, true);
            return new Result<string?>(ErrorType.None, string.Empty, destinationPath);
        }
        catch (Exception exception)
        {
            return new Result<string?>(ErrorType.Internal, $"{stableKey}: {exception.Message}", null);
        }
    }

    private static Bitmap? ExtractIconBitmap(string path, int iconIndex)
    {
        if (ExtractIconEx(path, iconIndex, out var largeIcon, out _, 1) == 0 || largeIcon == nint.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(largeIcon);
            return (Bitmap?)icon.ToBitmap();
        }
        finally
        {
            _ = DestroyIcon(largeIcon);
        }
    }

    private static Bitmap? ExtractAssociatedIconBitmap(string path)
    {
        using var icon = Icon.ExtractAssociatedIcon(path);
        return icon?.ToBitmap();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        out nint largeIcon,
        out nint smallIcon,
        uint icons);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int DestroyIcon(nint icon);
}
