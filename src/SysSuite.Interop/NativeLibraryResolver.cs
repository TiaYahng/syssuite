using System.Reflection;
using System.Runtime.InteropServices;

namespace SysSuite.Interop;

/// <summary>
/// 原生库定位与加载。解析顺序见 <see cref="EnumerateCandidates"/>；
/// 解析失败时所有原生能力降级为 <see cref="NativeStatus.LibraryNotLoaded"/>，
/// 绝不允许抛 DllNotFoundException 打断 UI 启动链路。
/// </summary>
internal static class NativeLibraryResolver
{
    private const string FileName = "SysSuite.Native.dll";
    private const string PathVariable = "SYSSUITE_NATIVE_PATH";
    private const int MaxParentLookup = 6;

    private static int registered;
    private static string? resolvedPath;

    /// <summary>已解析到的库绝对路径；未找到时为 null。</summary>
    internal static string? ResolvedPath => resolvedPath;

    /// <summary>尝试加载原生库；成功返回 true 并缓存路径。</summary>
    internal static bool TryLoad(out IntPtr handle)
    {
        handle = IntPtr.Zero;
        foreach (var candidate in EnumerateCandidates())
        {
            if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(candidate, out var loaded) && loaded != IntPtr.Zero)
            {
                resolvedPath = candidate;
                handle = loaded;
                return true;
            }
        }

        return false;
    }

    /// <summary>为 DllImport / LibraryImport 注册解析器（每个程序集仅允许一次）。</summary>
    internal static void EnsureRegistered()
    {
        if (Interlocked.CompareExchange(ref registered, 1, 0) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NativeMethods.LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        return TryLoad(out var handle) ? handle : IntPtr.Zero;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var configured = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return Directory.Exists(configured)
                ? Path.Combine(configured, FileName)
                : configured;
        }

        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, FileName);
        yield return Path.Combine(baseDirectory, "runtimes", "win-x64", "native", FileName);

        // 开发期便利：从输出目录向上回溯 CMake 的默认产物位置
        var directory = new DirectoryInfo(baseDirectory);
        for (var depth = 0; depth < MaxParentLookup && directory is not null; depth++)
        {
            var root = directory.FullName;
            yield return Path.Combine(root, "build", "native", "Release", FileName);
            yield return Path.Combine(root, "build", "native", "Debug", FileName);
            yield return Path.Combine(root, "build", "Release", FileName);
            yield return Path.Combine(root, "artifacts", "native", FileName);
            directory = directory.Parent;
        }
    }
}
