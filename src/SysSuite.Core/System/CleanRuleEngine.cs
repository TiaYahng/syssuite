using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed record CleanRule(
    string Id,
    string Target,
    IReadOnlyList<string>? Env,
    string? Regex,
    IReadOnlyList<string>? Exclude,
    int MinAgeDays,
    CleanRisk Level);

public sealed record ValidatedCleanRule(
    CleanRule Rule,
    Regex? IncludeRegex,
    IReadOnlyList<Regex> ExcludeRegexes);

public sealed class CleanRuleEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static IReadOnlyList<ValidatedCleanRule> Load(string filePath) => Load(filePath, out _);

    /// <summary>
    /// 加载规则文件。<paramref name="rejectedIds"/> 回填被丢弃的规则 id。
    /// </summary>
    /// <remarks>
    /// 被拒 id 必须回传而不是静默跳过：一条正则写错（例如 JSON 里少一层反斜杠转义）
    /// 会让整条规则消失得无声无息，界面上只表现为"这条规则从来没命中过"，
    /// 排查时极容易误判成"目标目录里没东西"。
    /// </remarks>
    public static IReadOnlyList<ValidatedCleanRule> Load(string filePath, out IReadOnlyList<string> rejectedIds)
    {
        rejectedIds = [];
        if (!File.Exists(filePath))
        {
            return [];
        }

        List<CleanRule> rules;
        try
        {
            rules = JsonSerializer.Deserialize<List<CleanRule>>(File.ReadAllText(filePath), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }

        var result = new List<ValidatedCleanRule>(rules.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();
        foreach (var rule in rules)
        {
            if (!Validate(rule, ids, out var validated))
            {
                rejected.Add(string.IsNullOrWhiteSpace(rule?.Id) ? "(无 id)" : rule.Id);
                continue;
            }

            result.Add(validated!);
        }

        if (rejected.Count > 0)
        {
            rejectedIds = rejected;
        }

        return result;
    }

    public static bool Validate(CleanRule rule, ISet<string>? existingIds, out ValidatedCleanRule? validated)
    {
        validated = null;
        if (string.IsNullOrWhiteSpace(rule.Id)
            || string.IsNullOrWhiteSpace(rule.Target)
            || rule.MinAgeDays < 0
            || (existingIds is not null && !existingIds.Add(rule.Id)))
        {
            return false;
        }

        try
        {
            var include = string.IsNullOrWhiteSpace(rule.Regex)
                ? null
                : new Regex(rule.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
            var excludes = (rule.Exclude ?? [])
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1)))
                .ToArray();
            validated = new ValidatedCleanRule(rule, include, excludes);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static IEnumerable<FileInfo> Enumerate(ValidatedCleanRule rule, DirectoryInfo root, CancellationToken cancellationToken)
    {
        var target = ExpandTarget(rule.Rule.Target, root.FullName, rule.Rule.Env);
        foreach (var directory in ExpandDirectories(target))
        {
            if (!directory.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            IEnumerable<FileInfo> files;
            try
            {
                files = directory.EnumerateFiles("*", new EnumerationOptions
                {
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    ReturnSpecialDirectories = false
                });
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-rule.Rule.MinAgeDays))
                {
                    continue;
                }

                var path = file.FullName;
                if (rule.IncludeRegex is not null && !rule.IncludeRegex.IsMatch(path))
                {
                    continue;
                }

                if (rule.ExcludeRegexes.Any(exclude => exclude.IsMatch(path)))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string ExpandTarget(string target, string root, IReadOnlyList<string>? env)
    {
        var expanded = target.Replace("{root}", root, StringComparison.OrdinalIgnoreCase);
        foreach (var name in env ?? [])
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                expanded = expanded.Replace("%" + name + "%", value, StringComparison.OrdinalIgnoreCase);
            }
        }

        expanded = Environment.ExpandEnvironmentVariables(expanded);
        return expanded;
    }

    private static IEnumerable<DirectoryInfo> ExpandDirectories(string target)
    {
        var rootPath = Path.GetPathRoot(target);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            yield break;
        }

        var remainder = target[rootPath.Length..]
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new List<string> { rootPath };
        foreach (var segment in remainder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new List<string>();
            foreach (var parent in current)
            {
                if (segment.Contains('*') || segment.Contains('?'))
                {
                    try
                    {
                        next.AddRange(Directory.EnumerateDirectories(parent, segment, SearchOption.TopDirectoryOnly));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                else
                {
                    var path = Path.Combine(parent, segment);
                    if (Directory.Exists(path))
                    {
                        next.Add(path);
                    }
                }
            }
            current = next;
            if (current.Count == 0)
            {
                yield break;
            }
        }

        foreach (var path in current)
        {
            yield return new DirectoryInfo(path);
        }
    }
}