namespace SysSuite.Core.Data;

public sealed partial class MsiAppEnumerator
{
    /// <summary>
    /// 按已解析的安装上下文读取属性。
    /// 传 null 时保留"逐个上下文探测"的兼容语义，供少量一次性调用方使用。
    /// </summary>
    private static string? GetProperty(string productCode, string propertyName, InstallContext? context = null)
    {
        if (context is { } single)
        {
            return QueryProperty(productCode, propertyName, single, out var value) ? value : null;
        }

        foreach (var candidate in Enum.GetValues<InstallContext>())
        {
            if (QueryProperty(productCode, propertyName, candidate, out var found))
            {
                return found;
            }
        }

        return null;
    }

    private static bool? GetBooleanProperty(string productCode, string propertyName, InstallContext context)
    {
        if (!QueryProperty(productCode, propertyName, context, out var value))
        {
            return null;
        }

        return int.TryParse(value, out var number) ? number != 0 : null;
    }

    private static bool QueryProperty(
        string productCode,
        string propertyName,
        InstallContext context,
        out string value)
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
            value = new string(buffer, 0, length);
            return true;
        }

        value = string.Empty;
        return false;
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
