using System.Globalization;

namespace SysSuite.Core.System;

/// <summary>
/// T3.5 的 DISM **输出解析**部分（从 <c>.Dism.cs</c> 拆出，按"执行 / 解析"分职责）。
/// </summary>
/// <remarks>
/// 解析原则：DISM 的输出是**本地化文本**，中文与英文的标签完全不同，按标签匹配天然是脆的。
/// 所以这里的约定是——解析失败就如实报"未知"（<c>-1</c>），**永远不猜一个数字**；
/// 同时把原始输出一字不改地放进日志，用户与支持人员始终能看到权威内容。
/// 拿猜出来的体积去说服用户"能省 3GB"比不做解析更糟。
///
/// 另一条硬约束：**这些标签与正则必须由真机输出驱动，不能照直觉写**。
/// 真机上已经栽过三次：漏一个"的"导致整条解析静默落空、用 <c>\b</c> 匹配不到 <c>0 bytes</c>、
/// 以及只看标签不看值会把「推荐清理 : 否」读成建议清理。改动前先重抓一份真输出。
/// </remarks>
public sealed partial class SystemSlimmingService
{
    /// <summary>组件存储分析报告的标签。中英各列一份，两者都匹配不到就判为解析失败。</summary>
    /// <remarks>
    /// 这些标签是**真机捕获**的，不是照直觉写的（Win11 26200 zh-CN）：
    /// <code>
    ///     已与 Windows 共享 : 8.56 GB
    ///     备份和已禁用的功能 : 10.04 GB
    ///     缓存和临时数据 :  0 bytes
    ///     可回收的程序包数 : 5
    ///     推荐使用组件存储清理 : 是
    /// </code>
    /// 两个必须记住的坑：① 是"备份和已禁用**的**功能"，少一个"的"整条解析就静默落空
    /// （这曾经让体积长期显示为"未知"）；② 体积为 0 时 DISM 在中文输出里写成英文单位
    /// <c>0 bytes</c>，它并不翻译 0。
    /// </remarks>
    private static readonly (string Field, string[] Labels)[] AnalyzeLabels =
    [
        ("backups", ["备份和已禁用的功能", "备份和已禁用功能", "Backups and Disabled Features"]),
        ("cache", ["缓存和临时数据", "Cache and Temporary Data"]),
        ("packages", ["可回收的程序包数", "Reclaimable Packages"]),
    ];

    /// <summary>推荐清理的措辞。真机中文形态是「推荐使用组件存储清理 : 是」。</summary>
    private static readonly string[] CleanupRecommendedMarkers =
        ["建议清理", "强烈建议清理", "推荐使用组件存储清理", "recommended", "should be cleaned"];

    /// <summary>值域里的否定词：「推荐使用组件存储清理 : <b>否</b>」不是建议清理。</summary>
    private static readonly string[] DecliningValues =
        ["否", "不需要", "不建议", "不推荐", "无需", "no", "false", "not recommended"];

    /// <summary>值域里的肯定词。</summary>
    private static readonly string[] AffirmingValues = ["是", "yes", "true"];

    /// <summary>从 DISM 分析输出里取"可回收字节数"与"可回收程序包数"。取不到一律返回未知而不是 0。</summary>
    internal static (long ReclaimableBytes, int? ReclaimablePackages) ParseAnalyzeOutput(List<string> lines)
    {
        long backups = -1;
        long cache = -1;
        int? packages = null;

        foreach (var line in lines)
        {
            foreach (var (field, labels) in AnalyzeLabels)
            {
                if (!labels.Any(label => line.Contains(label, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                switch (field)
                {
                    case "backups":
                        backups = ParseSize(line) ?? backups;
                        break;
                    case "cache":
                        cache = ParseSize(line) ?? cache;
                        break;
                    case "packages":
                        packages = ParseCount(line) ?? packages;
                        break;
                    default:
                        break;
                }
            }
        }

        var known = (backups > 0 ? backups : 0) + (cache > 0 ? cache : 0);
        return known > 0 ? (known, packages) : (-1, packages);
    }

    /// <summary>判断"是否建议清理"。必须看值域，不能只看标签。</summary>
    /// <remarks>
    /// 真机输出是「推荐使用组件存储清理 : 是」这种「标签 : 值」形态，所以只要整行包含"推荐使用
    /// 组件存储清理"就返回 true 会把「… : 否」也读成建议清理 —— 反向误报比漏报更糟（会推着用户
    /// 去做一次毫无收益、且不可回退的 /ResetBase）。因此有分隔符时一律以值域为准，
    /// 只有形如 "It is recommended that…" 的整句措辞才退回按措辞判定。
    /// </remarks>
    internal static bool IsCleanupRecommended(string line)
    {
        var value = ValueAfterSeparator(line);
        if (value.Length > 0)
        {
            if (DecliningValues.Any(marker => value.StartsWith(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (AffirmingValues.Any(marker => value.StartsWith(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return CleanupRecommendedMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>取「标签 : 值」里的值域；没有分隔符则返回空串。中英分隔符都认。</summary>
    private static string ValueAfterSeparator(string line)
    {
        var index = line.LastIndexOfAny([':', '：']);
        return index < 0 ? string.Empty : line[(index + 1)..].Trim();
    }

    /// <summary>取一行里最后的 <c>数字 + 单位</c> 组合。单位是拉丁文，即使在中文输出里也不翻译。</summary>
    /// <remarks>
    /// 用 <c>(?![A-Za-z])</c> 收尾而不是 <c>\b</c>：真机中文输出里体积为 0 时 DISM 写的是
    /// <c>0 bytes</c>（英文单位，不翻译），而 <c>B\b</c> 匹配不到 <c>bytes</c> —— B 后面还是字母。
    /// </remarks>
    internal static long? ParseSize(string line)
    {
        var match = global::System.Text.RegularExpressions.Regex.Match(
            line,
            @"([0-9]+(?:[.,][0-9]+)?)\s*(TB|GB|MB|KB|bytes|byte|B)(?![A-Za-z])",
            global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success && TryParseNumber(match.Groups[1].Value, out var value)
            ? (long)(value * UnitFactor(match.Groups[2].Value))
            : null;
    }

    /// <summary>取行尾整数。只对已按标签筛过的行使用 —— 日期行（<c>…10:22:31</c>）会被读成 31。</summary>
    internal static int? ParseCount(string line)
    {
        var match = global::System.Text.RegularExpressions.Regex.Match(line, @"([0-9]+)\s*$");
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;
    }

    /// <summary>把外部进程的输出切成行。DISM 与 compact 的输出都用它，两边行尾都可能是 CRLF。</summary>
    private static List<string> SplitLines(string text)
        => [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool TryParseNumber(string text, out double value)
        => double.TryParse(
            text.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    /// <summary>DISM 数字用 <c>.</c> 作小数点；用 1024 进制折算（DISM 的 GB 是二进制 GB）。</summary>
    private static double UnitFactor(string unit) => unit.ToUpperInvariant() switch
    {
        "TB" => 1024d * 1024 * 1024 * 1024,
        "GB" => 1024d * 1024 * 1024,
        "MB" => 1024d * 1024,
        "KB" => 1024d,
        _ => 1d,
    };
}
