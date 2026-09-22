namespace SysSuite.Interop;

/// <summary>
/// 原生调用状态码。数值与 native_api.h 的错误码一一对应；
/// 100 段为托管侧自有的加载期状态，不出现在 ABI 中。
/// </summary>
public enum NativeStatus
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>参数为 null / 容量非正 / 格式非法。</summary>
    InvalidArgument = 1,

    /// <summary>调用方缓冲区不足，原生层未写入任何数据。</summary>
    BufferTooSmall = 2,

    /// <summary>该能力在当前原生版本中尚未实现。</summary>
    NotImplemented = 3,

    /// <summary>原生层自身内存分配失败。</summary>
    OutOfMemory = 4,

    /// <summary>权限不足（需提权或被安全软件拦截）。</summary>
    AccessDenied = 5,

    /// <summary>设备或文件 I/O 失败。</summary>
    IoError = 6,

    /// <summary>调用方通过取消回调主动中止。</summary>
    Cancelled = 7,

    /// <summary>未分类内部错误，需查日志。</summary>
    InternalError = 8,

    /// <summary>托管侧状态：SysSuite.Native.dll 未找到或加载失败。</summary>
    LibraryNotLoaded = 100,

    /// <summary>托管侧状态：ABI 版本与托管侧期望值不匹配。</summary>
    AbiMismatch = 101,

    /// <summary>托管侧状态：返回了未登记的原生错误码。</summary>
    Unknown = 102,
}

/// <summary>状态码到人类可读描述的映射，用于 UI 与日志。</summary>
public static class NativeStatusText
{
    public static string Describe(NativeStatus status)
    {
        return status switch
        {
            NativeStatus.Ok => "成功",
            NativeStatus.InvalidArgument => "参数非法",
            NativeStatus.BufferTooSmall => "缓冲区不足",
            NativeStatus.NotImplemented => "原生能力尚未实现",
            NativeStatus.OutOfMemory => "原生层内存不足",
            NativeStatus.AccessDenied => "权限不足",
            NativeStatus.IoError => "设备或文件 I/O 失败",
            NativeStatus.Cancelled => "已取消",
            NativeStatus.InternalError => "原生层内部错误",
            NativeStatus.LibraryNotLoaded => "SysSuite.Native.dll 未加载（需先执行 CMake 构建）",
            NativeStatus.AbiMismatch => "ABI 版本不匹配，请重新构建原生模块",
            _ => "未知原生错误",
        };
    }

    /// <summary>把原生返回的整型错误码翻译为状态码。</summary>
    public static NativeStatus FromCode(int code)
    {
        return code switch
        {
            0 => NativeStatus.Ok,
            1 => NativeStatus.InvalidArgument,
            2 => NativeStatus.BufferTooSmall,
            3 => NativeStatus.NotImplemented,
            4 => NativeStatus.OutOfMemory,
            5 => NativeStatus.AccessDenied,
            6 => NativeStatus.IoError,
            7 => NativeStatus.Cancelled,
            8 => NativeStatus.InternalError,
            _ => NativeStatus.Unknown,
        };
    }
}
