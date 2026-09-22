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

    /// <summary>整棵目录树受保护的根（含根自身）。</summary>
    public static IReadOnlyList<string> Roots { get; } = BuildRoots();

    /// <summary>仅目录自身受保护、子目录不保护的根。</summary>
    public static IReadOnlyList<string> ExactOnlyRoots { get; } = BuildExactOnlyRoots();

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

        foreach (var candidate in Roots)
        {
            if (IsUnder(fullPath, candidate))
            {
                return candidate;
            }
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
