namespace SysSuite.Core.Abstractions.System;

/// <summary>报告导出格式。</summary>
public enum ReportFormat
{
    /// <summary>纯文本（.txt），便于复制与粘贴到工单。</summary>
    Text,

    /// <summary>自包含网页（.html），内嵌样式，可离线打开或打印。</summary>
    Html,

    /// <summary>结构化 JSON（.json），可被其他工具反序列化消费。</summary>
    Json
}

/// <summary>报告格式的元数据（扩展名 / 显示名）。</summary>
public static class ReportFormats
{
    public static IReadOnlyList<ReportFormat> All { get; } =
        [ReportFormat.Text, ReportFormat.Html, ReportFormat.Json];

    public static string GetExtension(ReportFormat format) => format switch
    {
        ReportFormat.Text => ".txt",
        ReportFormat.Html => ".html",
        ReportFormat.Json => ".json",
        _ => ".txt"
    };

    public static string GetDisplayName(ReportFormat format) => format switch
    {
        ReportFormat.Text => "文本",
        ReportFormat.Html => "网页",
        ReportFormat.Json => "JSON",
        _ => "文本"
    };
}

/// <summary>
/// 报告信封：生成时间与硬件快照。
/// JSON 格式即此结构的序列化结果，因此可直接反序列化回本类型。
/// </summary>
public sealed record HardwareReport(DateTimeOffset GeneratedAt, HardwareInfo Hardware);

public interface IReportExporter
{
    /// <summary>
    /// 把硬件快照渲染为指定格式的文本内容。
    /// <paramref name="generatedAt"/> 为空时取当前本地时间；显式传入便于测试断言。
    /// </summary>
    string Render(HardwareInfo info, ReportFormat format, DateTimeOffset? generatedAt = null);

    /// <summary>渲染并写入文件（UTF-8 无 BOM），目录不存在时自动创建。返回实际写入的绝对路径。</summary>
    Task<Result<string>> ExportAsync(
        HardwareInfo info,
        ReportFormat format,
        string path,
        DateTimeOffset? generatedAt = null,
        CancellationToken cancellationToken = default);
}
