using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SysSuite.Core.System;

internal sealed record ProcessOutcome(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    /// <summary>DISM 在非提权下的退出码，<c>ERROR_ELEVATION_REQUIRED</c>。</summary>
    internal const int ElevationRequired = 740;

    internal bool RequiresElevation => ExitCode == ElevationRequired;

    internal string AllOutput =>
        string.IsNullOrEmpty(StandardError) ? StandardOutput : StandardOutput + Environment.NewLine + StandardError;
}

/// <summary>
/// 启动控制台程序并收齐输出。
/// </summary>
/// <remarks>
/// 两个必须守住的点：
///
/// 1. **绝不闪控制台窗口**：<c>CreateNoWindow = true</c> 且 <c>UseShellExecute = false</c>。
///    DISM 这类程序动辄跑几分钟，弹一个黑窗出来既难看又容易被当成卡死。
/// 2. **按 OEM 代码页解码**：DISM 用 <c>WriteFile</c> 按当前控制台代码页（zh-CN 为 936）写出，
///    按 UTF-8 解会得到满屏乱码 —— 而这正是"日志完整"这条验收项要防的事。
///    .NET 8 默认不注册非 UTF 代码页，需要 <c>System.Text.Encoding.CodePages</c>。
/// </remarks>
internal static class ExternalProcessRunner
{
    private static readonly Encoding ConsoleEncoding = ResolveConsoleEncoding();

    public static async Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = ConsoleEncoding,
            StandardErrorEncoding = ConsoleEncoding,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new ProcessOutcome(-1, string.Empty, $"无法启动 {fileName}。", false);
        }

        // 先起读取再等退出：DISM 的输出量会填满管道缓冲，顺序反了就是死锁
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
        }

        var output = await SafeAwait(stdout).ConfigureAwait(false);
        var error = await SafeAwait(stderr).ConfigureAwait(false);
        var exitCode = timedOut ? -1 : SafeExitCode(process);

        return new ProcessOutcome(
            exitCode,
            output,
            timedOut ? $"操作超时（{timeout.TotalMinutes:F0} 分钟），已终止 {fileName}。" + Environment.NewLine + error : error,
            timedOut);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 已经自己退出了
        }
        catch (global::System.ComponentModel.Win32Exception)
        {
            // 权限不足杀不掉：不抛出去，让上层按超时处理
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static async Task<string> SafeAwait(Task<string> read)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static Encoding ResolveConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // 拿不到 OEM 代码页时退回 UTF-8：日志可能有乱码，但不该因此让功能不可用
            return Encoding.UTF8;
        }
    }
}
