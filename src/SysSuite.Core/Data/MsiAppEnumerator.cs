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
            // 每个 productCode 只解析一次安装上下文。此前 GetProperty 会对每个属性
            // 逐个试 3 个 context，等于把 MsiGetProductInfoExW 的调用次数翻了三倍；
            // 而 context 是与 productCode 绑定的，探一次即可复用。
            var contextCache = new Dictionary<string, InstallContext>(StringComparer.OrdinalIgnoreCase);
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
                var context = ResolveContext(product, contextCache);
                var name = GetProperty(product, "ProductName", context);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var estimatedSize = long.TryParse(GetProperty(product, "EstimatedSize", context), out var size)
                    ? (long?)size * 1024
                    : null;
                var systemComponent = GetBooleanProperty(product, "SystemComponent", context);
                var parentKeyName = GetProperty(product, "ParentKeyName", context);
                var releaseType = GetProperty(product, "ReleaseType", context);
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
                    GetProperty(product, "Publisher", context),
                    GetProperty(product, "DisplayVersion", context),
                    GetProperty(product, "InstallDate", context),
                    estimatedSize,
                    $"msiexec /x {product}",
                    $"msiexec /x {product} /qn",
                    product,
                    GetProperty(product, "InstallLocation", context)));
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

    /// <summary>
    /// 探出该产品的安装上下文并缓存。用成本最低、且在所有上下文都存在的属性
    /// （ProductName）做探针；全部上下文都失败时退回 <see cref="InstallContext.Machine"/>。
    /// </summary>
    private static InstallContext ResolveContext(string productCode, Dictionary<string, InstallContext> cache)
    {
        if (cache.TryGetValue(productCode, out var cached))
        {
            return cached;
        }

        var resolved = InstallContext.Machine;
        foreach (var context in Enum.GetValues<InstallContext>())
        {
            if (QueryProperty(productCode, "ProductName", context, out _))
            {
                resolved = context;
                break;
            }
        }

        cache[productCode] = resolved;
        return resolved;
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
