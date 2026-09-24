using System.Runtime.InteropServices;

namespace SysSuite.Interop;

/// <summary>
/// 与 <c>native_api.h</c> 的 <c>NativeFileEntry</c> 一一对齐。
/// 布局是 ABI 的一部分：只允许在尾部追加字段。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeFileEntry
{
    public ulong FileReference;
    public ulong ParentReference;
    public uint Attributes;
    public uint NameLength;

    /// <summary>
    /// 指向原生侧缓冲区的名字，**仅在本批次回调期间有效**（非 L'\0' 结尾，由
    /// <see cref="NameLength"/> 界定）。需要留存必须调用 <see cref="ReadName"/> 拷出来。
    /// </summary>
    public IntPtr Name;

    public bool IsDirectory => (Attributes & 0x10) != 0;

    public string ReadName()
        => Name == IntPtr.Zero || NameLength == 0
            ? string.Empty
            : Marshal.PtrToStringUni(Name, (int)NameLength) ?? string.Empty;
}

/// <summary>
/// MFT 批次回调。返回 true 继续，返回 false 中止（原生侧按取消处理）。
/// 用委托而不是 <c>Func&lt;ReadOnlySpan&lt;T&gt;, bool&gt;</c>：Span 不能作为泛型类型实参。
/// </summary>
public delegate bool NativeEntryBatchHandler(ReadOnlySpan<NativeFileEntry> entries);
