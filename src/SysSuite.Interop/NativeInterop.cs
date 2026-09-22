using System.Runtime.InteropServices;

namespace SysSuite.Interop;

/// <summary>
/// 原生能力门面。所有调用都在加载失败时降级为状态码而非异常，
/// 以保证"原生模块缺失"永远不会让 UI 启动链路崩溃（G6）。
/// </summary>
public static class NativeInterop
{
    private static readonly object Sync = new();
    private static bool initialized;
    private static IntPtr libraryHandle;

    /// <summary>原生库是否已成功加载。</summary>
    public static bool IsLibraryAvailable => EnsureInitialized();

    /// <summary>已加载库的绝对路径；未加载时为 null。</summary>
    public static string? LibraryPath
    {
        get
        {
            EnsureInitialized();
            return NativeLibraryResolver.ResolvedPath;
        }
    }

    /// <summary>读取 ABI 版本并完成版本握手。</summary>
    public static NativeStatus GetAbiVersion(out int version)
    {
        version = 0;
        if (!EnsureLoaded(out var status))
        {
            return status;
        }

        version = NativeMethods.AbiVersion();
        return version == NativeMethods.ExpectedAbiVersion
            ? NativeStatus.Ok
            : NativeStatus.AbiMismatch;
    }

    /// <summary>读取原生模块版本串（使用推荐缓冲区容量）。</summary>
    public static NativeStatus GetVersion(out string? version)
    {
        return GetVersion(new char[NativeMethods.VersionCapacityMin], out version);
    }

    /// <summary>使用调用方提供的缓冲区读取版本串，便于验证"缓冲区不足"路径。</summary>
    public static unsafe NativeStatus GetVersion(Span<char> buffer, out string? version)
    {
        version = null;
        if (!EnsureLoaded(out var status))
        {
            return status;
        }

        int code;
        fixed (char* pointer = buffer)
        {
            code = NativeMethods.Version(pointer, buffer.Length);
        }

        var result = NativeStatusText.FromCode(code);
        if (result != NativeStatus.Ok)
        {
            return result;
        }

        var terminator = buffer.IndexOf('\0');
        version = new string(buffer[..(terminator < 0 ? buffer.Length : terminator)]);
        return NativeStatus.Ok;
    }

    /// <summary>读取 SMBIOS 原始表（T1.3，当前原生侧返回未实现）。</summary>
    public static unsafe NativeStatus GetSmbios(Span<byte> buffer, out int written)
    {
        written = 0;
        if (!EnsureLoaded(out var status))
        {
            return status;
        }

        int code;
        fixed (byte* pointer = buffer)
        {
            code = NativeMethods.GetSmbios(pointer, buffer.Length, out var bytesWritten);
            if (code == 0)
            {
                written = bytesWritten;
            }
        }

        return NativeStatusText.FromCode(code);
    }

    /// <summary>卷扫描（T3.2，当前原生侧仅实现取消与参数校验）。</summary>
    public static NativeStatus ScanVolume(string volume, Func<bool>? isCancelled = null)
    {
        if (string.IsNullOrWhiteSpace(volume))
        {
            return NativeStatus.InvalidArgument;
        }

        if (!EnsureLoaded(out var status))
        {
            return status;
        }

        NativeMethods.NativeCancelCheck? check = null;
        if (isCancelled is not null)
        {
            check = _ => isCancelled() ? 1 : 0;
        }

        var code = NativeMethods.ScanVolume(volume, null, IntPtr.Zero, check);
        GC.KeepAlive(check);
        return NativeStatusText.FromCode(code);
    }

    /// <summary>状态码描述，供 UI 与日志直接使用。</summary>
    public static string Describe(NativeStatus status) => NativeStatusText.Describe(status);

    private static bool EnsureLoaded(out NativeStatus status)
    {
        if (!EnsureInitialized())
        {
            status = NativeStatus.LibraryNotLoaded;
            return false;
        }

        status = NativeStatus.Ok;
        return true;
    }

    private static bool EnsureInitialized()
    {
        if (initialized)
        {
            return libraryHandle != IntPtr.Zero;
        }

        lock (Sync)
        {
            if (initialized)
            {
                return libraryHandle != IntPtr.Zero;
            }

            NativeLibraryResolver.EnsureRegistered();
            initialized = true;
            libraryHandle = NativeLibraryResolver.TryLoad(out var handle) ? handle : IntPtr.Zero;
        }

        return libraryHandle != IntPtr.Zero;
    }
}
