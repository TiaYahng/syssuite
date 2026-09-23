using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;

namespace SysSuite.Tests;

/// <summary>
/// 快捷方式评分测试（M2 效率优化）。
///
/// 重构前：每个应用都要对**全部**快捷方式重跑一次
/// <c>ResolveSystemPath</c>（内含 <c>File.Exists</c>，直接命中磁盘）+ 两次分词。
/// 几百个应用 × 几百个快捷方式 = 数十万次磁盘探测，这是刷新慢的主因。
///
/// 重构后：分词、规范化链接名、是否 ugraf 都在**索引构建时**算好，
/// 与具体应用无关的部分只算一次。这组测试锁定评分行为没有因此改变。
/// </summary>
public class IconShortcutScoringTests
{
    [Fact]
    public void UnrelatedShortcutScoresZero()
    {
        var app = App("Adobe Photoshop", @"C:\Program Files\Adobe\Photoshop");

        // 名字不匹配、目标不在安装目录下、也不是 MSI → 不该入选
        Assert.Equal(0, Score(app, "记事本", @"C:\Windows\notepad.exe"));
    }

    [Fact]
    public void ExactNameMatchScoresHigherThanPartialTokenMatch()
    {
        var app = App("Adobe Photoshop", null);

        var exact = Score(app, "Adobe Photoshop", null);
        var partial = Score(app, "Adobe Reader", null);

        Assert.True(exact > 0, "完全同名的快捷方式应当入选");
        Assert.Equal(0, partial);
    }

    [Fact]
    public void TargetInsideInstallDirectoryOutranksNameOnlyMatch()
    {
        // 必须用真实存在的目录：NormalizeDirectory 内含 Directory.Exists，
        // 目录不存在时 installDir 会被归一化为 null，"命中安装目录"这条路径根本走不到。
        var installDir = CreateTempDirectory();
        try
        {
            var app = App("Some App", installDir);

            var byDirectory = Score(app, "完全不相关的名字", Path.Combine(installDir, "main.exe"));
            var byName = Score(app, "Some App", @"C:\Elsewhere\x.exe");

            Assert.True(byDirectory > 0, "目标落在安装目录内的快捷方式应当入选");
            Assert.True(byName > 0, "名字完全一致的快捷方式应当入选");
            // 目录命中 +180 是权重最高的一项，因为它比名字更可靠
            Assert.True(byDirectory > byName);
        }
        finally
        {
            Directory.Delete(installDir, recursive: true);
        }
    }

    [Fact]
    public void UgrafBonusIsAppliedFromPrecomputedFlag()
    {
        var installDir = CreateTempDirectory();
        try
        {
            var app = App("Siemens NX", installDir);
            var target = Path.Combine(installDir, "start.exe");

            var plain = Score(app, "Siemens NX", target);
            var ugraf = Score(app, "Siemens NX", target, isUgraf: true);

            Assert.Equal(80, ugraf - plain);
        }
        finally
        {
            Directory.Delete(installDir, recursive: true);
        }
    }

    [Fact]
    public void NullTargetPathWithNoNameMatchScoresZero()
    {
        var app = App("Test App", null);

        // 目标为空且名字不匹配：不能因为"没有目标"就被判成命中目录
        Assert.Equal(0, Score(app, "Another App", null));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "syssuite-icon-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int Score(AppRecord app, string linkName, string? targetPath, bool isUgraf = false)
        => IconCacheService.ScoreShortcutForTests(app, linkName, targetPath, @"C:\App\icon.exe", isUgraf);

    private static AppRecord App(string name, string? installDir) => AppRecord.Create(
        $"TEST|{name}",
        name,
        AppSource.Registry,
        "Test Publisher",
        "1.0.0",
        null,
        null,
        "uninstall.exe",
        "uninstall.exe /S",
        null,
        installDir);
}
