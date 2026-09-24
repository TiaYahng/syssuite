using System.Globalization;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// T3.5 的体积测量：Windows.old 与传递优化缓存。
/// </summary>
/// <remarks>
/// 这两项都**不由本服务删除**，原因不同但都不该绕过既有设施：
///
/// * Windows.old 需要先取得所有权，而"取得所有权 + 递归删除"已经有一套实现
///   （<c>ForceDeleteService</c>，含确认句与备份）。在这里再写一份等于让同一件危险事
///   存在两个实现，两边迟早不一致。
/// * 传递优化缓存位于 <c>Windows</c> 树下，其清理已经由 <c>rules/clean-temp.json</c> 的
///   <c>delivery-optimization</c> 规则覆盖 —— 那条规则同时享受 <see cref="ProtectedPaths"/>
///   的精确例外，比另开一个删除路径更不容易出错。
///
/// 所以本文件只负责"让用户看见有多大"，动作交给既有通道。
/// </remarks>
public sealed partial class SystemSlimmingService
{
    internal const string WindowsOldTargetId = "windows-old";
    internal const string DeliveryOptimizationTargetId = "delivery-optimization";

    internal void AddWindowsOldTarget(List<SlimmingTarget> targets, List<string> log)
    {
        var root = Path.GetPathRoot(WindowsDirectory);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var path = Path.Combine(root, "Windows.old");
        if (!Directory.Exists(path))
        {
            targets.Add(new SlimmingTarget(
                WindowsOldTargetId,
                "旧版 Windows 安装（Windows.old）",
                0,
                "未检测到 Windows.old。",
                Actionable: false,
                Path: null));
            return;
        }

        var size = MeasureDirectory(path, out var capped, out var note);
        log.Add(string.Create(CultureInfo.InvariantCulture, $"{path} 体积约 {FormatBytes(size)} {note}"));

        var detail = capped
            ? "体积为达到统计上限后的近似值。删除需要先取得所有权，请使用「强制删除」流程（含确认句与备份）。"
            : "删除需要先取得所有权，请使用「强制删除」流程（含确认句与备份）。";

        targets.Add(new SlimmingTarget(
            WindowsOldTargetId,
            "旧版 Windows 安装（Windows.old）",
            size,
            detail,
            Actionable: false,
            Path: path));
    }

    internal void AddDeliveryOptimizationTarget(List<SlimmingTarget> targets, List<string> log)
    {
        var path = Path.Combine(WindowsDirectory, "SoftwareDistribution", "DeliveryOptimization");
        if (!Directory.Exists(path))
        {
            targets.Add(new SlimmingTarget(
                DeliveryOptimizationTargetId,
                "传递优化缓存",
                0,
                "未检测到传递优化缓存目录。",
                Actionable: false,
                Path: null));
            return;
        }

        var size = MeasureDirectory(path, out var capped, out var note);
        log.Add(string.Create(CultureInfo.InvariantCulture, $"{path} 体积约 {FormatBytes(size)} {note}"));

        const string Detail =
            "清理由「磁盘清理」页的『传递优化』分类负责（复用同一套保护路径例外），本页只做体积统计。";

        targets.Add(new SlimmingTarget(
            DeliveryOptimizationTargetId,
            "传递优化缓存",
            size,
            capped ? Detail + " 体积为达到统计上限后的近似值。" : Detail,
            Actionable: false,
            Path: path));
    }

    /// <summary>
    /// 迭代式目录体积统计。
    /// </summary>
    /// <remarks>
    /// 有上限是刻意为之：Windows.old 动辄二三十万文件，为了一句"有多大"让界面等上好几分钟
    /// 是不划算的。达到上限后返回近似值并置 <paramref name="capped"/>，由调用方如实标注 ——
    /// 把截断值当精确值报出去，用户看到的每个数字都不可信。
    /// </remarks>
    internal static long MeasureDirectory(string path, out bool capped, out string note)
    {
        long total = 0;
        var counted = 0;
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            if (counted >= MeasurementFileCap)
            {
                capped = true;
                note = string.Create(CultureInfo.InvariantCulture, $"（已达到 {MeasurementFileCap} 文件统计上限，实际可能更大）");
                return total;
            }

            var directory = pending.Pop();
            try
            {
                var info = new DirectoryInfo(directory);

                // 跳过 reparse point：junction 会造成自指递归，把体积算到天上去
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                    && !string.Equals(directory, path, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var child in info.EnumerateDirectories())
                {
                    pending.Push(child.FullName);
                }

                foreach (var file in info.EnumerateFiles())
                {
                    total += file.Length;
                    counted++;
                    if (counted >= MeasurementFileCap)
                    {
                        break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        capped = counted >= MeasurementFileCap;
        note = capped ? "（已达统计上限，实际可能更大）" : string.Empty;
        return total;
    }
}
