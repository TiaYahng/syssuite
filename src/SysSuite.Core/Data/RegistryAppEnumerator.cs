using System.Globalization;
using Microsoft.Win32;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed class RegistryAppEnumerator
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private readonly bool includeSystemComponents;

    public RegistryAppEnumerator(bool includeSystemComponents = false)
    {
        this.includeSystemComponents = includeSystemComponents;
    }

    public Result<IReadOnlyList<AppRecord>> Enumerate()
    {
        try
        {
            var views = new[]
            {
                Task.Run(() => EnumerateView(Registry.LocalMachine, RegistryView.Registry64, UninstallKeyPath).ToList()),
                Task.Run(() => EnumerateView(Registry.LocalMachine, RegistryView.Registry32, UninstallKeyPath).ToList()),
                Task.Run(() => EnumerateView(Registry.CurrentUser, RegistryView.Default, UninstallKeyPath).ToList())
            };
            Task.WaitAll(views);
            var apps = views.SelectMany(task => task.Result).ToList();
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

    private IEnumerable<AppRecord> EnumerateView(RegistryKey root, RegistryView view, string uninstallKeyPath)
    {
        using var baseKey = RegistryKey.OpenBaseKey(root.Name == Registry.LocalMachine.Name ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
        using var uninstallKey = baseKey.OpenSubKey(uninstallKeyPath);
        if (uninstallKey is null)
        {
            yield break;
        }

        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            using var appKey = uninstallKey.OpenSubKey(subKeyName);
            if (appKey is null)
            {
                continue;
            }

            if (appKey.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!includeSystemComponents && IsEnabled(appKey, "SystemComponent"))
            {
                continue;
            }

            var isMsi = IsEnabled(appKey, "WindowsInstaller");
            var uninstallString = appKey.GetValue("UninstallString") as string;
            var releaseType = appKey.GetValue("ReleaseType") as string;
            var installLocation = appKey.GetValue("InstallLocation") as string;
            var displayIcon = appKey.GetValue("DisplayIcon") as string;
            if (!includeSystemComponents && IsRuntimeComponent(name, uninstallString, releaseType))
            {
                continue;
            }
            var fullKeyPath = $@"{baseKey.Name}\{uninstallKeyPath}\{subKeyName}";
            var estimatedSize = appKey.GetValue("EstimatedSize") is int sizeInKilobytes ? (long?)sizeInKilobytes * 1024 : null;
            yield return AppRecord.Create(
                isMsi ? $"MSI|{subKeyName}" : $"REG|{fullKeyPath}",
                name,
                isMsi ? AppSource.Msi : AppSource.Registry,
                appKey.GetValue("Publisher") as string,
                appKey.GetValue("DisplayVersion") as string,
                appKey.GetValue("InstallDate") as string,
                estimatedSize,
                uninstallString,
                appKey.GetValue("QuietUninstallString") as string,
                fullKeyPath,
                NormalizeInstallLocation(installLocation),
                NormalizeDisplayIcon(displayIcon));
        }
    }

    private static string? NormalizeInstallLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        return Directory.Exists(path) ? path : value;
    }

    private static string? NormalizeDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Environment.ExpandEnvironmentVariables(value.Trim());
    }

    internal static bool IsRuntimeComponent(string name, string? uninstallString, string? releaseType)
    {
        if (!string.IsNullOrWhiteSpace(releaseType))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(uninstallString))
        {
            return true;
        }

        var lowerName = name.ToLowerInvariant();
        return lowerName.Contains(" redistributable")
            || lowerName.Contains(" runtime ")
            || lowerName.Contains(" runtime - ")
            || lowerName.EndsWith(" runtime", StringComparison.Ordinal)
            || lowerName.Contains(" sdk ")
            || lowerName.EndsWith(" sdk", StringComparison.Ordinal)
            || lowerName.Contains(" targeting pack")
            || lowerName.Contains(" host fx resolver")
            || lowerName.Contains(" shared framework")
            || lowerName.Contains(" language pack")
            || lowerName.Contains(" setup support files")
            || lowerName.Contains(" setup (")
            || lowerName.Contains(" setup files")
            || lowerName.Contains(" native client")
            || lowerName.Contains(" management objects")
            || lowerName.Contains(" clr types")
            || lowerName.Contains(" scriptdom")
            || lowerName.Contains(" t-sql language service")
            || lowerName.Contains(" localdb")
            || lowerName.Contains(" click-to-run")
            || lowerName.Contains(" vba enabler")
            || lowerName.Contains(" browser for sql server")
            || lowerName.Contains(" bootstrapper package")
            || lowerName.Contains(" ajax extensions")
            || lowerName.Contains(" odbc driver")
            || lowerName.Contains(" vss writer")
            || lowerName.Contains(" update health tools")
            || lowerName.Contains("security update for")
            || lowerName.Contains(" update for microsoft")
            || lowerName.Contains(" add-in extensibility update");
    }

    private static bool IsEnabled(RegistryKey key, string valueName)
    {
        return key.GetValue(valueName) switch
        {
            int value => value != 0,
            string value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed != 0,
            _ => false
        };
    }
}
