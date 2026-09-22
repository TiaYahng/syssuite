using Microsoft.Win32;
using System.Xml.Linq;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed class StoreAppEnumerator
{
    private const string RepositoryKeyPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static Result<IReadOnlyList<AppRecord>> Enumerate()
    {
        try
        {
            var apps = new List<AppRecord>();
            using var repositoryKey = Registry.CurrentUser.OpenSubKey(RepositoryKeyPath);
            if (repositoryKey is null)
            {
                return new Result<IReadOnlyList<AppRecord>>(ErrorType.None, string.Empty, apps);
            }

            foreach (var packageFullName in repositoryKey.GetSubKeyNames())
            {
                using var packageKey = repositoryKey.OpenSubKey(packageFullName);
                if (packageKey is null)
                {
                    continue;
                }

                var rootFolder = packageKey.GetValue("PackageRootFolder") as string;
                var manifestPath = string.IsNullOrWhiteSpace(rootFolder) ? null : Path.Combine(rootFolder, "AppxManifest.xml");
                var manifest = File.Exists(manifestPath) ? TryLoadManifest(manifestPath) : null;
                var name = ResolveDisplayName(packageKey, manifest);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (IsHiddenStoreEntry(manifest))
                {
                    continue;
                }

                var fullKeyPath = $@"{repositoryKey.Name}\{packageFullName}";
                apps.Add(AppRecord.Create(
                    $"STORE|{packageFullName}",
                    name,
                    AppSource.Store,
                    packageKey.GetValue("PackagePublisherDisplayName") as string ?? packageKey.GetValue("PackagePublisher") as string,
                    packageKey.GetValue("PackageVersion") as string,
                    keyPath: fullKeyPath,
                    installDir: rootFolder,
                    iconPath: ResolveLogoPath(rootFolder, manifest),
                    hash: packageKey.GetValue("PackageFamilyName") as string));
            }

            return new Result<IReadOnlyList<AppRecord>>(ErrorType.None, string.Empty, apps);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.AccessDenied, exception.Message);
        }
        catch (Exception exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.Internal, exception.Message);
        }
    }

    private static XDocument? TryLoadManifest(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveDisplayName(RegistryKey packageKey, XDocument? manifest)
    {
        var displayName = packageKey.GetValue("DisplayName") as string;
        if (!string.IsNullOrWhiteSpace(displayName) && !IsResourceReference(displayName))
        {
            return displayName;
        }

        return manifest?.Descendants()
            .Where(element => element.Name.LocalName == "VisualElements")
            .Select(element => (string?)element.Attribute("DisplayName"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !IsResourceReference(value));
    }

    private static bool IsHiddenStoreEntry(XDocument? manifest)
    {
        var visualElements = manifest?.Descendants()
            .Where(element => element.Name.LocalName == "VisualElements")
            .ToList();
        if (visualElements is null || visualElements.Count == 0)
        {
            return true;
        }

        return visualElements
            .Select(element => (string?)element.Attribute("AppListEntry"))
            .Any(value => string.Equals(value, "none", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveLogoPath(string? rootFolder, XDocument? manifest)
    {
        var logo = manifest?.Descendants()
            .Where(element => element.Name.LocalName == "VisualElements")
            .Select(element => (string?)element.Attribute("Square44x44Logo"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(rootFolder) || string.IsNullOrWhiteSpace(logo))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(rootFolder, logo));
        return File.Exists(path) ? path : null;
    }

    private static bool IsResourceReference(string value)
    {
        return value.Contains("ms-resource", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("@{", StringComparison.Ordinal)
            || value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase);
    }
}
