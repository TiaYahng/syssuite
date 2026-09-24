using System.Globalization;
using System.Text;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// T3.5 的 compact 部分，以及完整日志落盘。
/// </summary>
/// <remarks>
/// **compact 只接受非系统保护路径**，这是本功能最重要的安全边界。
/// 计划里写的是"排除系统临界文件清单"，但那种清单必然过期 —— 与其维护一份
/// 迟早漏掉新系统文件的名单，不如直接复用 G7 的 <see cref="ProtectedPaths"/> 白名单：
/// 它已经覆盖了 Windows / Program Files / 用户主目录等全部临界区域，且是 fail-closed 的。
/// 代价是 compact 只能作用在用户自己的数据目录上 —— 这个代价可以接受。
/// </remarks>
public sealed partial class SystemSlimmingService
{
    /// <summary>返回给 UI 的日志行数上限；超出的部分只留在磁盘上的完整日志里。</summary>
    internal const int InlineLogLineCap = 400;

    /// <summary>仓库约定：文本文件一律 UTF-8 **无 BOM**。<c>Encoding.UTF8</c> 会带 BOM。</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<Result<SystemSlimmingResult>> CompactAsync(
        string directory,
        bool executableOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.Disabled,
                "系统瘦身是实验性功能，需在设置页同时打开「实验性功能」与「系统瘦身」。",
                0,
                []));
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.Rejected,
                "目标目录不存在。",
                0,
                []));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.Rejected,
                "目标目录路径无法解析。",
                0,
                []));
        }

        if (ProtectedPaths.IsProtected(fullPath))
        {
            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.Rejected,
                $"拒绝在系统保护路径上压缩（{ProtectedPaths.DescribeMatch(fullPath)}）。"
                    + "compact 会改变文件属性，落在系统目录上可能导致系统无法启动或更新失败。",
                0,
                []));
        }

        var arguments = new List<string>(4) { "/c" };
        if (executableOnly)
        {
            arguments.Add("/exe");
        }

        arguments.Add("/s:" + fullPath);
        arguments.Add("/i");   // 单个文件失败继续，不中断整批

        var outcome = await ExternalProcessRunner.RunAsync(
            CompactExecutable,
            arguments,
            CompactTimeout,
            cancellationToken).ConfigureAwait(false);

        List<string> lines = [.. outcome.AllOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        var (kept, logPath) = await PersistLogAsync("compact", lines, cancellationToken).ConfigureAwait(false);
        var suffix = logPath is null ? string.Empty : $" 完整日志：{logPath}";

        if (outcome.ExitCode == 0)
        {
            var freed = ParseCompactFreedBytes(lines);
            var freedNote = freed > 0 ? $" 已压缩约 {FormatBytes(freed)}。" : string.Empty;
            var scopeNote = executableOnly ? "（仅可执行文件）" : string.Empty;

            return new Result<SystemSlimmingResult>(ErrorType.None, string.Empty, new SystemSlimmingResult(
                SlimmingOutcome.Success,
                $"已对 {fullPath} 执行压缩{scopeNote}。{freedNote}{suffix}",
                freed,
                kept));
        }

        return new Result<SystemSlimmingResult>(ErrorType.Internal, string.Empty, new SystemSlimmingResult(
            SlimmingOutcome.Failed,
            $"压缩失败（退出码 {outcome.ExitCode}）。{suffix}",
            0,
            kept));
    }

    /// <summary>
    /// 把完整日志写到磁盘，返回（回传给 UI 的节选，日志路径）。
    /// 磁盘写入失败不算功能失败 —— 日志是附属品，不该拖垮主操作。
    /// </summary>
    private static async Task<(IReadOnlyList<string> Kept, string? LogPath)> PersistLogAsync(
        string operation,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        string? logPath = null;
        if (lines.Count > 0)
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SysSuite",
                    "logs");
                Directory.CreateDirectory(directory);
                logPath = Path.Combine(
                    directory,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"slimming-{operation}-{DateTime.Now:yyyyMMdd-HHmmss}.log"));
                await File.WriteAllLinesAsync(logPath, lines, Utf8NoBom, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logPath = null;
            }
        }

        if (lines.Count <= InlineLogLineCap)
        {
            return (lines, logPath);
        }

        // 只保留头尾：开头是命令上下文，结尾是结论与错误，中间成百上千行是逐文件明细
        var head = lines.Take(InlineLogLineCap / 2);
        var tail = lines.Skip(lines.Count - InlineLogLineCap / 2);
        var kept = new List<string>(InlineLogLineCap + 1);
        kept.AddRange(head);
        kept.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"…… 中间省略 {lines.Count - InlineLogLineCap} 行，完整内容见日志文件 ……"));
        kept.AddRange(tail);
        return (kept, logPath);
    }

    /// <summary>
    /// 从 compact 输出里算出真正的节省量。
    /// </summary>
    /// <remarks>
    /// 优先读**汇总行**（"总共 1,120,000 字节的数据保存在 40,960 字节中"），它由 compact 自己
    /// 累加，比逐行相加更可信；汇总行认不出来时才退回逐文件累加。
    ///
    /// 逐文件行的真实格式是 <c>sample.exe   560000 :     20480 = 27.3 到 1 [OK]</c> ——
    /// 即"文件名 原始 : 压缩后 = 压缩率"，等号在数字对**之后**。
    /// 解析不出来就返回 0，由调用方把它当成"未报告"而不是"没省下空间"。
    /// </remarks>
    internal static long ParseCompactFreedBytes(List<string> lines)
    {
        long perFile = 0;
        foreach (var line in lines)
        {
            var match = global::System.Text.RegularExpressions.Regex.Match(line, @"([0-9]+)\s*:\s*([0-9]+)\s*=");
            if (match.Success
                && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var before)
                && long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var after)
                && before > after)
            {
                perFile += before - after;
            }
        }

        foreach (var line in lines)
        {
            var summary = global::System.Text.RegularExpressions.Regex.Match(
                line,
                @"([0-9][0-9,]*)\s*(?:字节的数据保存在|bytes of data saved in)\s*([0-9][0-9,]*)");

            if (summary.Success
                && TryParseGroupedNumber(summary.Groups[1].Value, out var total)
                && TryParseGroupedNumber(summary.Groups[2].Value, out var stored)
                && total > stored)
            {
                return total - stored;
            }
        }

        return perFile;
    }

    /// <summary>compact 的汇总量带千分位（1,120,000），必须先剥掉逗号。</summary>
    private static bool TryParseGroupedNumber(string text, out long value)
        => long.TryParse(
            text.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
}
