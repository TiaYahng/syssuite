using System.Text.RegularExpressions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;
using Xunit;

namespace SysSuite.Tests;

/// <summary>
/// T3.1 验收：发货规则集里**每一条**规则都要有三例（命中 / 过滤 / 目标缺失）。
/// </summary>
/// <remarks>
/// 用例由 <c>rules/clean-temp.json</c> 驱动，而不是在测试里手写一份规则副本 ——
/// 手写副本会让"规则文件改了、测试还绿着"，等于是给错误结论盖了个通过的章。
/// 代价是新增规则若不写测试会被立刻暴露，这正是想要的效果。
/// </remarks>
public class CleanRuleSetTests
{
    private static string RulesPath { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules", "clean-temp.json"));

    private static IReadOnlyList<ValidatedCleanRule> LoadRules() => CleanRuleEngine.Load(RulesPath);

    [Fact]
    public void ShippedRuleSetHasNoRejectedRules()
    {
        var rules = CleanRuleEngine.Load(RulesPath, out var rejected);

        Assert.NotEmpty(rules);
        // 一条正则转义写错就会让整条规则静默消失，这里必须显式兜住
        Assert.Empty(rejected);
    }

    public static TheoryData<string> RuleIds()
    {
        var data = new TheoryData<string>();
        foreach (var rule in LoadRules())
        {
            data.Add(rule.Rule.Id);
        }

        return data;
    }

    [Fact]
    public void ShippedRuleSetIsCompleteAndInternallyConsistent()
    {
        var rules = LoadRules();
        Assert.NotEmpty(rules);

        var ids = rules.Select(rule => rule.Rule.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // 计划清单里的档位必须都在，缺一条就是回归
        foreach (var expected in new[]
                 {
                     "windows-temp", "root-temp", "programdata-temp", "user-temp",
                     "chrome-cache", "edge-cache", "firefox-cache", "thumbnails",
                     "error-reports", "update-download", "delivery-optimization",
                     "log-files", "font-cache", "dns-cache",
                 })
        {
            Assert.Contains(expected, ids);
        }
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void RuleHitsAnAgedFileUnderItsTarget(string id)
    {
        var (rule, root) = Arrange(id);
        try
        {
            var name = FindName(rule.IncludeRegex, matching: true)!;
            var file = Path.Combine(root, name);
            File.WriteAllText(file, "stale");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));

            var hits = CleanRuleEngine.Enumerate(rule, new DirectoryInfo(TempRoot(root)), CancellationToken.None).ToArray();

            Assert.Contains(hits, item => string.Equals(item.FullName, file, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void RuleFiltersOutFilesThatDoNotQualify(string id)
    {
        var (rule, root) = Arrange(id);
        try
        {
            var matching = FindName(rule.IncludeRegex, matching: true)!;
            var rejected = FindName(rule.IncludeRegex, matching: false);

            // 名字不合规 → 直接落选；若名字全都合规（regex 为 .*），改用"不够旧"来落选
            var file = rejected is not null
                ? Path.Combine(root, rejected)
                : Path.Combine(root, matching);
            File.WriteAllText(file, "candidate");
            File.SetLastWriteTimeUtc(file, rejected is not null ? DateTime.UtcNow.AddDays(-30) : DateTime.UtcNow);

            if (rejected is null && rule.Rule.MinAgeDays <= 0)
            {
                // 既不筛名字也不筛年龄的规则（如 minAgeDays=0 且 regex=.*）：无过滤语义可验
                return;
            }

            var hits = CleanRuleEngine.Enumerate(rule, new DirectoryInfo(TempRoot(root)), CancellationToken.None).ToArray();

            Assert.DoesNotContain(hits, item => string.Equals(item.FullName, file, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void RuleYieldsNothingWhenItsTargetIsMissing(string id)
    {
        var rule = LoadRules().First(item => string.Equals(item.Rule.Id, id, StringComparison.OrdinalIgnoreCase));
        var absent = Path.Combine(Path.GetTempPath(), $"SysSuite-Absent-{Guid.NewGuid():N}");

        var hits = CleanRuleEngine.Enumerate(rule, new DirectoryInfo(absent), CancellationToken.None).ToArray();

        Assert.Empty(hits);
    }

    /// <summary>
    /// 造出规则目标目录，返回规则与其绝对路径。
    /// </summary>
    private static (ValidatedCleanRule Rule, string Directory) Arrange(string id)
    {
        var rules = LoadRules();
        var rule = rules.First(item => string.Equals(item.Rule.Id, id, StringComparison.OrdinalIgnoreCase));

        var relative = rule.Rule.Target.Replace("{root}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-Rule-{Guid.NewGuid():N}");
        // 目录模板里的 * 是"任意一层用户目录"，占位成一个真实目录名即可
        var directory = Path.Combine(root, relative.Replace('*', 'u'));
        Directory.CreateDirectory(directory);
        return (rule, directory);
    }

    /// <summary>从规则目标路径反推它挂在哪个人造盘根下。</summary>
    private static string TempRoot(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current.Parent is not null && !current.Name.StartsWith("SysSuite-Rule-", StringComparison.Ordinal))
        {
            current = current.Parent;
        }

        return current.FullName;
    }

    private static void Cleanup(string directory)
    {
        var root = TempRoot(directory);
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
        catch (IOException)
        {
            // 清理失败不该让用例失败：临时目录由系统回收
        }
    }

    /// <summary>
    /// 找一个（不）满足规则 include 正则的文件名。
    /// 用候选表试探而不是写死名字，新规则加进来也能自动挑到合适的样本。
    /// </summary>
    private static string? FindName(Regex? regex, bool matching)
    {
        string[] candidates =
        [
            "sample.tmp", "cache.dat", "app.log", "thumbcache_32.db",
            "sample.etl", "package.cab", "cache.dns", "report.txt",
        ];

        if (regex is null)
        {
            return matching ? candidates[0] : null;
        }

        foreach (var candidate in candidates)
        {
            var path = Path.Combine("C:", "probe", candidate);
            if (regex.IsMatch(path) == matching)
            {
                return candidate;
            }
        }

        return matching ? candidates[0] : null;
    }
}
