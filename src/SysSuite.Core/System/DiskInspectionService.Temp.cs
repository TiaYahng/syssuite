using System.IO;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService
{
    private static readonly EnumerationOptions TempOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false
    };

    private void FindTempItems(
        IEnumerable<DirectoryInfo> roots,
        List<CleanItem> items,
        ScanTracker tracker,
        CancellationToken cancellationToken)
    {
        // 整轮扫描只取一次规则快照：中途换规则集会让同一批文件的判定标准不一致
        var rules = ruleProvider.GetRules();
        if (rules.Count == 0)
        {
            rules = BuildFallbackRules();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var rule in rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tracker.ReportPhase($"正在应用清理规则：{rule.Rule.Id}", root.FullName);
                foreach (var file in CleanRuleEngine.Enumerate(rule, root, cancellationToken))
                {
                    if (!seen.Add(file.FullName) || !TempCleanerService.IsCleanableTempFile(file))
                    {
                        continue;
                    }

                    var (risk, importance) = TempCleanerService.AnalyzeFile(file);
                    var effectiveRisk = rule.Rule.Level > risk ? rule.Rule.Level : risk;
                    tracker.ReportFile(file.FullName, file.Length);
                    items.Add(new CleanItem(file.FullName, "临时文件", file.Length, effectiveRisk,
                        $"规则 {rule.Rule.Id}：{importance}"));
                }
            }
        }
    }

    /// <summary>
    /// 回收站以**整站一个条目**呈现。
    ///
    /// 站内是 <c>$I&lt;name&gt;</c>（元数据）与 <c>$R&lt;name&gt;</c>（内容）成对存放的，
    /// 逐条列出来既没有可操作性（删一半会留孤儿），也过不了删除接口的保护校验。
    /// 所以这里只报一个"整站"条目，执行时走 <see cref="RecycleBinService"/> 的清空 API。
    /// </summary>
    private static void FindRecycleBinItems(
        IEnumerable<DirectoryInfo> roots,
        List<CleanItem> items,
        ScanTracker tracker)
    {
        foreach (var root in roots)
        {
            // 回收站是**卷级**的。扫描一个子目录时把整盘回收站报出来是错的：
            // 用户选的是那个目录，却冒出一个他没要求统计、且删除会波及全盘的条目。
            if (root.Parent is not null)
            {
                continue;
            }

            var volume = root.FullName;
            tracker.ReportPhase("正在统计回收站", volume);
            var info = RecycleBinService.Query(volume);
            if (info.IsEmpty)
            {
                continue;
            }

            items.Add(new CleanItem(
                Path.Combine(volume, "$Recycle.Bin"),
                RecycleBinService.Category,
                info.SizeBytes,
                CleanRisk.Caution,
                $"回收站内有 {info.ItemCount} 个已删除项目，清空后不可恢复。"));
        }
    }

    private static List<ValidatedCleanRule> BuildFallbackRules()
    {
        var rules = new[]
        {
            new CleanRule("windows-temp", "{root}\\\\Windows\\\\Temp", [], ".*", [], 1, CleanRisk.Safe),
            new CleanRule("root-temp", "{root}\\\\Temp", [], ".*", [], 1, CleanRisk.Caution),
            new CleanRule("programdata-temp", "{root}\\\\ProgramData\\\\Temp", [], ".*", [], 1, CleanRisk.Caution),
            new CleanRule("user-temp", "{root}\\\\Users\\\\*\\\\AppData\\\\Local\\\\Temp", [], ".*", [], 1, CleanRisk.Caution)
        };
        var result = new List<ValidatedCleanRule>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (CleanRuleEngine.Validate(rule, ids, out var validated) && validated is not null)
            {
                result.Add(validated);
            }
        }
        return result;
    }

    private static IEnumerable<string> GetTempRoots(DirectoryInfo root)
    {
        yield return Path.Combine(root.FullName, "Windows", "Temp");
        yield return Path.Combine(root.FullName, "Temp");
        yield return Path.Combine(root.FullName, "ProgramData", "Temp");

        var usersDirectory = new DirectoryInfo(Path.Combine(root.FullName, "Users"));
        if (!usersDirectory.Exists)
        {
            yield break;
        }

        foreach (var user in usersDirectory.EnumerateDirectories("*", ScanOptions))
        {
            yield return Path.Combine(user.FullName, "AppData", "Local", "Temp");
        }
    }
}