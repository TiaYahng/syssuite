using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;
using DriveInfo = SysSuite.Core.Abstractions.System.DriveInfo;

namespace SysSuite.Tests;

/// <summary>
/// T1.6 报告导出：三种格式的内容正确性、编码契约与失败路径。
/// </summary>
public class ReportExporterTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 9, 23, 0, 30, 0, TimeSpan.FromHours(8));

    private readonly ReportExporter exporter = new();

    [Fact]
    public void TextReportCarriesEverySection()
    {
        var text = exporter.Render(SampleInfo(), ReportFormat.Text, Stamp);

        Assert.Contains("SysSuite 系统信息报告", text, StringComparison.Ordinal);
        Assert.Contains("2026-09-23 00:30:00 +08:00", text, StringComparison.Ordinal);
        foreach (var section in new[] { "【基本信息】", "【处理器】", "【内存】", "【主板 / BIOS】", "【存储设备】", "【盘符使用情况】", "【显卡】", "【网络适配器】" })
        {
            Assert.Contains(section, text, StringComparison.Ordinal);
        }

        Assert.Contains("Samsung SSD 980 500GB", text, StringComparison.Ordinal);
        Assert.Contains("465.8 GB", text, StringComparison.Ordinal);
        Assert.Contains("46.7 GB", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TextReportFallsBackToUnknownForBlankFields()
    {
        var info = SampleInfo() with { CpuName = string.Empty, Motherboard = "   " };

        var text = exporter.Render(info, ReportFormat.Text, Stamp);

        // 恰好两处空字段（处理器型号、主板）落回占位文案，其余字段都有实测值
        Assert.Equal(2, text.Split("：未知", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TextReportMarksMissingCollections()
    {
        var info = SampleInfo() with
        {
            Storage = [],
            Drives = [],
            GraphicsCards = [],
            NetworkAdapters = []
        };

        var text = exporter.Render(info, ReportFormat.Text, Stamp);

        Assert.Equal(4, text.Split("未检测到").Length - 1);
    }

    [Fact]
    public void HtmlReportIsSelfContainedWithUtf8Declaration()
    {
        var html = exporter.Render(SampleInfo(), ReportFormat.Html, Stamp);

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Contains("SysSuite 系统信息报告", html, StringComparison.Ordinal);
        Assert.Contains("盘符使用情况", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>" + Environment.NewLine, html, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlReportEscapesUntrustedFieldValues()
    {
        var info = SampleInfo() with { CpuName = "<script>alert('x')</script> & <b>bold</b>" };

        var html = exporter.Render(info, ReportFormat.Html, Stamp);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &lt;b&gt;bold&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonReportRoundTripsToHardwareReport()
    {
        var info = SampleInfo();

        var json = exporter.Render(info, ReportFormat.Json, Stamp);
        var restored = ReportExporter.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(Stamp, restored.GeneratedAt);
        Assert.Equal(info.CpuName, restored.Hardware.CpuName);
        Assert.Equal(info.TotalPhysicalMemory, restored.Hardware.TotalPhysicalMemory);
        Assert.Equal(info.Drives.Count, restored.Hardware.Drives.Count);
        Assert.Equal(info.Drives[0].TotalBytes, restored.Hardware.Drives[0].TotalBytes);
        Assert.Equal(info.GraphicsCards[0].MemoryBytes, restored.Hardware.GraphicsCards[0].MemoryBytes);
        Assert.Equal(info.NetworkAdapters[0].MacAddress, restored.Hardware.NetworkAdapters[0].MacAddress);
    }

    [Fact]
    public void JsonReportKeepsChineseUnescaped()
    {
        var json = exporter.Render(SampleInfo(), ReportFormat.Json, Stamp);

        Assert.Contains("独立显卡", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonReportOmitsDerivedDriveFields()
    {
        var json = exporter.Render(SampleInfo(), ReportFormat.Json, Stamp);

        Assert.DoesNotContain("UsedBytes", json, StringComparison.Ordinal);
        Assert.DoesNotContain("UsedPercent", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportWritesUtf8WithoutBom()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "report.txt");

            var result = await exporter.ExportAsync(SampleInfo(), ReportFormat.Text, path, Stamp);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(Path.GetFullPath(path), result.Value);
            Assert.Equal(exporter.Render(SampleInfo(), ReportFormat.Text, Stamp), await File.ReadAllTextAsync(path));

            var head = (await File.ReadAllBytesAsync(path)).Take(3).ToArray();
            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, head);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportCreatesMissingDirectory()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "nested", "deep", "report.json");

            var result = await exporter.ExportAsync(SampleInfo(), ReportFormat.Json, path, Stamp);

            Assert.True(result.IsSuccess, result.Message);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExportRejectsBlankPath(string path)
    {
        var result = await exporter.ExportAsync(SampleInfo(), ReportFormat.Text, path, Stamp);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.InvalidInput, result.Error);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    [InlineData(2L * 1024 * 1024 * 1024 * 1024, "2.0 TB")]
    public void FormatBytesUsesBinaryUnits(long value, string expected)
        => Assert.Equal(expected, ReportExporter.FormatBytes(value));

    [Fact]
    public void PadDisplayAccountsForWideCharacters()
    {
        // 中文按两列计算，中英混排的标签列才会对齐
        Assert.Equal("型号" + new string(' ', 8), ReportExporter.PadDisplay("型号", 12));
        Assert.Equal("BIOS" + new string(' ', 8), ReportExporter.PadDisplay("BIOS", 12));
        Assert.Equal("计算机名" + new string(' ', 4), ReportExporter.PadDisplay("计算机名", 12));
        Assert.Equal("物理处理器" + new string(' ', 2), ReportExporter.PadDisplay("物理处理器", 12));
    }

    [Fact]
    public void PadDisplayLeavesOverlongTextUntouched()
        => Assert.Equal("逻辑处理器数量", ReportExporter.PadDisplay("逻辑处理器数量", 12));

    [Fact]
    public void RenderRejectsNullInfo()
        => Assert.Throws<ArgumentNullException>(() => exporter.Render(null!, ReportFormat.Text, Stamp));

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "syssuite-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static HardwareInfo SampleInfo() => new()
    {
        ComputerName = "DEV-BOX",
        OperatingSystem = "Microsoft Windows 11 专业版",
        OsVersion = "10.0.26100.0",
        CpuName = "AMD Ryzen 7 5800X",
        LogicalProcessors = 16,
        PhysicalProcessors = 1,
        Motherboard = "ASUS PRIME B550M-A",
        BiosVersion = "American Megatrends Inc. 3002",
        TotalPhysicalMemory = 34359738368UL,
        Storage = [new StorageInfo("Samsung SSD 980 500GB", 500107862016L)],
        Drives = [new DriveInfo("C:", "系统盘", 500107862016L, 450003058688L)],
        GraphicsCards = [new GraphicsCardInfo("NVIDIA GeForce RTX 3060", "NVIDIA", 12884901888UL, "31.0.15.3699", "1920 x 1080 x 60 赫兹", "独立显卡")],
        NetworkAdapters = [new NetworkAdapterInfo("Intel(R) Ethernet Connection I219-V", "AA-BB-CC-DD-EE-01", true)]
    };
}
