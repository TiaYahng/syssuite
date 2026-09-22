namespace SysSuite.Core.Data;

public sealed partial class MsiAppEnumerator
{
    public static string? GetEmbeddedIconPath(string productCode, string destinationDirectory)
    {
        var localPackage = GetProperty(productCode, "LocalPackage");
        if (string.IsNullOrWhiteSpace(localPackage) || !File.Exists(localPackage))
        {
            return null;
        }

        var database = 0u;
        if (NativeMethods.MsiOpenDatabaseW(localPackage, nint.Zero, out database) != 0)
        {
            return null;
        }

        try
        {
            var iconName = ReadDatabaseString(database, "SELECT Value FROM Property WHERE Property = 'ARPPRODUCTICON'");
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return null;
            }

            var escapedIconName = iconName.Replace("'", "''");
            var view = 0u;
            if (NativeMethods.MsiDatabaseOpenViewW(
                database,
                $"SELECT Data FROM Icon WHERE Name = '{escapedIconName}'",
                out view) != 0)
            {
                return null;
            }

            try
            {
                if (NativeMethods.MsiViewExecute(view, 0) != 0)
                {
                    return null;
                }

                var record = 0u;
                if (NativeMethods.MsiViewFetch(view, out record) != 0)
                {
                    return null;
                }

                try
                {
                    Directory.CreateDirectory(destinationDirectory);
                    var safeProductCode = string.Join("_", productCode.Split(Path.GetInvalidFileNameChars()));
                    var safeIconName = string.Join("_", iconName.Split(Path.GetInvalidFileNameChars()));
                    return ReadIconStream(record, 1, Path.Combine(destinationDirectory, $"{safeProductCode}-{safeIconName}"));
                }
                finally
                {
                    _ = NativeMethods.MsiCloseHandle(record);
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
    }

    private static string? ReadIconStream(uint record, uint field, string pathWithoutExtension)
    {
        var buffer = new byte[MaximumStringLength];
        var length = buffer.Length;
        if (NativeMethods.MsiRecordReadStream(record, field, buffer, ref length) != 0 || length == 0)
        {
            return null;
        }

        var extension = buffer[0] == 0x4D && buffer[1] == 0x5A ? ".exe" : ".ico";
        var path = pathWithoutExtension + extension;
        using var stream = File.Create(path);
        while (true)
        {
            stream.Write(buffer, 0, length);
            length = buffer.Length;
            if (NativeMethods.MsiRecordReadStream(record, field, buffer, ref length) != 0)
            {
                return null;
            }

            if (length == 0)
            {
                return path;
            }
        }
    }
}
