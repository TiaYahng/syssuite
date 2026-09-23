using System.Globalization;
using System.Text;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class ReportExporter
{
    /// <summary>文本报告中字段标签列的目标显示宽度（列数）。</summary>
    private const int FieldLabelWidth = 12;

    private static string RenderText(HardwareInfo info, DateTimeOffset generatedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine(ReportHeading);
        builder.AppendLine(CultureInfo.InvariantCulture, $"生成时间：{FormatTimestamp(generatedAt)}");
        builder.AppendLine(new string('=', 48));
        builder.AppendLine();

        builder.AppendLine("【基本信息】");
        AppendField(builder, "计算机名", OrUnknown(info.ComputerName));
        AppendField(builder, "操作系统", OrUnknown(info.OperatingSystem));
        AppendField(builder, "系统版本", OrUnknown(info.OsVersion));
        builder.AppendLine();

        builder.AppendLine("【处理器】");
        AppendField(builder, "型号", OrUnknown(info.CpuName));
        AppendField(builder, "物理处理器", info.PhysicalProcessors.ToString("N0", CultureInfo.InvariantCulture));
        AppendField(builder, "逻辑处理器", info.LogicalProcessors.ToString("N0", CultureInfo.InvariantCulture));
        builder.AppendLine();

        builder.AppendLine("【内存】");
        AppendField(builder, "总容量", DescribeMemory(info.TotalPhysicalMemory));
        builder.AppendLine();

        builder.AppendLine("【主板 / BIOS】");
        AppendField(builder, "主板", OrUnknown(info.Motherboard));
        AppendField(builder, "BIOS", OrUnknown(info.BiosVersion));
        builder.AppendLine();

        AppendStorageSection(builder, info);
        AppendDriveSection(builder, info);
        AppendGraphicsSection(builder, info);
        AppendNetworkSection(builder, info);

        builder.AppendLine(new string('=', 48));
        builder.AppendLine("由 SysSuite 生成");
        return builder.ToString();
    }

    private static void AppendStorageSection(StringBuilder builder, HardwareInfo info)
    {
        builder.AppendLine("【存储设备】");
        if (info.Storage.Count == 0)
        {
            AppendField(builder, "设备", "未检测到");
        }
        else
        {
            foreach (var disk in info.Storage)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  - {OrUnknown(disk.Model)} · {FormatBytes(disk.SizeBytes)}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendDriveSection(StringBuilder builder, HardwareInfo info)
    {
        builder.AppendLine("【盘符使用情况】");
        if (info.Drives.Count == 0)
        {
            AppendField(builder, "盘符", "未检测到");
        }
        else
        {
            foreach (var drive in info.Drives)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"  {OrUnknown(drive.DriveLetter)}  {OrUnknown(drive.Label)}  总容量 {FormatBytes(drive.TotalBytes)}  已用 {FormatBytes(drive.UsedBytes)}  使用率 {drive.UsedPercent:F1}%");
            }
        }

        builder.AppendLine();
    }

    private static void AppendGraphicsSection(StringBuilder builder, HardwareInfo info)
    {
        builder.AppendLine("【显卡】");
        if (info.GraphicsCards.Count == 0)
        {
            AppendField(builder, "适配器", "未检测到");
        }
        else
        {
            foreach (var card in info.GraphicsCards)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  - {OrUnknown(card.Name)} · {OrUnknown(card.CategoryDescription)}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      厂商：{OrUnknown(card.Manufacturer)}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      显存：{DescribeGraphicsMemory(card.MemoryBytes)}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      驱动：{OrUnknown(card.DriverVersion)}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      输出：{OrUnknown(card.VideoModeDescription)}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendNetworkSection(StringBuilder builder, HardwareInfo info)
    {
        builder.AppendLine("【网络适配器】");
        if (info.NetworkAdapters.Count == 0)
        {
            AppendField(builder, "适配器", "未检测到");
        }
        else
        {
            foreach (var adapter in info.NetworkAdapters)
            {
                var state = adapter.IsEnabled ? "已启用" : "未启用";
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"  - {OrUnknown(adapter.Name)} · {OrUnknown(adapter.MacAddress)} · {state}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendField(StringBuilder builder, string label, string value)
        => builder.AppendLine(CultureInfo.InvariantCulture, $"  {PadDisplay(label, FieldLabelWidth)}：{value}");

    /// <summary>
    /// 按显示宽度右侧补空格。C# 的 <c>{label,-12}</c> 按 UTF-16 字符数对齐，
    /// 而中文字符在等宽/终端下占两列，会导致中英混排的标签列错位。
    /// </summary>
    internal static string PadDisplay(string text, int displayWidth)
    {
        var width = DisplayWidth(text);
        return width >= displayWidth ? text : text + new string(' ', displayWidth - width);
    }

    private static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var ch in text)
        {
            width += IsWideCharacter(ch) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWideCharacter(char ch)
        => ch is >= '\u1100' and <= '\u115F'
            or >= '\u2E80' and <= '\u303E'
            or >= '\u3041' and <= '\u33FF'
            or >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uA000' and <= '\uA4CF'
            or >= '\uAC00' and <= '\uD7A3'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\uFE30' and <= '\uFE4F'
            or >= '\uFF00' and <= '\uFF60'
            or >= '\uFFE0' and <= '\uFFE6';
}
