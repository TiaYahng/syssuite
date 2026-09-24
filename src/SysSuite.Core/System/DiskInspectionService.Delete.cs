using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 清理的删除原语（从 <c>.Clean.cs</c> 拆出，只为守住 300 行门禁）。
///
/// 拆分边界是"编排 vs 原语"：<c>.Clean.cs</c> 负责批次、备份、历史与撤销的流程，
/// 本文件只负责"把一项删掉"这件事本身。
/// </summary>
public sealed partial class DiskInspectionService
{
    private static bool EmptyRecycleBin(CleanItem item, List<string> errors, ref long freedBytes)
    {
        var volume = Path.GetPathRoot(item.Path);
        if (string.IsNullOrEmpty(volume))
        {
            return AddError(errors, item.Path, "无法从回收站条目解析盘符。");
        }

        var result = RecycleBinService.Empty(volume);
        if (!result.IsSuccess)
        {
            return AddError(errors, item.Path, result.Message);
        }

        var info = result.Value!;
        if (info.IsEmpty)
        {
            return AddError(errors, item.Path, "回收站已经是空的。");
        }

        freedBytes += info.SizeBytes;
        return true;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool ValidateReportItems(DiskInspectionReport report, IReadOnlyList<CleanItem> items)
    {
        var reportPaths = report.Items
            .Select(item => GetFullPath(item.Path))
            .Where(path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items.All(item => GetFullPath(item.Path) is { } path && reportPaths.Contains(path));
    }

    private static bool DeleteInspectionItem(CleanItem item, List<string> errors, ref long freedBytes)
    {
        // 回收站不是文件：必须**先于**保护路径校验分流，因为它的路径天然位于段级红线内，
        // 走文件删除分支必然被拒；真正的一致性由 shell 的清空 API 保证。
        if (string.Equals(item.Category, RecycleBinService.Category, StringComparison.Ordinal))
        {
            return EmptyRecycleBin(item, errors, ref freedBytes);
        }

        // G7：删除前的最后一道硬拒绝，规则引擎即使误命中也不允许穿透
        if (ProtectedPaths.IsProtected(item.Path))
        {
            return AddError(errors, item.Path, $"位于系统保护路径（{ProtectedPaths.DescribeMatch(item.Path)}），已拒绝删除。");
        }

        try
        {
            if (item.Category is "重复文件" or "临时文件" or "大文件")
            {
                return DeleteDuplicateFile(item, ref freedBytes);
            }

            return item.Category == "空文件夹" && DeleteEmptyFolder(item, errors);
        }
        catch (IOException exception)
        {
            return AddError(errors, item.Path, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return AddError(errors, item.Path, exception.Message);
        }
    }

    private static bool DeleteDuplicateFile(CleanItem item, ref long freedBytes)
    {
        var file = new FileInfo(item.Path);
        if (!file.Exists)
        {
            return true;
        }

        var size = file.Length;
        file.Delete();
        freedBytes += size;
        return true;
    }

    private static bool DeleteEmptyFolder(CleanItem item, List<string> errors)
    {
        var directory = new DirectoryInfo(item.Path);
        if (!directory.Exists || IsApplicationDirectory(directory, out _))
        {
            return AddError(errors, item.Path, "目录已变化或属于应用目录。");
        }

        if (!IsEmptyDirectory(directory))
        {
            return AddError(errors, item.Path, "目录已不再为空。");
        }

        directory.Delete(false);
        return true;
    }

    private static bool AddError(List<string> errors, string path, string message)
    {
        if (errors.Count < MaximumErrorMessages)
        {
            errors.Add($"{path}: {message}");
        }

        return false;
    }
}
