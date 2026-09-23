using System.Globalization;
using System.Text;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 系统信息报告导出器（T1.6）。渲染为纯函数，写文件单独一层，便于单测直接断言文本。
/// </summary>
public sealed partial class ReportExporter : IReportExporter
{
    internal const string ReportHeading = "SysSuite 系统信息报告";

    public string Render(HardwareInfo info, ReportFormat format, DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        var stamp = generatedAt ?? DateTimeOffset.Now;
        return format switch
        {
            ReportFormat.Html => RenderHtml(info, stamp),
            ReportFormat.Json => RenderJson(info, stamp),
            _ => RenderText(info, stamp)
        };
    }

    public async Task<Result<string>> ExportAsync(
        HardwareInfo info,
        ReportFormat format,
        string path,
        DateTimeOffset? generatedAt = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Result<string>(ErrorType.InvalidInput, "导出路径不能为空。");
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = Render(info, format, generatedAt);

            // UTF-8 无 BOM：JSON 解析器与主流编辑器均按 UTF-8 处理；HTML 由 meta charset 声明
            await File.WriteAllTextAsync(fullPath, content, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            return fullPath.Success();
        }
        catch (UnauthorizedAccessException exception)
        {
            return new Result<string>(ErrorType.AccessDenied, $"没有写入权限：{exception.Message}");
        }
        catch (IOException exception)
        {
            return new Result<string>(ErrorType.Internal, $"写入失败：{exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return new Result<string>(ErrorType.InvalidInput, $"路径无效：{exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return new Result<string>(ErrorType.InvalidInput, $"路径格式不受支持：{exception.Message}");
        }
    }

    /// <summary>按 1024 进制格式化容量；与系统信息页的显示口径保持一致。</summary>
    internal static string FormatBytes(long value)
    {
        return Math.Abs(value) switch
        {
            >= 1024L * 1024 * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d / 1024d / 1024d:N1} TB"),
            >= 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d / 1024d:N1} GB"),
            >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d:N1} MB"),
            >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d:N1} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{value:N0} B")
        };
    }

    internal static string FormatBytes(ulong value) => FormatBytes((long)Math.Min(value, long.MaxValue));

    internal static string FormatTimestamp(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    /// <summary>空值时给出统一的占位文案，避免导出报告出现空白字段。</summary>
    internal static string OrUnknown(string? value, string fallback = "未知")
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string DescribeMemory(ulong bytes)
        => bytes == 0 ? "未知" : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d / 1024d / 1024d:N1} GB");

    private static string DescribeGraphicsMemory(ulong bytes)
        => bytes > 0 ? FormatBytes(bytes) : "未知";
}
