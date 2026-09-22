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

    private static void FindTempItems(
        IEnumerable<DirectoryInfo> roots,
        List<CleanItem> items,
        ScanTracker tracker,
        CancellationToken cancellationToken)
    {
        var rulesPath = Path.Combine(AppContext.BaseDirectory, "rules", "clean-temp.json");
        var rules = CleanRuleEngine.Load(rulesPath);
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