namespace SysSuite.Core.System;

/// <summary>
/// G7 破坏性操作红线：全模块共享的系统目录白名单。
/// 任何删除、注册表写入、防护变更都必须先过 <see cref="IsProtected"/>。
/// </summary>
/// <remarks>
/// 设计原则：
/// 1. **fail-closed**：路径为空、无法规范化、无法判定时一律视为受保护；
/// 2. **只读**：常量表本身不可被业务代码修改，避免"绕过白名单"的实现方式；
/// 3. 前缀保护用于系统自有目录树，精确保护用于"容器型"目录（用户主目录、ProgramData），
///    后者只拒绝自身，否则会连正常的残留清理一并误伤。
///
/// 关于例外：早期实现把整棵 <c>Windows</c> 目录保护起来，导致清理规则里所有落在
/// <c>Windows\Temp</c>、<c>Windows\SoftwareDistribution\Download</c> 的条目
/// 「扫描得到、删除必失败」—— 规则集形同虚设（偏差 D21）。因此引入
/// <see cref="CleanableRoots"/>：**只有在已确认落在保护树内之后**才检查例外，
/// 且例外必须是保护根的子路径。段级红线（WinSxS / $Recycle.Bin 等）不参与例外。
/// </remarks>
public static class ProtectedPaths
{
    /// <summary>路径中任一目录段命中即视为受保护（跨盘符通用）。</summary>
    private static readonly string[] ProtectedSegments =
    [
        "$Recycle.Bin",
        "System Volume Information",
        "WindowsApps",
        "WinSxS",
    ];

    /// <summary>
    /// 相对 <c>Windows</c> 目录的可清理缓存子路径。
    /// 每一项都必须是 <see cref="Roots"/> 中某个根的**真子路径**，否则不生成例外 ——
    /// 宁可少清一项，也不接受一条能越过保护根的例外。
    ///
    /// 声明位置必须在 <see cref="CleanableRoots"/> 之前：静态字段按声明顺序初始化，
    /// 反了会在类型初始化期拿到 null。
    /// </summary>
    private static readonly string[] CleanableRelativePaths =
    [
        "Temp",
        "SoftwareDistribution\\Download",
        "SoftwareDistribution\\DeliveryOptimization",
        "Logs",
        "ServiceProfiles\\LocalService\\AppData\\Local\\FontCache",
        "System32\\Dns",
    ];

    /// <summary>整棵目录树受保护的根（含根自身）。</summary>
    public static IReadOnlyList<string> Roots { get; } = BuildRoots();

    /// <summary>仅目录自身受保护、子目录不保护的根。</summary>
    public static IReadOnlyList<string> ExactOnlyRoots { get; } = BuildExactOnlyRoots();

    /// <summary>
    /// 保护树内允许清理的缓存目录（<see cref="Roots"/> 的子路径）。
    /// 这些目录由 Windows 自身在需要时重建，删除其中内容不会破坏系统。
    /// </summary>
    public static IReadOnlyList<string> CleanableRoots { get; } = BuildCleanableRoots();

    /// <summary>判断路径是否位于保护范围内；无法判定时返回 true。</summary>
    public static bool IsProtected(string? path)
    {
        return DescribeMatch(path) is not null;
    }

    /// <summary>
    /// 返回命中的保护项（保护根或目录段），未命中返回 null。
    /// 供日志与 UI 解释"为什么被拒绝"，避免用户只看到一句笼统的失败提示。
    /// </summary>
    public static string? DescribeMatch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(空路径)";
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return "(路径非法)";
        }
        catch (NotSupportedException)
        {
            return "(路径非法)";
        }
        catch (PathTooLongException)
        {
            return "(路径过长)";
        }

        var root = Path.GetPathRoot(fullPath);
        if (root is not null && root.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (ProtectedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return segment;
            }
        }

        // 先确认落在保护树内，再判例外 —— 顺序不能反：否则一条配错的例外就能凭空放行任意路径
        var matchedRoot = Roots.FirstOrDefault(candidate => IsUnder(fullPath, candidate));
        if (matchedRoot is not null)
        {
            return CleanableRoots.Any(candidate => IsUnder(fullPath, candidate))
                ? null
                : matchedRoot;
        }

        foreach (var candidate in ExactOnlyRoots)
        {
            if (!string.IsNullOrEmpty(candidate) && fullPath.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsUnder(string fullPath, string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildRoots()
    {
        var roots = new List<string>();
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        return roots;
    }

    private static List<string> BuildCleanableRoots()
    {
        var roots = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows))
        {
            return roots;
        }

        foreach (var relative in CleanableRelativePaths)
        {
            var candidate = Path.Combine(windows, relative);
            // 例外不得落回段级红线（WinSxS 等），也不得与保护根自身相等
            var segments = candidate.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(segment => ProtectedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (Roots.Any(root => string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Add(roots, candidate);
        }

        return roots;
    }

    private static List<string> BuildExactOnlyRoots()
    {
        var roots = new List<string>();
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        return roots;
    }

    private static void Add(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(normalized);
        }
    }
}
