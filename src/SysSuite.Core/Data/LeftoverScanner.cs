using Microsoft.Win32;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.System;

namespace SysSuite.Core.Data;

public sealed partial class LeftoverScanner : ILeftoverScanner
{
    private const string AppPathsKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string MuiCacheKeyPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const int MaximumItems = 500;

    private readonly string localAppDataRoot;
    private readonly string commonAppDataRoot;

    public LeftoverScanner(string? localAppDataRoot = null, string? commonAppDataRoot = null)
    {
        this.localAppDataRoot = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.commonAppDataRoot = commonAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }

    public Task<Result<IReadOnlyList<LeftoverItem>>> ScanAsync(AppRecord app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        return Task.Run(() =>
        {
            try
            {
                var items = new List<LeftoverItem>();
                ScanInstallDirectory(app, items);
                ScanAppDataDirectories(app, items);
                ScanRegistry(app, items);
                return new Result<IReadOnlyList<LeftoverItem>>(ErrorType.None, string.Empty, items.Take(MaximumItems).ToList());
            }
            catch (OperationCanceledException)
            {
                return new Result<IReadOnlyList<LeftoverItem>>(ErrorType.Cancelled, "残留扫描已取消。");
            }
            catch (Exception exception)
            {
                return new Result<IReadOnlyList<LeftoverItem>>(ErrorType.Internal, exception.Message);
            }
        }, cancellationToken);
    }

    public Task<Result<int>> DeleteAsync(IReadOnlyCollection<LeftoverItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Task.Run(async () =>
        {
            var deletedCount = 0;
            var errors = new List<string>();
            foreach (var item in items.Where(item => item.Selected))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ProtectedPaths.IsProtected(item.Path))
                {
                    errors.Add($"{item.Path}: 位于系统保护路径，已拒绝。");
                    continue;
                }

                try
                {
                    if (DeleteItem(item))
                    {
                        deletedCount++;
                    }
                    else
                    {
                        errors.Add($"{item.Path}: 项目已不存在。");
                    }
                }
                catch (Exception exception)
                {
                    errors.Add($"{item.Path}: {exception.Message}");
                }
            }

            if (errors.Count > 0)
            {
                return new Result<int>(ErrorType.Internal, string.Join(Environment.NewLine, errors.Take(20)), deletedCount);
            }

            return new Result<int>(ErrorType.None, string.Empty, deletedCount);
        }, cancellationToken);
    }

    private static void ScanInstallDirectory(AppRecord app, List<LeftoverItem> items)
    {
        if (string.IsNullOrWhiteSpace(app.InstallDir) || !Directory.Exists(app.InstallDir) || ProtectedPaths.IsProtected(app.InstallDir))
        {
            return;
        }

        var hasExecutable = Directory.EnumerateFiles(app.InstallDir, "*.exe", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true
        }).Any();
        if (!hasExecutable)
        {
            items.Add(new LeftoverItem(app.InstallDir, LeftoverKind.InstallDirectory, "安装目录已无可执行文件。"));
        }
    }

    private void ScanAppDataDirectories(AppRecord app, List<LeftoverItem> items)
    {
        var roots = new[] { localAppDataRoot, commonAppDataRoot };
        var referenceName = Path.GetFileName(app.InstallDir?.TrimEnd(Path.DirectorySeparatorChar) ?? string.Empty);
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (Similarity(name, app.Name) < 0.8d && Similarity(name, referenceName) < 0.8d)
                {
                    continue;
                }

                items.Add(new LeftoverItem(directory, LeftoverKind.AppDataDirectory, "目录名与软件名或安装目录高度相似。"));
            }
        }
    }

    private static bool DeleteItem(LeftoverItem item)
    {
        if (item.Kind is LeftoverKind.InstallDirectory or LeftoverKind.AppDataDirectory)
        {
            if (!Directory.Exists(item.Path))
            {
                return false;
            }

            Directory.Delete(item.Path, true);
            return true;
        }

        if (item.Kind == LeftoverKind.RegistryValue)
        {
            var separator = item.Path.IndexOf('|');
            if (separator <= 0)
            {
                return false;
            }

            var fullPath = item.Path[..separator];
            var valueName = item.Path[(separator + 1)..];
            var keySeparator = fullPath.IndexOf('\\');
            if (keySeparator <= 0)
            {
                return false;
            }

            var rootName = fullPath[..keySeparator];
            var keyPath = fullPath[(keySeparator + 1)..];
            using var rootKey = rootName switch
            {
                "HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                "HKEY_CURRENT_USER" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
                _ => null
            };
            using var key = rootKey?.OpenSubKey(keyPath, true);
            if (key is null || key.GetValue(valueName) is null)
            {
                return false;
            }

            key.DeleteValue(valueName, false);
            return true;
        }

        if (item.Kind == LeftoverKind.RegistryKey)
        {
            var separator = item.Path.IndexOf('\\');
            if (separator <= 0)
            {
                return false;
            }

            var rootName = item.Path[..separator].ToUpperInvariant();
            var keyPath = item.Path[(separator + 1)..];
            using var rootKey = rootName switch
            {
                "HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                "HKEY_CURRENT_USER" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
                _ => null
            };
            if (rootKey is null || rootKey.OpenSubKey(keyPath) is null)
            {
                return false;
            }

            rootKey.DeleteSubKeyTree(keyPath, false);
            return true;
        }

        return false;
    }

    internal static double Similarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0d;
        }

        var first = NormalizeName(left);
        var second = NormalizeName(right);
        if (first.Length == 0 || second.Length == 0)
        {
            return 0d;
        }

        var distance = new int[first.Length + 1, second.Length + 1];
        for (var index = 0; index <= first.Length; index++)
        {
            distance[index, 0] = index;
        }

        for (var index = 0; index <= second.Length; index++)
        {
            distance[0, index] = index;
        }

        for (var firstIndex = 1; firstIndex <= first.Length; firstIndex++)
        {
            for (var secondIndex = 1; secondIndex <= second.Length; secondIndex++)
            {
                var substitution = distance[firstIndex - 1, secondIndex - 1] + (first[firstIndex - 1] == second[secondIndex - 1] ? 0 : 1);
                distance[firstIndex, secondIndex] = Math.Min(
                    Math.Min(distance[firstIndex - 1, secondIndex] + 1, distance[firstIndex, secondIndex - 1] + 1),
                    substitution);
            }
        }

        return 1d - distance[first.Length, second.Length] / Math.Max(first.Length, second.Length);
    }

    private static string NormalizeName(string value)
    {
        return string.Join(string.Empty, value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant));
    }
}
