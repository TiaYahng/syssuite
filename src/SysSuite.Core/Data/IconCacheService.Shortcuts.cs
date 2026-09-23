using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService
{
    private const string UgrafExecutableName = "ugraf";

    private List<IconSource> FindShortcutIcons(AppRecord app)
    {
        if (string.IsNullOrWhiteSpace(app.Name))
        {
            return [];
        }

        var nameTokens = GetNameTokens(app.Name);
        var installDirectory = NormalizeDirectory(app.InstallDir);

        // 快捷方式索引建好后就固定了，候选与评分用的名字宽度也是固定的，
        // 只有"目标是否落在该应用的安装目录下"这一项依赖 app。因此把不依赖 app 的
        // 部分在索引阶段算完，这里只做一次目录包含判断 —— 此前每个应用都要对
        // 全部快捷方式重跑 ResolveSystemPath（内含 File.Exists 命中磁盘）。
        var icons = new List<(IconSource Source, int Score)>();
        foreach (var shortcut in shortcutIndex.Value)
        {
            var targetMatchesInstallDirectory = installDirectory is not null
                && IsPathUnderDirectory(shortcut.TargetPath, installDirectory);
            var score = ScoreShortcut(app, shortcut, nameTokens, targetMatchesInstallDirectory);
            if (score > 0)
            {
                icons.Add((new IconSource(shortcut.IconPath, shortcut.IconIndex), score));
            }
        }

        return icons
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Source.Path.Length)
            .Select(item => item.Source)
            .Distinct()
            .ToList();
    }

    private static int ScoreShortcut(
        AppRecord app,
        ShortcutCandidate shortcut,
        IReadOnlySet<string> nameTokens,
        bool targetMatchesInstallDirectory)
    {
        var exactNameMatch = shortcut.NormalizedLinkName.Equals(
            NormalizeShortcutName(app.Name),
            StringComparison.OrdinalIgnoreCase);
        var productIconMatch = app.Source == AppSource.Msi
            && app.KeyPath is not null
            && shortcut.IconPath.Contains(app.KeyPath[^38..], StringComparison.OrdinalIgnoreCase);
        if (!exactNameMatch && !targetMatchesInstallDirectory && !productIconMatch)
        {
            return 0;
        }

        var score = shortcut.NameTokens.Count(nameTokens.Contains) * 40;
        if (exactNameMatch)
        {
            score += 100;
        }

        if (targetMatchesInstallDirectory)
        {
            score += 180;
        }

        if (shortcut.IsUgraf)
        {
            score += 80;
        }

        if (productIconMatch)
        {
            score += 80;
        }

        return score;
    }

    private List<ShortcutCandidate> BuildShortcutIndex()
    {
        var directories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 4
        };
        var result = new List<ShortcutCandidate>();
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (var linkPath in Directory.EnumerateFiles(directory, "*.lnk", options))
            {
                var shortcut = ReadShortcut(linkPath);
                if (shortcut is null)
                {
                    continue;
                }

                var iconReference = ParseIconReference(shortcut.Value.IconLocation);
                if (iconReference is null || iconReference.Value.Path.StartsWith(','))
                {
                    iconReference = ParseIconReference(shortcut.Value.TargetPath);
                }

                if (iconReference is null)
                {
                    continue;
                }

                var iconPath = ResolveSystemPath(iconReference.Value.Path);
                if (iconPath is null
                    || !IsSupportedIconFile(iconPath)
                    || IsAdministrativeBinary(iconPath))
                {
                    continue;
                }

                result.Add(new ShortcutCandidate(
                    GetNameTokens(Path.GetFileNameWithoutExtension(linkPath)),
                    NormalizeShortcutName(Path.GetFileNameWithoutExtension(linkPath)),
                    shortcut.Value.TargetPath,
                    iconPath,
                    iconReference.Value.IconIndex,
                    Path.GetFileNameWithoutExtension(iconPath).Equals(UgrafExecutableName, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return result;
    }

    /// <summary>
    /// 供测试构造索引项用的薄转发。评分逻辑已改为消费预计算字段，
    /// 测试需要能用同样的方式造出候选，否则覆盖不到真实调用形态。
    /// </summary>
    internal static int ScoreShortcutForTests(
        AppRecord app,
        string linkName,
        string? targetPath,
        string iconPath,
        bool isUgraf = false)
    {
        var tokens = GetNameTokens(linkName);
        var shortcut = new ShortcutCandidate(
            tokens,
            NormalizeShortcutName(linkName),
            targetPath,
            iconPath,
            0,
            isUgraf);
        var installDirectory = NormalizeDirectory(app.InstallDir);
        var matchesDirectory = installDirectory is not null
            && IsPathUnderDirectory(targetPath, installDirectory);
        return ScoreShortcut(app, shortcut, GetNameTokens(app.Name), matchesDirectory);
    }

    private static (string? TargetPath, string? IconLocation)? ReadShortcut(string linkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(linkPath);
            return (Convert.ToString(shortcut.TargetPath), Convert.ToString(shortcut.IconLocation));
        }
        catch
        {
            return null;
        }
    }
}
