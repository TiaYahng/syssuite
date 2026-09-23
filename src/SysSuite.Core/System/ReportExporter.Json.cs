using System.Text.Encodings.Web;
using System.Text.Json;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class ReportExporter
{
    /// <summary>
    /// UnsafeRelaxedJsonEscaping 让中文以原字符输出（而非 \uXXXX），报告可直接阅读；
    /// 报告内容不含 HTML 上下文，转义放宽不引入注入面。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string RenderJson(HardwareInfo info, DateTimeOffset generatedAt)
        => JsonSerializer.Serialize(new HardwareReport(generatedAt, info), JsonOptions);

    /// <summary>供测试与外部工具复用：按同一份选项把 JSON 反序列化回报告信封。</summary>
    internal static HardwareReport? Deserialize(string json)
        => JsonSerializer.Deserialize<HardwareReport>(json, JsonOptions);
}
