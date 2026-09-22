using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService
{
    private const long LargeFileThreshold = 512L * 1024 * 1024;
    private const int LargeFileTopN = 200;

    private static void AddLargeFileItems(
        IEnumerable<FileInfo> files,
        List<CleanItem> items,
        CancellationToken cancellationToken)
    {
        var existing = items.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files
            .Where(file => file.Length >= LargeFileThreshold)
            .OrderByDescending(file => file.Length)
            .Take(LargeFileTopN))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!existing.Add(file.FullName) || file.Directory is not null && IsApplicationDirectory(file.Directory, out _))
            {
                continue;
            }

            items.Add(new CleanItem(
                file.FullName,
                "大文件",
                file.Length,
                CleanRisk.Caution,
                $"文件大小 {FormatLargeFileSize(file.Length)}，删除前请确认用途。"));
        }
    }

    private static string FormatLargeFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:N1} GB",
            _ => $"{bytes / 1024d / 1024d:N1} MB"
        };
    }
}