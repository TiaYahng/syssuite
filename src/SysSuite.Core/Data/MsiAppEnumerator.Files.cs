namespace SysSuite.Core.Data;

public sealed partial class MsiAppEnumerator
{
    public static IReadOnlyList<string> GetInstalledExecutablePaths(string productCode)
    {
        var localPackage = GetProperty(productCode, "LocalPackage");
        if (string.IsNullOrWhiteSpace(localPackage) || !File.Exists(localPackage))
        {
            return Array.Empty<string>();
        }

        var database = 0u;
        if (NativeMethods.MsiOpenDatabaseW(localPackage, nint.Zero, out database) != 0)
        {
            return Array.Empty<string>();
        }

        var components = new List<(string FileName, string ComponentId)>();
        try
        {
            var view = 0u;
            if (NativeMethods.MsiDatabaseOpenViewW(
                database,
                "SELECT File.FileName, Component.ComponentId FROM File, Component WHERE File.Component_ = Component.Component AND Component.KeyPath = File.File",
                out view) != 0)
            {
                return Array.Empty<string>();
            }

            try
            {
                var executeResult = NativeMethods.MsiViewExecute(view, 0);
                if (executeResult != 0)
                {
                    return Array.Empty<string>();
                }

                while (true)
                {
                    var record = 0u;
                    var result = NativeMethods.MsiViewFetch(view, out record);
                    if (result == ErrorNoMoreItems)
                    {
                        break;
                    }

                    if (result != 0)
                    {
                        continue;
                    }

                    try
                    {
                        var fileName = ReadRecordString(record, 1);
                        var componentId = ReadRecordString(record, 2);
                        if (fileName is null || componentId is null)
                        {
                            continue;
                        }

                        var separator = fileName.IndexOf('|');
                        var longFileName = separator >= 0 ? fileName[(separator + 1)..] : fileName;
                        if (longFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            components.Add((longFileName, componentId));
                        }
                    }
                    finally
                    {
                        _ = NativeMethods.MsiCloseHandle(record);
                    }
                }
            }
            finally
            {
                if (view != 0)
                {
                    _ = NativeMethods.MsiCloseHandle(view);
                }
            }
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(database);
        }

        return ResolveLocalComponentPaths(productCode, components)
            .Where(path => File.Exists(path))
            .OrderBy(path => !IsPreferredExecutable(path))
            .ThenBy(path => path.Length)
            .ToList();
    }

    private static bool IsPreferredExecutable(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.Contains("main", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("app", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("start", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ResolveLocalComponentPaths(
        string productCode,
        IReadOnlyList<(string FileName, string ComponentId)> components)
    {
        foreach (var component in components)
        {
            foreach (var context in Enum.GetValues<InstallContext>())
            {
                var buffer = new char[MaximumStringLength];
                var length = buffer.Length;
                var state = NativeMethods.MsiGetComponentPathExW(
                    productCode,
                    component.ComponentId,
                    null,
                    (int)context,
                    buffer,
                    ref length);
                if (state is 3 or 4 && length > 0)
                {
                    var path = new string(buffer, 0, length);
                    if (File.Exists(path))
                    {
                        yield return path;
                    }

                    break;
                }
            }
        }
    }
}
