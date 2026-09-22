using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;

namespace SysSuite.Tests;

public class CleanRuleEngineTests
{
    [Fact]
    public void ValidateRejectsDuplicateIdsAndInvalidRegex()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = CleanRuleEngine.Validate(
            new CleanRule("temp", "{root}\\Temp", [], ".*", [], 1, CleanRisk.Safe), ids, out var validated);
        var duplicate = CleanRuleEngine.Validate(
            new CleanRule("temp", "{root}\\Other", [], ".*", [], 1, CleanRisk.Safe), ids, out _);
        var invalid = CleanRuleEngine.Validate(
            new CleanRule("bad", "{root}\\Other", [], "(", [], 1, CleanRisk.Safe), ids, out _);

        Assert.True(first);
        Assert.NotNull(validated);
        Assert.False(duplicate);
        Assert.False(invalid);
    }

    [Fact]
    public void RuleEngineExpandsWildcardDirectoriesAndHonorsAgeAndExcludes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-Rules-{Guid.NewGuid():N}");
        try
        {
            var temp = Path.Combine(root, "Users", "alice", "AppData", "Local", "Temp");
            Directory.CreateDirectory(temp);
            var oldFile = Path.Combine(temp, "old.tmp");
            var excluded = Path.Combine(temp, "keep.tmp");
            var fresh = Path.Combine(temp, "fresh.tmp");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(excluded, "excluded");
            File.WriteAllText(fresh, "fresh");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-3));
            File.SetLastWriteTimeUtc(excluded, DateTime.UtcNow.AddDays(-3));
            File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

            var rule = new CleanRule("user-temp", "{root}\\Users\\*\\AppData\\Local\\Temp", [], ".*\\.tmp$", ["keep\\.tmp$"], 1, CleanRisk.Caution);
            Assert.True(CleanRuleEngine.Validate(rule, new HashSet<string>(StringComparer.OrdinalIgnoreCase), out var validated));
            var files = CleanRuleEngine.Enumerate(validated!, new DirectoryInfo(root), CancellationToken.None).ToArray();

            Assert.Single(files);
            Assert.Equal(oldFile, files[0].FullName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}