using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SysSuite.Core.Abstractions;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService
{
    private static Result<string?> ExtractFirstAvailableIcon(string stableKey, IReadOnlyList<IconSource> sourcePaths, string destinationPath)
    {
        Result<string?>? lastResult = null;
        foreach (var sourcePath in sourcePaths)
        {
            var result = ExtractIcon(stableKey, sourcePath, destinationPath);
            if (result.IsSuccess && result.Value is not null)
            {
                return result;
            }

            lastResult = result;
        }

        return lastResult ?? new Result<string?>(ErrorType.NotFound, "未找到可提取的图标。", null);
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
