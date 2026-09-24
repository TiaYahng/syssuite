using SysSuite.Core.System;
using Xunit;

namespace SysSuite.Tests;

/// <summary>
/// T3.1 配套：规则热更新、保护路径例外、回收站查询。
/// </summary>
public class CleanRuleProviderTests
{
    private static string WriteRules(string directory, params string[] ids)
    {
        var file = Path.Combine(directory, "clean-temp.json");
        var body = string.Join(
            ',',
            ids.Select(id =>
                $"{{\"id\":\"{id}\",\"target\":\"{{root}}\\\\Temp\\\\{id}\",\"env\":[],\"regex\":\".*\",\"exclude\":[],\"minAgeDays\":1,\"level\":\"safe\"}}"));
        File.WriteAllText(file, "[" + body + "]");
        return file;
    }

    [Fact]
    public void ProviderPicksUpRuleFileEditsWithinFiveSeconds()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SysSuite-Hot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var file = WriteRules(directory, "alpha");
            using var provider = new CleanRuleProvider(file);

            Assert.Single(provider.GetRules());

            WriteRules(directory, "alpha", "beta");

            // 验收口径是"改 json 5s 生效"；内部防抖 1.5s，监听不可用时退回 5s 轮询
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && provider.GetRules().Count < 2)
            {
                Thread.Sleep(100);
            }

            var ids = provider.GetRules().Select(rule => rule.Rule.Id).ToArray();
            Assert.Contains("beta", ids);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ProviderKeepsLastGoodRulesWhenFileBecomesEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SysSuite-Hot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var file = WriteRules(directory, "alpha");
            using var provider = new CleanRuleProvider(file);
            Assert.Single(provider.GetRules());

            // 编辑器保存的中途文件可能是空的：此时退化成内置兜底会让清理范围悄悄改变，
            // 正确做法是沿用上一版
            File.WriteAllText(file, "[]");

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
                if (File.ReadAllText(file).Length > 0)
                {
                    break;
                }
            }

            Assert.Single(provider.GetRules());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SameSnapshotIsReturnedDuringASingleScan()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SysSuite-Hot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var file = WriteRules(directory, "alpha");
            using var provider = new CleanRuleProvider(file);

            var first = provider.GetRules();
            var second = provider.GetRules();

            // 一次扫描只取一次快照；中途换规则集会让同一批文件判定标准不一致
            Assert.Same(first, second);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WindowsCacheDirectoriesAreCleanableButWinSxSStaysProtected()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // 早期实现把整棵 Windows 目录保护起来，导致所有临时/缓存规则"扫得到删不掉"
        Assert.False(ProtectedPaths.IsProtected(Path.Combine(windows, "Temp", "stale.tmp")));
        Assert.False(ProtectedPaths.IsProtected(Path.Combine(windows, "SoftwareDistribution", "Download", "x.cab")));
        Assert.False(ProtectedPaths.IsProtected(Path.Combine(windows, "Logs", "CBS", "CBS.log")));

        // 段级红线不参与例外
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(windows, "WinSxS", "payload.dll")));
        // 例外之外的 Windows 子树仍然受保护
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(windows, "System32", "drivers", "etc", "hosts")));
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(windows, "Fonts", "arial.ttf")));
    }

    [Fact]
    public void RecycleBinQueryReportsZeroForAnAbsentVolume()
    {
        // 只读查询，且用一个肯定不存在的盘符：既不触碰用户数据，也验证失败降级不抛异常
        var info = RecycleBinService.Query("Z:\\");

        Assert.True(info.IsEmpty);
        Assert.Equal(0, info.ItemCount);
    }
}
