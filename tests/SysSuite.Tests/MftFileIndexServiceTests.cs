using SysSuite.Core.System;
using Xunit;

namespace SysSuite.Tests;

/// <summary>
/// T3.2：卷索引的两条通道与路径重建。
///
/// 这里只测**纯函数部分**。真实卷枚举需要提权（FSCTL_ENUM_USN_DATA 要卷句柄带 GENERIC_READ，
/// 非提权直接 err=5），所以那部分靠提权探针实测，不进单测。
/// </summary>
public class MftFileIndexServiceTests
{
    [Theory]
    [InlineData(@"C:\", @"\\?\C:")]
    [InlineData(@"C:\Windows", @"\\?\C:")]
    [InlineData(@"C:\Windows\System32\drivers", @"\\?\C:")]
    [InlineData(@"C:", @"\\?\C:")]
    [InlineData("C:/Windows/", @"\\?\C:")]
    public void ToVolumePathDropsTrailingSeparatorRegardlessOfInputForm(string input, string expected)
        => Assert.Equal(expected, MftFileIndexService.ToVolumePath(input));

    [Fact]
    public void ToVolumePathNeverEmitsTrailingSeparator()
    {
        // 卷句柄名必须是 \\.\C: 这种形态，写成 \\.\C:\ 会被 CreateFileW 拒掉
        var volume = MftFileIndexService.ToVolumePath(@"C:\");

        Assert.EndsWith("C:", volume, StringComparison.Ordinal);
        Assert.DoesNotContain(@":\", volume, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumerateReportsUnsupportedForMissingVolume()
    {
        var result = MftFileIndexService.Enumerate(@"Z:\definitely-not-a-real-volume-xyz");

        Assert.Equal(VolumeIndexChannel.Unsupported, result.Channel);
        Assert.Empty(result.Entries);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public void IsSupportedReturnsFalseRatherThanThrowingForBogusInput()
    {
        // 探测型 API 抛异常会让调用方被迫到处 try —— 这里必须返回 false
        Assert.False(MftFileIndexService.IsSupported(string.Empty));
        Assert.False(MftFileIndexService.IsSupported(@"Z:\nope"));
    }

    [Fact]
    public void RebuildPathsBuildsNestedPaths()
    {
        // 5 = 卷根，100/101 是根下的目录，102/103 是 100 下的子项
        var entries = new List<(ulong, ulong, string, bool)>
        {
            (100, 5, "Windows", true),
            (101, 5, "Users", true),
            (102, 100, "System32", true),
            (103, 100, "explorer.exe", false),
        };

        var paths = MftFileIndexService.RebuildPaths(entries, @"C:\")
            .ToDictionary(item => item.Path, item => item.IsDirectory, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(4, paths.Count);
        Assert.True(paths[@"C:\Windows"]);
        Assert.True(paths[@"C:\Users"]);
        Assert.True(paths[@"C:\Windows\System32"]);
        Assert.False(paths[@"C:\Windows\explorer.exe"]);
    }

    [Fact]
    public void RebuildPathsTreatsOrphanParentAsVolumeRootChild()
    {
        // 父记录不在表内（USN 日志被截断时很常见）→ 当成根的直接子项，而不是丢弃
        var entries = new List<(ulong, ulong, string, bool)>
        {
            (500, 999999, "orphan.txt", false),
        };

        var paths = MftFileIndexService.RebuildPaths(entries, @"C:\");

        Assert.Single(paths);
        Assert.Equal(@"C:\orphan.txt", paths[0].Path);
    }

    [Fact]
    public void RebuildPathsTreatsSelfReferencingParentAsRootChild()
    {
        var entries = new List<(ulong, ulong, string, bool)>
        {
            (7, 7, "self", true),
        };

        var paths = MftFileIndexService.RebuildPaths(entries, @"C:\");

        Assert.Single(paths);
        Assert.Equal(@"C:\self", paths[0].Path);
    }

    [Fact]
    public void RebuildPathsDropsEntriesUnderExcludedDirectories()
    {
        // 验收项：$Recycle.Bin 与 System Volume Information 下的东西不该进索引
        var entries = new List<(ulong, ulong, string, bool)>
        {
            (10, 5, "$Recycle.Bin", true),
            (11, 10, "deleted.txt", false),
            (20, 5, "System Volume Information", true),
            (21, 20, "tracking.log", false),
            (30, 5, "keep.txt", false),
        };

        var paths = MftFileIndexService.RebuildPaths(entries, @"C:\")
            .Select(item => item.Path)
            .ToList();

        Assert.Equal([@"C:\keep.txt"], paths);
    }

    [Fact]
    public void RebuildPathsTerminatesOnCyclicParentReferences()
    {
        // 循环引用不能无限下潜：A→B→A。实现靠 MaxDepth 兜底，这里确认它确实返回而不是挂死
        var entries = new List<(ulong, ulong, string, bool)>
        {
            (1, 2, "a", true),
            (2, 1, "b", true),
        };

        var paths = MftFileIndexService.RebuildPaths(entries, @"C:\");

        Assert.Empty(paths);
    }

    [Fact]
    public void RebuildPathsSurvivesVeryDeepChainWithoutStackOverflow()
    {
        // 递归实现在这种输入上会直接崩 —— 这正是它被写成显式栈的原因
        const int Depth = 3000;
        const ulong VolumeRoot = 999;   // 刻意取一个**不在表内**的父引用，才是真正的"根"
        var entries = new List<(ulong, ulong, string, bool)>(Depth);
        entries.Add((1000, VolumeRoot, "d0", true));
        for (var i = 1; i < Depth; i++)
        {
            entries.Add(((ulong)(1000 + i), (ulong)(1000 + i - 1), $"d{i}", true));
        }

        var paths = MftFileIndexService.RebuildPaths(entries, @"C:\");

        Assert.Equal(Depth, paths.Count);
        Assert.Contains(@"C:\d0\d1\d2", paths[^1].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void RebuildPathsTreatsParentThatCollidesWithAnotherFileAsARealLink()
    {
        // 父引用指向表内另一个文件时，它就是一条真链而不是"根"。
        // 上一条测试最初把父引用写成 5，而 5 恰好是个真实文件 id，于是整条链绕成环、
        // 全部条目被判为不可解析 —— 这里把这个行为固化下来，避免以后误改。
        var entries = new List<(ulong, ulong, string, bool)>
        {
            (1, 2, "a", true),
            (2, 3, "b", true),
            (3, 1, "c", true),
        };

        Assert.Empty(MftFileIndexService.RebuildPaths(entries, @"C:\"));
    }

    [Fact]
    public void RebuildPathsLeavesSizeAtZeroBecauseUsnRecordsCarryNoSize()
    {
        // USN 记录不含大小。调用方若把 0 当"空文件"会得出错误结论，这里把契约钉住
        var entries = new List<(ulong, ulong, string, bool)>
        {
            (100, 5, "big.iso", false),
        };

        var paths = MftFileIndexService.RebuildPaths(entries, @"C:\");

        Assert.Equal(0, paths[0].SizeBytes);
    }

    [Fact]
    public void RebuildPathsReturnsEmptyForEmptyInput()
    {
        Assert.Empty(MftFileIndexService.RebuildPaths([], @"C:\"));
    }
}
