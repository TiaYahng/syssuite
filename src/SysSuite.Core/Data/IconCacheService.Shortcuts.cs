using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService
{
    private List<IconSource> FindShortcutIcons(AppRecord app)
    {
        if (string.IsNullOrWhiteSpace(app.Name))
        {
            return [];
        }

        var nameTokens = GetNameTokens(app.Name);
        var installDirectory = NormalizeDirectory(app.InstallDir);
        var icons = new List<(IconSource Source, int Score)>();
        foreach (var shortcut in shortcutIndex.Value)
        {
            var targetPath = ResolveSystemPath(shortcut.TargetPath, installDirectory);
            var score = ScoreShortcut(app, shortcut.LinkName, targetPath, shortcut.IconPath, nameTokens);
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
                    Path.GetFileNameWithoutExtension(linkPath),
                    shortcut.Value.TargetPath,
                    iconPath,
                    iconReference.Value.IconIndex));
            }
        }

        return result;
    }

    private static int ScoreShortcut(
        AppRecord app,
        string linkName,
        string? targetPath,
        string iconPath,
        IReadOnlySet<string> nameTokens)
    {
        var installDirectory = NormalizeDirectory(app.InstallDir);
        var exactNameMatch = string.Equals(
            NormalizeShortcutName(linkName),
            NormalizeShortcutName(app.Name),
            StringComparison.OrdinalIgnoreCase);
        var targetMatchesInstallDirectory = !string.IsNullOrWhiteSpace(installDirectory)
            && IsPathUnderDirectory(targetPath, installDirectory);
        var productIconMatch = app.Source == AppSource.Msi
            && app.KeyPath is not null
            && iconPath.Contains(app.KeyPath[^38..], StringComparison.OrdinalIgnoreCase);
        if (!exactNameMatch && !targetMatchesInstallDirectory && !productIconMatch)
        {
            return 0;
        }

        var score = GetNameTokens(linkName).Count(nameTokens.Contains) * 40;
        if (exactNameMatch)
        {
            score += 100;
        }

        if (targetMatchesInstallDirectory)
        {
            score += 180;
        }

        if (Path.GetFileNameWithoutExtension(targetPath)?.Equals("ugraf", StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 80;
        }

        if (productIconMatch)
        {
            score += 80;
        }

        return score;
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
