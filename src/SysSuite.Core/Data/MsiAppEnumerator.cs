using System.ComponentModel;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class MsiAppEnumerator
{
    private const int ErrorNoMoreItems = 259;
    private const int MaximumStringLength = 8192;

    public static Result<IReadOnlyList<AppRecord>> Enumerate(IReadOnlySet<string>? visibleProductCodes = null)
    {
        try
        {
            var apps = new List<AppRecord>();
            var productCode = new char[39];
            for (var index = 0; ; index++)
            {
                Array.Clear(productCode);
                var result = NativeMethods.MsiEnumProductsW(index, productCode);
                if (result == ErrorNoMoreItems)
                {
                    break;
                }

                if (result != 0)
                {
                    continue;
                }

                var terminatorIndex = Array.IndexOf(productCode, '\0');
                if (terminatorIndex <= 0)
                {
                    continue;
                }

                var product = new string(productCode, 0, terminatorIndex);
                var name = GetProperty(product, "ProductName");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var estimatedSize = long.TryParse(GetProperty(product, "EstimatedSize"), out var size)
                    ? (long?)size * 1024
                    : null;
                var systemComponent = GetBooleanProperty(product, "SystemComponent");
                var parentKeyName = GetProperty(product, "ParentKeyName");
                var releaseType = GetProperty(product, "ReleaseType");
                if (systemComponent == true
                    || !string.IsNullOrWhiteSpace(parentKeyName)
                    || releaseType is not null && int.TryParse(releaseType, out var releaseTypeValue) && releaseTypeValue >= 2)
                {
                    continue;
                }
                if (visibleProductCodes is not null && !visibleProductCodes.Contains(product))
                {
                    continue;
                }
                apps.Add(AppRecord.Create(
                    $"MSI|{product}",
                    name,
                    AppSource.Msi,
                    GetProperty(product, "Publisher"),
                    GetProperty(product, "DisplayVersion"),
                    GetProperty(product, "InstallDate"),
                    estimatedSize,
                    $"msiexec /x {product}",
                    $"msiexec /x {product} /qn",
                    product,
                    GetProperty(product, "InstallLocation")));
            }

            return new Result<IReadOnlyList<AppRecord>>(ErrorType.None, string.Empty, apps);
        }
        catch (Win32Exception exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.DependencyMissing, exception.Message);
        }
        catch (Exception exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.Internal, exception.Message);
        }
    }

    public static string? GetIconPath(string productCode)
    {
        return GetProperty(productCode, "ProductIcon");
    }

    private enum InstallContext
    {
        UserManaged = 1,
        User = 2,
        Machine = 4
    }
}
