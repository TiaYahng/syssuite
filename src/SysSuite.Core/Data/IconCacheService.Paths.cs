using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService
{
    private static (string Path, int IconIndex)? ParseIconReference(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var value = Environment.ExpandEnvironmentVariables(rawPath.Trim());
        if (value.StartsWith('"'))
        {
            var quotedEnd = value.IndexOf('"', 1);
            if (quotedEnd < 1)
            {
                return null;
            }

            value = value[1..quotedEnd];
        }

        var iconIndex = 0;
        var comma = value.LastIndexOf(',');
        if (comma > 1
            && int.TryParse(value.AsSpan(comma + 1), out var parsedIndex))
        {
            iconIndex = parsedIndex;
            value = value[..comma];
        }

        var executableEnd = FindExecutableEnd(value);
        if (executableEnd >= 0)
        {
            value = value[..executableEnd];
        }
        else
        {
            var separator = value.IndexOf(" -", StringComparison.Ordinal);
            if (separator > 0)
            {
                value = value[..separator];
            }
        }

        return string.IsNullOrWhiteSpace(value) ? null : (value, iconIndex);
    }

    private static string? ResolveSystemPath(string? path, string? installDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (File.Exists(expanded))
        {
            return expanded;
        }

        installDirectory = NormalizeDirectory(installDirectory);
        if (!string.IsNullOrWhiteSpace(installDirectory)
            && !Path.IsPathRooted(expanded))
        {
            var installedPath = Path.Combine(installDirectory, expanded);
            if (File.Exists(installedPath))
            {
                return installedPath;
            }
        }

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var systemPath = Path.Combine(systemRoot, Path.GetFileName(expanded));
        return File.Exists(systemPath) ? systemPath : expanded;
    }

    private static string? NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var normalized = directory.Trim().Trim('"');
        return Directory.Exists(normalized) ? normalized : null;
    }

    private static bool IsSupportedIconFile(string? path)
    {
        return path is not null
            && File.Exists(path)
            && Path.GetExtension(path) is ".exe" or ".dll" or ".ico" or ".png" or ".icl";
    }

    private static bool IsAdministrativeBinary(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var isInUninstallerDirectory = normalizedPath.Contains(
            $"{Path.DirectorySeparatorChar}Uninstaller{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
        return isInUninstallerDirectory
            || fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("unins", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("maintenance", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("remove", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRustup(AppRecord app)
    {
        return string.Equals(app.Name, "Rustup: the Rust toolchain installer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(app.StableKey, @"REG|HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Rustup", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindExecutableEnd(string path)
    {
        var end = -1;
        foreach (var extension in new[] { ".exe", ".dll", ".ico" })
        {
            var index = path.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                end = index + extension.Length;
                break;
            }
        }

        return end;
    }

    private static HashSet<string> GetNameTokens(string value)
    {
        return value
            .Split([' ', '-', '_', '.', '(', ')', '[', ']', '{', '}'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Where(token => token.Length > 1
                && !int.TryParse(token, out _)
                && token is not "microsoft" and not "windows" and not "the" and not "app" and not "application")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPathUnderDirectory(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            return fullPath.StartsWith($"{fullDirectory}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeShortcutName(string name)
    {
        return string.Join(
            ' ',
            name.Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => word.TrimEnd(':')));
    }
}
