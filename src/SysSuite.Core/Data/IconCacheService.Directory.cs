using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService
{
    private static void AddIconReference(
        List<IconSource> sources,
        string? rawPath,
        string? installDirectory,
        bool allowAdministrative = false)
    {
        var reference = ParseIconReference(rawPath);
        if (reference is null)
        {
            return;
        }

        var path = ResolveSystemPath(reference.Value.Path, NormalizeDirectory(installDirectory));
        if (path is not null
            && IsSupportedIconFile(path)
            && (allowAdministrative || !IsAdministrativeBinary(path)))
        {
            sources.Add(new IconSource(path, reference.Value.IconIndex));
        }
    }

    private static void AddInstallDirectoryIcons(List<IconSource> sources, AppRecord app)
    {
        AddInstallDirectoryIcons(sources, app, NormalizeDirectory(app.InstallDir));
    }

    private static void AddInstallDirectoryIcons(List<IconSource> sources, AppRecord app, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var depthOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 2
        };
        var files = Directory.EnumerateFiles(directory, "*", depthOptions)
            .Select(Path.GetFullPath)
            .ToList();

        var nameTokens = GetNameTokens(app.Name);
        var imageCandidates = files
            .Where(path => Path.GetExtension(path) is ".ico" or ".png")
            .Where(path => !IsAdministrativeBinary(path))
            .Select(path => (Path: path, Score: ScoreFileName(path, nameTokens)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Length)
            .Take(8);
        foreach (var candidate in imageCandidates)
        {
            sources.Add(new IconSource(candidate.Path, 0));
        }

        var executableCandidates = files
            .Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsAdministrativeBinary(path))
            .Select(path => (Path: path, Score: ScoreExecutable(path, nameTokens)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Length)
            .Take(12);
        foreach (var candidate in executableCandidates)
        {
            sources.Add(new IconSource(candidate.Path, 0));
        }
    }

    private static string? DeriveInstallDirectoryFromUninstallString(string? uninstallString)
    {
        var reference = ParseIconReference(uninstallString);
        if (reference is null)
        {
            return null;
        }

        var executable = ResolveSystemPath(reference.Value.Path);
        if (executable is null || !File.Exists(executable) || !IsAdministrativeBinary(executable))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(executable);
        return NormalizeDirectory(directory);
    }

    private static int ScoreFileName(string path, IReadOnlySet<string> nameTokens)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var score = 0;
        if (fileName.Contains("logo", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("icon", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        score += GetNameTokens(fileName).Count(nameTokens.Contains) * 25;
        return score;
    }

    private static int ScoreExecutable(string path, IReadOnlySet<string> nameTokens)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("unins", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("repair", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("modify", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var score = GetNameTokens(fileName).Count(nameTokens.Contains) * 30;
        if (fileName.Contains("main", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("start", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("launch", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("client", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("ugraf", StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        if (path.Contains($"{Path.DirectorySeparatorChar}NXBIN{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && fileName.Equals("ugraf", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        return score;
    }
}
