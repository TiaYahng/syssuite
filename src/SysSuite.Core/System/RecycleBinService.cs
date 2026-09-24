using System.ComponentModel;
using System.Runtime.InteropServices;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed record RecycleBinInfo(string RootPath, long SizeBytes, long ItemCount)
{
    public bool IsEmpty => ItemCount <= 0;
}

/// <summary>
/// 回收站的查询与清空。
///
/// 这里刻意**不做成清理规则**：回收站不是普通目录，逐文件删除是错的，原因有三：
/// 1. 站内是 <c>$I&lt;name&gt;</c>（元数据）与 <c>$R&lt;name&gt;</c>（内容）成对存放的，
///    只删其中一半会留下无法清理的孤儿，还让"回收站大小"显示错乱；
/// 2. <c>$Recycle.Bin</c> 是 <see cref="ProtectedPaths"/> 的段级红线，规则命中也删不掉；
/// 3. 每个用户各有一个 SID 子目录，清别人的需要提权，逐文件枚举会把权限错误刷满日志。
///
/// 正确入口是 shell 提供的 <c>SHQueryRecycleBin</c> / <c>SHEmptyRecycleBin</c>，
/// 它由资源管理器自己维护一致性，也天然处理多用户与权限。
/// </summary>
public static class RecycleBinService
{
    /// <summary>清理列表中回收站条目的类别名，执行器据此分流到清空 API。</summary>
    public const string Category = "回收站";

    // SHERB_* —— 见 shlobj_core.h
    private const int SherbNoConfirmation = 0x00000001;
    private const int SherbNoProgressUi = 0x00000002;
    private const int SherbNoSound = 0x00000004;

    // HRESULT 的高位是失败位，超出 int 正数范围，必须显式 unchecked 转换
    private const int ErrorFileNotFound = unchecked((int)0x80070002);
    private const int ErrorAccessDenied = unchecked((int)0x80070005);

    /// <summary>
    /// 查询回收站占用。<paramref name="rootPath"/> 形如 <c>C:\</c>；传 null 表示汇总所有盘。
    /// 查询失败（卷未就绪、权限不足）不抛异常，返回零值条目 —— 扫描阶段单点失败不该中断整轮。
    /// </summary>
    public static RecycleBinInfo Query(string? rootPath)
    {
        var info = new ShQueryRbInfo { CbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        var hresult = SHQueryRecycleBin(rootPath, ref info);
        return hresult < 0
            ? new RecycleBinInfo(rootPath ?? string.Empty, 0, 0)
            : new RecycleBinInfo(rootPath ?? string.Empty, info.Size, info.ItemCount);
    }

    /// <summary>
    /// 清空回收站。
    ///
    /// 传 <see cref="SherbNoConfirmation"/> 是**有意为之**：确认框由 UI 层（ViewModel 的
    /// Interactions）负责，这里再弹一次系统对话框就变成双重确认，且那个模态框不在我们的
    /// 主题与取消流程控制之内，长任务中会卡住整个清理批次。
    /// </summary>
    public static Result<RecycleBinInfo> Empty(string? rootPath)
    {
        var before = Query(rootPath);
        var hresult = SHEmptyRecycleBin(
            IntPtr.Zero,
            rootPath,
            SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);

        if (hresult == ErrorFileNotFound || (hresult == 0 && before.IsEmpty))
        {
            return new Result<RecycleBinInfo>(ErrorType.None, string.Empty, before);
        }

        if (hresult < 0)
        {
            var message = hresult == ErrorAccessDenied
                ? $"清空回收站被拒绝（{rootPath}）：需要管理员权限，或该卷的回收站正被占用。"
                : $"清空回收站失败（{rootPath}）：{DescribeHresult(hresult)}";

            return new Result<RecycleBinInfo>(
                hresult == ErrorAccessDenied ? ErrorType.AccessDenied : ErrorType.Internal,
                message);
        }

        return new Result<RecycleBinInfo>(ErrorType.None, string.Empty, before);
    }

    private static string DescribeHresult(int hresult)
    {
        // HRESULT 的低 16 位即 Win32 错误码时，交给 Win32Exception 给出本地化描述
        var code = hresult & 0xFFFF;
        return code == 0
            ? $"0x{hresult:X8}"
            : $"{new Win32Exception(code).Message} (0x{hresult:X8})";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int CbSize;
        public long Size;
        public long ItemCount;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, int flags);
}
