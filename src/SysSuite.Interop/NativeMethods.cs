using System.Runtime.InteropServices;

namespace SysSuite.Interop;

/// <summary>
/// SysSuite.Native.dll 的 P/Invoke 绑定。签名与 docs/native-abi.md 一一对应，
/// 任何变更都必须同步修订 ABI 文档并递增 NATIVE_ABI_VERSION。
/// </summary>
internal static partial class NativeMethods
{
    internal const string LibraryName = "SysSuite.Native";

    /// <summary>与 native_api.h 的 NATIVE_ABI_VERSION 保持一致。</summary>
    internal const int ExpectedAbiVersion = 1;

    /// <summary>与 native_api.h 的 NATIVE_VERSION_CAPACITY_MIN 保持一致。</summary>
    internal const int VersionCapacityMin = 32;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeScanCallback(
        IntPtr context,
        IntPtr entryIds,
        uint entryCount,
        ulong processed,
        ulong total);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NativeCancelCheck(IntPtr context);

    [LibraryImport(LibraryName, EntryPoint = "Native_AbiVersion")]
    internal static partial int AbiVersion();

    // 输出缓冲区一律由调用方分配，这里使用裸指针是为了保留运行时封送能力
    // （委托 → 函数指针的封送在 DisableRuntimeMarshalling 下不可用），见 docs/native-abi.md §3
    [LibraryImport(LibraryName, EntryPoint = "Native_Version")]
    internal static unsafe partial int Version(char* buffer, int capacity);

    [LibraryImport(LibraryName, EntryPoint = "Native_GetSmbios")]
    internal static unsafe partial int GetSmbios(byte* buffer, int capacity, out int written);

    [LibraryImport(LibraryName, EntryPoint = "Native_ScanVolume", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ScanVolume(
        string volume,
        NativeScanCallback? onBatch,
        IntPtr context,
        NativeCancelCheck? isCancelled);
}
