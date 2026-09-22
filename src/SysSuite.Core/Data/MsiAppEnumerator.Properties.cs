namespace SysSuite.Core.Data;

public sealed partial class MsiAppEnumerator
{
    private static string? GetProperty(string productCode, string propertyName)
    {
        foreach (var context in Enum.GetValues<InstallContext>())
        {
            var buffer = new char[MaximumStringLength];
            var length = buffer.Length;
            var result = NativeMethods.MsiGetProductInfoExW(
                productCode,
                null,
                (int)context,
                propertyName,
                buffer,
                ref length);
            if (result == 0 && length > 0)
            {
                return new string(buffer, 0, length);
            }
        }

        return null;
    }

    private static bool? GetBooleanProperty(string productCode, string propertyName)
    {
        foreach (var context in Enum.GetValues<InstallContext>())
        {
            var buffer = new char[MaximumStringLength];
            var length = buffer.Length;
            var result = NativeMethods.MsiGetProductInfoExW(
                productCode,
                null,
                (int)context,
                propertyName,
                buffer,
                ref length);
            if (result != 0 || length == 0) continue;
            var value = new string(buffer, 0, length);
            if (int.TryParse(value, out var num)) return num != 0;
        }
        return null;
    }

    private static string? ReadRecordString(uint record, uint field)
    {
        var buffer = new char[MaximumStringLength];
        var length = buffer.Length;
        var result = NativeMethods.MsiRecordGetStringW(record, field, buffer, ref length);
        return result == 0 && length > 0 ? new string(buffer, 0, length) : null;
    }

    private static string? ReadDatabaseString(uint database, string query)
    {
        var view = 0u;
        if (NativeMethods.MsiDatabaseOpenViewW(database, query, out view) != 0)
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
                return ReadRecordString(record, 1);
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
}
