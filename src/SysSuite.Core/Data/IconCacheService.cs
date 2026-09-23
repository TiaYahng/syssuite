using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class IconCacheService : IIconCacheService, IDisposable
{
    private const int CacheVersion = 8;
    private static readonly EnumerationOptions SearchOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false
    };

    private readonly string cacheRoot;
    private readonly SemaphoreSlim concurrencyLimit = new(6, 6);
    private readonly ConcurrentDictionary<string, IReadOnlyList<IconSource>> sourceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IReadOnlyList<ShortcutCandidate>> shortcutIndex;

    /// <summary>
    /// 快捷方式索引项。除原始信息外，预存了评分阶段用得到的派生值 ——
    /// 这些值只依赖快捷方式本身，与具体应用无关，索引建好即可固定，
    /// 避免每个应用都重算一遍（旧实现每应用每快捷方式都要分词 + 命中磁盘）。
    /// </summary>
    private readonly record struct ShortcutCandidate(
        IReadOnlySet<string> NameTokens,
        string NormalizedLinkName,
        string? TargetPath,
        string IconPath,
        int IconIndex,
        bool IsUgraf);

    private readonly record struct IconSource(string Path, int IconIndex);

    public IconCacheService(string? cacheRoot = null)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysSuite",
            "iconcache"));
        Directory.CreateDirectory(this.cacheRoot);
        shortcutIndex = new Lazy<IReadOnlyList<ShortcutCandidate>>(BuildShortcutIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<Result<string?>> GetIconPathAsync(AppRecord app, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(app.StableKey);

        // 解析图标来源只做一次：ResolveIconSources 会读注册表、走 COM、甚至枚举安装目录，
        // 而这些结果对同一个 AppRecord 是不会变的。此前把整个解析放在信号量之内，
        // 导致并发度实际被压到 1，几百个应用串行解析，这是刷新慢的主因。
        var iconSources = sourceCache.GetOrAdd(
            $"{app.Source}|{app.StableKey}",
            _ => ResolveIconSources(app));
        if (iconSources.Count == 0)
        {
            return new Result<string?>(ErrorType.None, string.Empty, null);
        }

        var iconKey = string.Join('|', iconSources.Select(source => $"{source.Path}|{source.IconIndex}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{CacheVersion}|{app.Source}|{app.StableKey}|{iconKey}")));
        var cachedPath = Path.Combine(cacheRoot, $"{hash}.png");
        if (File.Exists(cachedPath))
        {
            return new Result<string?>(ErrorType.None, string.Empty, cachedPath);
        }

        // 只有真正要抽图标（解码 + 落盘）时才排队限流
        await concurrencyLimit.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cachedPath))
            {
                return new Result<string?>(ErrorType.None, string.Empty, cachedPath);
            }

            return await Task.Run(() => ExtractFirstAvailableIcon(app.StableKey, iconSources, cachedPath), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new Result<string?>(ErrorType.Cancelled, "图标刷新已取消。");
        }
        catch (Exception exception)
        {
            return new Result<string?>(ErrorType.Internal, exception.Message, null);
        }
        finally
        {
            concurrencyLimit.Release();
        }
    }

    private List<IconSource> ResolveIconSources(AppRecord app)
    {
        var sources = new List<IconSource>();
        if (app.Source != AppSource.Msi)
        {
            AddIconReference(sources, app.IconPath, app.InstallDir);
        }

        if (sources.Count == 0 && IsRustup(app))
        {
            AddIconReference(sources, app.UninstallString, null);
        }

        if (app.Source == AppSource.Msi)
        {
            sources.AddRange(FindMsiIcons(app, Path.Combine(cacheRoot, "msi-icons")));
        }

        if (sources.Count == 0)
        {
            sources.AddRange(FindShortcutIcons(app));
        }

        if (sources.Count == 0)
        {
            var installDirectory = NormalizeDirectory(app.InstallDir) ?? DeriveInstallDirectoryFromUninstallString(app.UninstallString);
            if (installDirectory is not null)
            {
                AddInstallDirectoryIcons(sources, app, installDirectory);
            }
        }

        return sources.Distinct().ToList();
    }

    public void Dispose()
    {
        concurrencyLimit.Dispose();
    }
}
