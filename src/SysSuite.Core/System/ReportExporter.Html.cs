using System.Globalization;
using System.Net;
using System.Text;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class ReportExporter
{
    /// <summary>内嵌样式：自包含、可离线打开、打印友好（浅色底更适合纸质与 PDF）。</summary>
    private const string HtmlStyleSheet = """
        :root { color-scheme: light; }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 32px 20px; background: #f5f6f8; color: #1f2328;
               font-family: "Segoe UI", "Microsoft YaHei", system-ui, sans-serif; font-size: 14px; line-height: 1.6; }
        main { max-width: 880px; margin: 0 auto; }
        header { padding-bottom: 12px; border-bottom: 2px solid #d8dbe0; margin-bottom: 18px; }
        h1 { margin: 0; font-size: 22px; }
        .meta { margin: 6px 0 0; color: #5c6470; font-size: 13px; }
        section { background: #fff; border: 1px solid #e2e5ea; border-radius: 10px; padding: 14px 16px; margin-bottom: 14px; }
        h2 { margin: 0 0 10px; font-size: 15px; color: #2c333c; }
        table { width: 100%; border-collapse: collapse; }
        th, td { text-align: left; padding: 5px 8px; border-bottom: 1px solid #eef0f3; vertical-align: top; font-size: 13px; }
        tr:last-child th, tr:last-child td { border-bottom: none; }
        th { width: 140px; color: #5c6470; font-weight: 600; }
        td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
        .empty { color: #8a929e; }
        .card + .card { margin-top: 10px; padding-top: 10px; border-top: 1px dashed #e2e5ea; }
        .card-title { margin: 0 0 6px; font-size: 13px; font-weight: 600; color: #1f2328; }
        footer { margin-top: 18px; color: #8a929e; font-size: 12px; text-align: center; }
        @media print { body { background: #fff; padding: 0; } section { break-inside: avoid; } }
        """;

    private static string RenderHtml(HardwareInfo info, DateTimeOffset generatedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"zh-CN\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.Append("<title>").Append(Html(ReportHeading)).AppendLine("</title>");
        builder.AppendLine("<style>").AppendLine(HtmlStyleSheet).AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.AppendLine("<header>");
        builder.Append("<h1>").Append(Html(ReportHeading)).AppendLine("</h1>");
        builder.Append("<p class=\"meta\">生成时间：").Append(Html(FormatTimestamp(generatedAt))).AppendLine("</p>");
        builder.AppendLine("</header>");

        AppendHtmlBasicSection(builder, info);
        AppendHtmlStorageSection(builder, info);
        AppendHtmlDriveSection(builder, info);
        AppendHtmlGraphicsSection(builder, info);
        AppendHtmlNetworkSection(builder, info);

        builder.AppendLine("<footer>由 SysSuite 生成</footer>");
        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendHtmlBasicSection(StringBuilder builder, HardwareInfo info)
    {
        OpenSection(builder, "基本信息");
        OpenTable(builder);
        AppendHtmlRow(builder, "计算机名", OrUnknown(info.ComputerName));
        AppendHtmlRow(builder, "操作系统", OrUnknown(info.OperatingSystem));
        AppendHtmlRow(builder, "系统版本", OrUnknown(info.OsVersion));
        AppendHtmlRow(builder, "处理器", OrUnknown(info.CpuName));
        AppendHtmlRow(builder, "物理处理器", info.PhysicalProcessors.ToString("N0", CultureInfo.InvariantCulture));
        AppendHtmlRow(builder, "逻辑处理器", info.LogicalProcessors.ToString("N0", CultureInfo.InvariantCulture));
        AppendHtmlRow(builder, "内存容量", DescribeMemory(info.TotalPhysicalMemory));
        AppendHtmlRow(builder, "主板", OrUnknown(info.Motherboard));
        AppendHtmlRow(builder, "BIOS", OrUnknown(info.BiosVersion));
        CloseTable(builder);
        CloseSection(builder);
    }

    private static void AppendHtmlStorageSection(StringBuilder builder, HardwareInfo info)
    {
        OpenSection(builder, "存储设备");
        if (info.Storage.Count == 0)
        {
            builder.AppendLine("<p class=\"empty\">未检测到存储设备。</p>");
        }
        else
        {
            OpenTable(builder);
            foreach (var disk in info.Storage)
            {
                AppendHtmlRow(builder, OrUnknown(disk.Model), FormatBytes(disk.SizeBytes));
            }

            CloseTable(builder);
        }

        CloseSection(builder);
    }

    private static void AppendHtmlDriveSection(StringBuilder builder, HardwareInfo info)
    {
        OpenSection(builder, "盘符使用情况");
        if (info.Drives.Count == 0)
        {
            builder.AppendLine("<p class=\"empty\">未检测到盘符。</p>");
        }
        else
        {
            builder.AppendLine("<table>");
            builder.AppendLine("<tr><th>盘符</th><th>卷标</th><th class=\"num\">总容量</th><th class=\"num\">已用</th><th class=\"num\">使用率</th></tr>");
            foreach (var drive in info.Drives)
            {
                builder.Append("<tr><td>").Append(Html(OrUnknown(drive.DriveLetter)))
                    .Append("</td><td>").Append(Html(OrUnknown(drive.Label)))
                    .Append("</td><td class=\"num\">").Append(Html(FormatBytes(drive.TotalBytes)))
                    .Append("</td><td class=\"num\">").Append(Html(FormatBytes(drive.UsedBytes)))
                    .Append("</td><td class=\"num\">")
                    .Append(drive.UsedPercent.ToString("F1", CultureInfo.InvariantCulture))
                    .AppendLine("%</td></tr>");
            }

            builder.AppendLine("</table>");
        }

        CloseSection(builder);
    }

    private static void AppendHtmlGraphicsSection(StringBuilder builder, HardwareInfo info)
    {
        OpenSection(builder, "显卡");
        if (info.GraphicsCards.Count == 0)
        {
            builder.AppendLine("<p class=\"empty\">未检测到显示适配器。</p>");
        }
        else
        {
            foreach (var card in info.GraphicsCards)
            {
                builder.AppendLine("<div class=\"card\">");
                builder.Append("<p class=\"card-title\">").Append(Html(OrUnknown(card.Name)))
                    .Append(" · ").Append(Html(OrUnknown(card.CategoryDescription))).AppendLine("</p>");
                OpenTable(builder);
                AppendHtmlRow(builder, "厂商", OrUnknown(card.Manufacturer));
                AppendHtmlRow(builder, "显存", DescribeGraphicsMemory(card.MemoryBytes));
                AppendHtmlRow(builder, "驱动", OrUnknown(card.DriverVersion));
                AppendHtmlRow(builder, "输出", OrUnknown(card.VideoModeDescription));
                CloseTable(builder);
                builder.AppendLine("</div>");
            }
        }

        CloseSection(builder);
    }

    private static void AppendHtmlNetworkSection(StringBuilder builder, HardwareInfo info)
    {
        OpenSection(builder, "网络适配器");
        if (info.NetworkAdapters.Count == 0)
        {
            builder.AppendLine("<p class=\"empty\">未检测到网络适配器。</p>");
        }
        else
        {
            OpenTable(builder);
            foreach (var adapter in info.NetworkAdapters)
            {
                var state = adapter.IsEnabled ? "已启用" : "未启用";
                AppendHtmlRow(builder, OrUnknown(adapter.Name), string.Concat(OrUnknown(adapter.MacAddress), " · ", state));
            }

            CloseTable(builder);
        }

        CloseSection(builder);
    }

    private static void OpenSection(StringBuilder builder, string title)
    {
        builder.AppendLine("<section>");
        builder.Append("<h2>").Append(Html(title)).AppendLine("</h2>");
    }

    private static void CloseSection(StringBuilder builder) => builder.AppendLine("</section>");

    private static void OpenTable(StringBuilder builder) => builder.AppendLine("<table>");

    private static void CloseTable(StringBuilder builder) => builder.AppendLine("</table>");

    private static void AppendHtmlRow(StringBuilder builder, string label, string value)
    {
        builder.Append("<tr><th>").Append(Html(label))
            .Append("</th><td>").Append(Html(value))
            .AppendLine("</td></tr>");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
