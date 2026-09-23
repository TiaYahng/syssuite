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

    /// <summary>
    /// 查询所有物理盘的 SMART 健康信息（T1.3）。
    ///
    /// 语义约定：
    ///   * 单块盘不支持 SMART（RAID 虚拟盘、部分 USB 桥）不影响整体结果，该盘
    ///     <see cref="SmartDriveInfo.IsAvailable"/> = false、健康度为 <see cref="SmartHealthStatus.Unknown"/>。
    ///   * 返回的状态码反映**整体调用**是否成功；权限不足时返回
    ///     <see cref="NativeStatus.AccessDenied"/> 且列表为空（需提权后重试）。
    /// </summary>
    public static unsafe NativeStatus QuerySmart(out IReadOnlyList<SmartDriveInfo> drives)
    {
        drives = [];
        if (!EnsureLoaded(out var status))
        {
            return status;
        }

        const int Capacity = 16;
        var buffer = new NativeSmartInfo[Capacity];
        for (var i = 0; i < Capacity; i++)
        {
            buffer[i].StructSize = (uint)NativeSmartInfo.SizeOf;
        }

        int code;
        var count = 0;
        fixed (NativeSmartInfo* pointer = buffer)
        {
            code = NativeMethods.QuerySmart(pointer, Capacity, out count, null, IntPtr.Zero);
        }

        var result = NativeStatusText.FromCode(code);

        // 即使返回 CANCELLED 也保留已探测到的部分结果
        if (count <= 0)
        {
            return result;
        }

        var list = new List<SmartDriveInfo>(count);
        for (var i = 0; i < count && i < Capacity; i++)
        {
            list.Add(Translate(ref buffer[i]));
        }

        drives = list;
        return result;
    }

    private static SmartDriveInfo Translate(ref NativeSmartInfo native)
    {
        var available = native.IsAvailable == 1;
        return new SmartDriveInfo(
            (int)native.DriveNumber,
            native.ReadModel(),
            native.ReadSerial(),
            native.ReadFirmware(),
            available,
            (SmartHealthStatus)native.HealthStatus,
            (int)native.TemperatureCelsius,
            // 0 表示该项不可用，用 null 让 UI 区分"读到 0"和"没读出来"
            native.RemainingLifePercent == 0 ? null : (int)native.RemainingLifePercent,
            native.ReallocatedSectors == 0 ? null : native.ReallocatedSectors,
            native.PendingSectors == 0 ? null : native.PendingSectors,
            native.UncorrectableErrors == 0 ? null : native.UncorrectableErrors,
            (long)Math.Min(native.PowerOnHours, long.MaxValue),
            (long)Math.Min(native.PowerCycleCount, long.MaxValue),
            native.TotalBytesWritten == 0 ? null : (long)Math.Min(native.TotalBytesWritten, long.MaxValue),
            (int)native.AtaSmartAttributeCount);
    }

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
