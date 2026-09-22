using Microsoft.Win32;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class LeftoverScanner
{
    private static void ScanRegistry(AppRecord app, List<LeftoverItem> items)
    {
        if (!string.IsNullOrWhiteSpace(app.KeyPath) && RegistryKeyExists(app.KeyPath))
        {
            items.Add(new LeftoverItem(app.KeyPath, LeftoverKind.RegistryKey, "原卸载注册表键仍存在。"));
        }

        foreach (var (root, view) in new[]
                 {
                     (Registry.LocalMachine, RegistryView.Registry64),
                     (Registry.LocalMachine, RegistryView.Registry32),
                     (Registry.CurrentUser, RegistryView.Default)
                 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(root.Name == Registry.LocalMachine.Name ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
            using var uninstallKey = baseKey.OpenSubKey(UninstallKeyPath);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                var installLocation = subKey?.GetValue("InstallLocation") as string;
                if (string.Equals(displayName, app.Name, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(app.InstallDir)
                        && !string.IsNullOrWhiteSpace(installLocation)
                        && installLocation.StartsWith(app.InstallDir, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new LeftoverItem($@"{baseKey.Name}\{UninstallKeyPath}\{subKeyName}", LeftoverKind.RegistryKey, "同名的卸载注册表项仍存在。"));
                }
            }
        }

        ScanAppPaths(app, items);
        ScanMuiCache(app, items);
    }

    private static void ScanAppPaths(AppRecord app, List<LeftoverItem> items)
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var appPathsKey = root.OpenSubKey(AppPathsKeyPath);
            if (appPathsKey is null)
            {
                continue;
            }

            foreach (var subKeyName in appPathsKey.GetSubKeyNames())
            {
                using var subKey = appPathsKey.OpenSubKey(subKeyName);
                var defaultValue = subKey?.GetValue(null) as string;
                if (defaultValue is not null
                    && !string.IsNullOrWhiteSpace(app.InstallDir)
                    && defaultValue.StartsWith(app.InstallDir, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new LeftoverItem($@"{appPathsKey.Name}\{subKeyName}", LeftoverKind.RegistryKey, "App Paths 指向原安装目录。"));
                }
            }
        }
    }

    private static void ScanMuiCache(AppRecord app, List<LeftoverItem> items)
    {
        using var muiCacheKey = Registry.CurrentUser.OpenSubKey(MuiCacheKeyPath);
        if (muiCacheKey is null)
        {
            return;
        }

        foreach (var valueName in muiCacheKey.GetValueNames())
        {
            var value = muiCacheKey.GetValue(valueName) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if ((!string.IsNullOrWhiteSpace(app.InstallDir) && valueName.Contains(app.InstallDir, StringComparison.OrdinalIgnoreCase))
                || value.Contains(app.Name, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new LeftoverItem($"{muiCacheKey.Name}\\{valueName}", LeftoverKind.RegistryValue, "MUI Cache 中仍有软件描述。"));
            }
        }
    }

    private static bool RegistryKeyExists(string fullPath)
    {
        var separator = fullPath.IndexOf('\\');
        if (separator <= 0)
        {
            return false;
        }

        var rootName = fullPath[..separator];
        var keyPath = fullPath[(separator + 1)..];
        return rootName.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine.OpenSubKey(keyPath) is not null,
            "HKEY_CURRENT_USER" => Registry.CurrentUser.OpenSubKey(keyPath) is not null,
            _ => false
        };
    }
}
