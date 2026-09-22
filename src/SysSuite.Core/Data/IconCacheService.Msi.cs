using System.Diagnostics;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService
{
    private static List<IconSource> FindMsiIcons(AppRecord app, string cacheRoot)
    {
        if (app.KeyPath is null)
        {
            return [];
        }

        var match = global::System.Text.RegularExpressions.Regex.Match(app.KeyPath, @"\{[0-9A-Fa-f\-]{36}\}$");
        if (!match.Success)
        {
            return [];
        }

        var productCode = app.KeyPath[(app.KeyPath.Length - 38)..];
        var productIcon = MsiAppEnumerator.GetIconPath(productCode);
        if (!string.IsNullOrWhiteSpace(productIcon))
        {
            productIcon = Environment.ExpandEnvironmentVariables(productIcon);
        }

        var icons = new List<(IconSource Source, int Score)>();
        var registryIcon = ParseIconReference(app.IconPath);
        if (registryIcon is not null)
        {
            var registryIconPath = ResolveSystemPath(registryIcon.Value.Path, NormalizeDirectory(app.InstallDir));
            if (registryIconPath is not null
                && IsSupportedIconFile(registryIconPath)
                && !IsAdministrativeBinary(registryIconPath))
            {
                icons.Add((new IconSource(registryIconPath, registryIcon.Value.IconIndex), ScoreMsiIcon(app, registryIconPath, isProductIcon: true) - 15));
            }
        }

        if (!string.IsNullOrWhiteSpace(productIcon) && File.Exists(productIcon))
        {
            icons.Add((new IconSource(productIcon, 0), ScoreMsiIcon(app, productIcon, isProductIcon: true)));
        }

        // Prefer MSI ProductIcon before expensive MSI file-table enumeration.
        if (icons.Any(candidate => candidate.Score >= 260)
            && !icons.Any(candidate => Path.GetFileNameWithoutExtension(candidate.Source.Path).Equals("ARPPRODUCTICON", StringComparison.OrdinalIgnoreCase)))
        {
            return icons.Select(candidate => candidate.Source).Distinct().Take(8).ToList();
        }

        var installDirectory = NormalizeDirectory(app.InstallDir);
        if (installDirectory is not null)
        {
            var installIcons = new List<IconSource>();
            AddInstallDirectoryIcons(installIcons, app, installDirectory);
            foreach (var source in installIcons.Take(16))
            {
                icons.Add((source, ScoreMsiIcon(app, source.Path, isProductIcon: false)));
            }

            if (icons.Any(candidate => candidate.Score >= 260 && !IsArpProductIcon(candidate.Source.Path)))
            {
                return icons
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Source.Path.Length)
                    .Select(candidate => candidate.Source)
                    .Distinct()
                    .Take(16)
                    .ToList();
            }
        }

        var embeddedIcon = MsiAppEnumerator.GetEmbeddedIconPath(
            productCode,
            Path.Combine(cacheRoot, "msi-icons"));
        if (embeddedIcon is not null)
        {
            icons.Add((new IconSource(embeddedIcon, 0), ScoreMsiIcon(app, embeddedIcon, isProductIcon: true)));
        }

        var executablePaths = MsiAppEnumerator.GetInstalledExecutablePaths(productCode)
            .Where(path => string.IsNullOrWhiteSpace(installDirectory)
                || IsPathUnderDirectory(path, installDirectory));
        foreach (var path in executablePaths)
        {
            var score = ScoreMsiIcon(app, path, isProductIcon: false);
            if (score > 0)
            {
                icons.Add((new IconSource(path, 0), score));
            }
        }

        return icons
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Source.Path.Length)
            .Select(candidate => candidate.Source)
            .Distinct()
            .Take(16)
            .ToList();
    }

    private static bool IsArpProductIcon(string path)
    {
        return Path.GetFileNameWithoutExtension(path).Equals("ARPPRODUCTICON", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreMsiIcon(AppRecord app, string path, bool isProductIcon)
    {
        if (IsAdministrativeBinary(path))
        {
            return 0;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        var nameTokens = GetNameTokens(app.Name);
        var publisherTokens = GetNameTokens(app.Publisher ?? string.Empty);
        var fileTokens = GetNameTokens(fileName);
        var score = isProductIcon ? 260 : 0;
        if (isProductIcon && fileName.Contains("icon", StringComparison.OrdinalIgnoreCase)
            && (fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains("remove", StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        score += fileTokens.Count(nameTokens.Contains) * 55;
        score += fileTokens.Count(publisherTokens.Contains) * 20;

        if (fileName.Contains("main", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("start", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("launch", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("client", StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        if (fileName.Contains("service", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("daemon", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("helper", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("agent", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("updater", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("crash", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("licen", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("config", StringComparison.OrdinalIgnoreCase))
        {
            score -= 100;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var productTokens = GetNameTokens(info.ProductName ?? string.Empty);
            var companyTokens = GetNameTokens(info.CompanyName ?? string.Empty);
            score += productTokens.Count(nameTokens.Contains) * 90;
            score += productTokens.Count(publisherTokens.Contains) * 35;
            score += companyTokens.Count(publisherTokens.Contains) * 25;
            if (productTokens.Overlaps(nameTokens))
            {
                score += 80;
            }
        }
        catch
        {
            // Version metadata is optional; filename/path scoring remains available.
        }

        if (path.Contains($"{Path.DirectorySeparatorChar}NXBIN{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && fileName.Equals("ugraf", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        return Math.Max(score, 0);
    }
}
