using SysSuite.Interop;

namespace SysSuite.Core.System;

public sealed record VolumeFileEntry(string Path, bool IsDirectory, long SizeBytes);

public enum VolumeIndexChannel
{
    /// <summary>原生 MFT / USN 通道。</summary>
    NativeMft,

    /// <summary>降级通道：非 NTFS、原生模块缺失或无权限时使用。</summary>
    FindFirstFile,

    /// <summary>卷不可枚举（不存在、未就绪）。</summary>
    Unsupported,
}

public sealed record VolumeIndexResult(
    VolumeIndexChannel Channel,
    IReadOnlyList<VolumeFileEntry> Entries,
    string? Note);

/// <summary>
/// 卷级文件索引（T3.2）。
/// </summary>
/// <remarks>
/// 两条通道对调用方呈现**同一个结果模型**，差别的只是快慢与权限要求：
///
/// * 原生 MFT 通道（<see cref="NativeInterop.ScanVolumeEntries"/>）顺序读 USN 日志，
///   省掉层层 opendir，在目录树深、目录多时明显更快；代价是需要能打开卷句柄（通常要提权），
///   且 USN 记录**不含文件大小**，所以这里给出的 <see cref="VolumeFileEntry.SizeBytes"/> 恒为 0。
/// * FindFirstFile 通道是保底：任何卷都能跑，且顺带拿到大小，但慢得多。
///
/// 调用方不该假定能拿到大小 —— 需要大小就自己 stat，别把 0 当成"空文件"。
/// </remarks>
public static class MftFileIndexService
{
    private const int MaxDepth = 4096;

    private static readonly EnumerationOptions WalkOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>验收项：这两个目录必须被排除，枚举到它们既无意义也常伴权限错误。</summary>
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        "System Volume Information",
    };

    public static bool IsSupported(string root)
    {
        try
        {
            var drive = Path.GetPathRoot(root);
            return !string.IsNullOrWhiteSpace(drive)
                && new DriveInfo(drive).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static VolumeIndexResult Enumerate(string root, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new VolumeIndexResult(VolumeIndexChannel.Unsupported, [], "卷不存在。");
        }

        var mft = TryEnumerateByMft(root, cancellationToken);
        if (mft is not null)
        {
            return mft;
        }

        var fallback = EnumerateByFindFirstFile(root, cancellationToken);
        return new VolumeIndexResult(
            VolumeIndexChannel.FindFirstFile,
            fallback,
            "MFT 通道不可用（需要 NTFS 与卷句柄权限），已降级为目录遍历。");
    }

    /// <summary>把 <c>C:\</c> 形式的根转成 <c>\\?\C:</c>：打开卷句柄需要的就是这个形态。</summary>
    public static string ToVolumePath(string root)
    {
        var drive = Path.GetPathRoot(root) ?? root;
        return @"\\?\" + drive.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static VolumeIndexResult? TryEnumerateByMft(string root, CancellationToken cancellationToken)
    {
        if (!IsSupported(root) || !NativeInterop.IsLibraryAvailable)
        {
            return null;
        }

        var entries = new List<(ulong File, ulong Parent, string Name, bool IsDirectory)>();
        var status = NativeInterop.ScanVolumeEntries(
            ToVolumePath(root),
            batch =>
            {
                foreach (var entry in batch)
                {
                    var name = entry.ReadName();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    entries.Add((entry.FileReference, entry.ParentReference, name, entry.IsDirectory));
                }

                return !cancellationToken.IsCancellationRequested;
            },
            () => cancellationToken.IsCancellationRequested);

        // 原生失败一律交给降级通道：扫描能力不该因为"拿不到卷句柄"而整体不可用
        if (status != NativeStatus.Ok || entries.Count == 0)
        {
            return null;
        }

        var paths = RebuildPaths(entries, root);
        return new VolumeIndexResult(
            VolumeIndexChannel.NativeMft,
            paths,
            NativeInterop.Describe(status) is { Length: > 0 } note ? note : null);
    }

    private static List<VolumeFileEntry> EnumerateByFindFirstFile(string root, CancellationToken cancellationToken)
    {
        var results = new List<VolumeFileEntry>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            FileSystemInfo[] children;
            try
            {
                children = directory.GetFileSystemInfos("*", WalkOptions);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (child is DirectoryInfo childDirectory)
                {
                    if (ExcludedDirectoryNames.Contains(childDirectory.Name))
                    {
                        continue;
                    }

                    results.Add(new VolumeFileEntry(childDirectory.FullName, true, 0));
                    pending.Push(childDirectory);
                }
                else if (child is FileInfo file)
                {
                    results.Add(new VolumeFileEntry(file.FullName, false, file.Length));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 按父引用重建完整路径。
    /// </summary>
    /// <remarks>
    /// 刻意写成**迭代 + 显式栈**而不是递归：真实卷的目录深度能到几十层，但引用链
    /// 偶尔会因记录不完整而出现长链，递归版本会在那种输入上直接栈溢出。
    /// 同时沿链做路径缓存，避免同一棵子树被重复拼上百万次。
    /// </remarks>
    public static List<VolumeFileEntry> RebuildPaths(
        IReadOnlyList<(ulong File, ulong Parent, string Name, bool IsDirectory)> entries,
        string root)
    {
        var byReference = new Dictionary<ulong, (ulong File, ulong Parent, string Name, bool IsDirectory)>(entries.Count);
        foreach (var entry in entries)
        {
            byReference[entry.File] = entry;
        }

        var cache = new Dictionary<ulong, string>();
        var results = new List<VolumeFileEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var path = ResolvePath(entry.File, byReference, cache, root);
            if (path is not null)
            {
                results.Add(new VolumeFileEntry(path, entry.IsDirectory, 0));
            }
        }

        return results;
    }

    private static string? ResolvePath(
        ulong reference,
        Dictionary<ulong, (ulong File, ulong Parent, string Name, bool IsDirectory)> byReference,
        Dictionary<ulong, string> cache,
        string root)
    {
        if (cache.TryGetValue(reference, out var cached))
        {
            return cached;
        }

        var chain = new List<ulong>(32);
        string? basePath = null;
        var current = reference;
        while (true)
        {
            if (cache.TryGetValue(current, out var hit))
            {
                basePath = hit;
                break;
            }

            if (!byReference.TryGetValue(current, out var entry))
            {
                return null;
            }

            chain.Add(current);
            var parent = entry.Parent;

            // 自身引用或父不在表内 → 它是卷根的直接子项
            if (parent == current || !byReference.ContainsKey(parent))
            {
                break;
            }

            current = parent;
            if (chain.Count > MaxDepth)
            {
                return null;
            }
        }

        var path = basePath ?? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var entry = byReference[chain[i]];
            if (ExcludedDirectoryNames.Contains(entry.Name))
            {
                return null;
            }

            path = Path.Combine(path, entry.Name);
            cache[chain[i]] = path;
        }

        return path;
    }
}
