using SysSuite.Core.System;
using Xunit;

namespace SysSuite.Tests;

/// <summary>
/// T3.5 的输出解析与体积统计（从 <c>SystemSlimmingServiceTests.cs</c> 拆出守住 300 行门禁）。
///
/// 这些用例的共同点是**纯函数、不碰系统**：DISM 输出是本地化的，解析失败必须报"未知"
/// 而不是猜一个数字 —— 拿猜出来的体积去说服用户"能省 3GB"比不做解析更糟。
/// </summary>
public class SystemSlimmingParseTests
{
    /// <summary>
    /// 真机捕获的原样输出（Win11 26200 zh-CN，提权执行
    /// <c>dism /Online /Cleanup-Image /AnalyzeComponentStore</c>，去掉进度条行）。
    /// </summary>
    /// <remarks>
    /// 这份数据**必须**来自真机：早先这里放的是按直觉编的输出（"备份和已禁用功能 : 2.15 GB"），
    /// 于是解析器写错了标签也照样全绿 —— 单测与实现一起错了。同一课在 compact 上已经付过一次
    /// 学费（见 D22）。改动 DISM 解析前，先重新抓一份真输出替换这里。
    /// </remarks>
    private const string ChineseAnalyzeOutput = """
        部署映像服务和管理工具
        版本: 10.0.26100.8972

        映像版本: 10.0.26200.9168

        组件存储(WinSxS)信息:

        Windows 资源管理器报告的组件存储大小 : 20.07 GB

        组件存储的实际大小 : 18.61 GB

            已与 Windows 共享 : 8.56 GB
            备份和已禁用的功能 : 10.04 GB
            缓存和临时数据 :  0 bytes

        上次清理的日期 : 2026-09-24 20:22:21

        可回收的程序包数 : 5
        推荐使用组件存储清理 : 是

        操作成功完成。
        """;

    /// <summary>英文布局与中文一一对应（同一台机器的 en-US 形态）。</summary>
    private const string EnglishAnalyzeOutput = """
        Deployment Image Servicing and Management tool
        Version: 10.0.26100.8972

        Image Version: 10.0.26200.9168

        Component Store (WinSxS) information:

        Windows Explorer Reported Size of Component Store : 20.07 GB

        Actual Size of Component Store : 18.61 GB

            Shared with Windows : 8.56 GB
            Backups and Disabled Features : 10.04 GB
            Cache and Temporary Data :  0 bytes

        Date of Last Cleanup : 2026-09-24 20:22:21

        Number of Reclaimable Packages : 5
        Component Store Cleanup Recommended : Yes

        The operation completed successfully.
        """;

    /// <summary>
    /// 真机那一行的"备份和已禁用的功能"（10.04 GB）折算成字节。
    /// </summary>
    /// <remarks>
    /// 数值由工具算得（<c>int(10.04 * 1024 ** 3)</c>），不要手推 ——
    /// 1024 进制的手算在这个项目里已经错过三次（8.11 GB、1.34 GB、这一次差 10,000）。
    /// </remarks>
    private const long RealBackupsBytes = 10_780_367_912L;

    // ---- DISM 输出解析 ----

    [Fact]
    public void ParseAnalyzeOutputReadsChineseOutput()
    {
        var (reclaimable, packages) = SystemSlimmingService.ParseAnalyzeOutput(Split(ChineseAnalyzeOutput));

        Assert.Equal(5, packages);
        Assert.Equal((long)(10.04 * 1024 * 1024 * 1024), reclaimable);
        Assert.Equal(RealBackupsBytes, reclaimable);
        Assert.True(SystemSlimmingService.IsCleanupRecommended("推荐使用组件存储清理 : 是"));
    }

    [Fact]
    public void ParseAnalyzeOutputReadsEnglishOutput()
    {
        var (reclaimable, packages) = SystemSlimmingService.ParseAnalyzeOutput(Split(EnglishAnalyzeOutput));

        Assert.Equal(5, packages);
        Assert.Equal(RealBackupsBytes, reclaimable);
    }

    [Fact]
    public void ParseAnalyzeOutputReportsUnknownInsteadOfZeroWhenLabelsAreMissing()
    {
        // 最关键的一条：本地化标签全不认识时必须返回"未知"(-1)。
        // 返回 0 会被 UI 渲染成"没有可回收空间"，那是在误导用户放弃清理
        var (reclaimable, packages) = SystemSlimmingService.ParseAnalyzeOutput(
            ["Something completely different", "Size: 5 GB"]);

        Assert.Equal(-1, reclaimable);
        Assert.Null(packages);
    }

    [Theory]
    [InlineData("    备份和已禁用的功能 : 10.04 GB", RealBackupsBytes)]
    [InlineData("某处 : 512 MB", 536_870_912L)]
    [InlineData("某处 : 1024 KB", 1_048_576L)]
    [InlineData("某处 : 256 B", 256L)]
    public void ParseSizeUnderstandsEveryUnit(string line, long expected)
        => Assert.Equal(expected, SystemSlimmingService.ParseSize(line));

    [Theory]
    [InlineData("    缓存和临时数据 :  0 bytes", 0L)]
    [InlineData("Cache and Temporary Data :  0 bytes", 0L)]
    [InlineData("某处 : 1 byte", 1L)]
    public void ParseSizeUnderstandsTheEnglishBytesUnitInLocalizedOutput(string line, long expected)
    {
        // 真机中文输出里体积为 0 时 DISM 写的是英文单位 "0 bytes"（它不翻译 0）。
        // 早先用 \b 收尾的正则匹配不到 bytes（B 后面还是字母），于是这一行恒被判为"未知"。
        Assert.Equal(expected, SystemSlimmingService.ParseSize(line));
    }

    [Fact]
    public void ParseSizeReturnsNullWhenThereIsNoSize()
        => Assert.Null(SystemSlimmingService.ParseSize("可回收的程序包数 : 3"));

    [Theory]
    [InlineData("可回收的程序包数 : 3", 3)]
    [InlineData("Reclaimable Packages : 12", 12)]
    [InlineData("可回收的程序包数 : 0", 0)]
    public void ParseCountReadsTheTrailingInteger(string line, int expected)
        => Assert.Equal(expected, SystemSlimmingService.ParseCount(line));

    [Fact]
    public void ParseCountReturnsNullWhenTheLineDoesNotEndWithANumber()
        => Assert.Null(SystemSlimmingService.ParseCount("建议 : 强烈建议清理组件存储"));

    [Fact]
    public void ParseCountWouldMistakeATimestampForACountSoItMustStayLabelled()
    {
        // 契约提醒：ParseCount 只看行尾整数，日期行（…10:22:31）会被读成 31。
        // 它**只能**用在已经按标签筛过的"可回收的程序包数"行上 ——
        // 这条断言把该约束写进测试，避免以后有人拿它去扫全量输出。
        Assert.Equal(31, SystemSlimmingService.ParseCount("上次清理日期 : 2024-04-30 10:22:31"));
    }

    [Theory]
    [InlineData("推荐使用组件存储清理 : 是", true)]
    [InlineData("推荐使用组件存储清理 : 否", false)]
    [InlineData("Component Store Cleanup Recommended : Yes", true)]
    [InlineData("Component Store Cleanup Recommended : No", false)]
    [InlineData("建议 : 强烈建议清理组件存储", true)]
    [InlineData("建议 : 不需要清理", false)]
    [InlineData("建议 : 不建议清理", false)]
    [InlineData("It is recommended that you clean up the component store", true)]
    public void IsCleanupRecommendedReadsTheValueNotJustTheLabel(string line, bool expected)
    {
        // 前四条是真机形态（标签 : 值）。只看标签的实现会把「… : 否」也读成建议清理 ——
        // 那会推着用户去做一次毫无收益且不可回退的清理，比漏报更糟。
        Assert.Equal(expected, SystemSlimmingService.IsCleanupRecommended(line));
    }

    // ---- compact 输出解析 ----

    [Fact]
    public void ParseCompactFreedBytesReadsTheChineseSummaryLine()
    {
        // 真机捕获的原样输出（compact /c /exe 在一个含 2 个可压缩文件的目录上）
        var lines = new List<string>
        {
            @"正在压缩 E:\build\compact-target\ 中的文件",
            "sample0.exe            560000 :     20480 = 27.3 到 1 [OK]",
            "sample1.exe            560000 :     20480 = 27.3 到 1 [OK]",
            "已压缩 1 个目录中的 2 个文件。",
            "总共 1,120,000 字节的数据保存在 40,960 字节中。",
            "压缩率为 27.3 到 1。",
        };

        Assert.Equal(1_079_040, SystemSlimmingService.ParseCompactFreedBytes(lines));
    }

    [Fact]
    public void ParseCompactFreedBytesReadsTheEnglishSummaryLine()
    {
        var lines = new List<string>
        {
            @"Compressing files in E:\build\compact-target\",
            "Total 1,120,000 bytes of data saved in 40,960 bytes.",
        };

        Assert.Equal(1_079_040, SystemSlimmingService.ParseCompactFreedBytes(lines));
    }

    [Fact]
    public void ParseCompactFreedBytesFallsBackToPerFileLines()
    {
        // 没有汇总行时按逐文件行累加。真实格式是「文件名 原始 : 压缩后 = 压缩率」——
        // 等号在数字对之后，早先按「= 原始 : 压缩后」写会让这里永远算不出 0 以外的值
        var lines = new List<string>
        {
            "sample0.exe            560000 :     20480 = 27.3 到 1 [OK]",
            "sample1.exe            560000 :     20480 = 27.3 到 1 [OK]",
        };

        Assert.Equal(1_079_040, SystemSlimmingService.ParseCompactFreedBytes(lines));
    }

    [Fact]
    public void ParseCompactFreedBytesIgnoresGrowthAndGarbage()
    {
        // 变大了（before < after）不该被当成"节省了负数"
        Assert.Equal(0, SystemSlimmingService.ParseCompactFreedBytes(["x.txt = 100 : 200", "noise"]));
        Assert.Equal(0, SystemSlimmingService.ParseCompactFreedBytes([]));
    }

    // ---- 体积统计 ----

    [Fact]
    public async Task MeasureDirectorySumsFileSizesRecursively()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-measure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "a.bin"), new byte[100]);
            await File.WriteAllBytesAsync(Path.Combine(root, "nested", "b.bin"), new byte[250]);

            var total = SystemSlimmingService.MeasureDirectory(root, out var capped, out _);

            Assert.Equal(350, total);
            Assert.False(capped);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MeasureDirectoryReturnsZeroForMissingDirectory()
    {
        var total = SystemSlimmingService.MeasureDirectory(
            Path.Combine(Path.GetTempPath(), $"SysSuite-missing-{Guid.NewGuid():N}"), out _, out _);

        Assert.Equal(0, total);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatBytesUsesBinaryUnits(long bytes, string expected)
        => Assert.Equal(expected, SystemSlimmingService.FormatBytes(bytes));

    [Fact]
    public void FormatBytesReportsUnknownForNegativeInput()
    {
        // 负数是"没测到"的哨兵值，展示成"未知"而不是 "-1 B"
        Assert.Equal("未知", SystemSlimmingService.FormatBytes(-1));
    }

    private static List<string> Split(string text)
        => [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
