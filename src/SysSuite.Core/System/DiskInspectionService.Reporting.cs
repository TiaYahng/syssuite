using System.Globalization;
using System.IO;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService
{
    public static DiskInspectionReport RebuildReport(DiskInspectionReport report)
    {
        return CreateReport(
            report.Roots.Select(root => new DirectoryInfo(root)),
            report.Items);
    }

    private static DiskInspectionReport CreateReport(
        IEnumerable<DirectoryInfo> roots,
        IEnumerable<CleanItem> items)
    {
        var orderedItems = items
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var groupData = orderedItems
            .GroupBy(item => BuildGroupKey(item.Category, item.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => new DiskGroup(
                group.Key,
                Path.GetFileName(group.First().Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                group.First().Category,
                group.Sum(item => item.SizeBytes),
                group.Count(),
                string.Create(CultureInfo.InvariantCulture, $"{group.Count()} 项 · {FormatBytes(group.Sum(item => item.SizeBytes))}")))
            .OrderBy(group => group.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var groups = groupData.ToDictionary(group => group.Key, StringComparer.OrdinalIgnoreCase);
        var cleanItems = orderedItems
            .Select(item => new DiskCleanItem(
                item.Path,
                item.Category,
                item.SizeBytes,
                item.Risk,
                item.Importance,
                groups[BuildGroupKey(item.Category, item.Path)]))
            .ToArray();
        return new DiskInspectionReport(
            roots.Select(root => root.FullName).ToArray(),
            cleanItems,
            groupData);
    }

    private static string BuildGroupKey(string category, string path)
    {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var title = category == "大文件"
            ? Path.GetDirectoryName(normalized) ?? normalized
            : Path.GetFileName(normalized);
        return $"{category}\u0001{title}";
    }

    private static string FormatBytes(long value)
    {
        return value switch
        {
            >= 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d / 1024d:N1} GB"),
            >= 1024L * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d:N1} MB"),
            >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d:N1} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{value:N0} B")
        };
    }
}
