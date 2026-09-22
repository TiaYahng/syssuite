using System.IO;
using System.Security.Cryptography;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService
{
    private static int DuplicateScanParallelism =>
        Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private static global::System.Linq.ParallelQuery<CleanItem> FindDuplicateItems(
        IReadOnlyList<FileInfo> files,
        ScanTracker tracker,
        CancellationToken cancellationToken)
    {
        var partialGroups = files
            .GroupBy(file => file.Length)
            .Where(group => group.Count() > 1)
            .AsParallel()
            .WithDegreeOfParallelism(DuplicateScanParallelism)
            .WithCancellation(cancellationToken)
            .SelectMany(sizeGroup => FindPartialCandidates(sizeGroup, tracker, cancellationToken))
            .GroupBy(candidate => candidate.PartialHash, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        return partialGroups
            .AsParallel()
            .WithDegreeOfParallelism(DuplicateScanParallelism)
            .WithCancellation(cancellationToken)
            .SelectMany(partialGroup => FindFullCandidates(partialGroup, tracker, cancellationToken))
            .GroupBy(candidate => (candidate.Length, candidate.Hash))
            .Where(group => group.Count() > 1)
            .SelectMany(BuildCleanItems);
    }

    private static IEnumerable<DuplicateCandidate> FindPartialCandidates(
        IGrouping<long, FileInfo> files,
        ScanTracker tracker,
        CancellationToken cancellationToken)
    {
        return files
            .Select(file =>
            {
                tracker.ReportBytes(file.FullName, Math.Min(file.Length, PartialHashLength));
                return new DuplicateCandidate(
                file.Length,
                ComputeHash(file.FullName, PartialHashLength, cancellationToken) ?? string.Empty,
                string.Empty,
                file);
            })
            .Where(candidate => candidate.PartialHash.Length > 0);
    }

    private static IEnumerable<DuplicateCandidate> FindFullCandidates(
        IGrouping<string, DuplicateCandidate> partialGroup,
        ScanTracker tracker,
        CancellationToken cancellationToken)
    {
        return partialGroup
            .Select(candidate =>
            {
                tracker.ReportBytes(candidate.File.FullName, candidate.File.Length);
                return candidate with
                {
                    Hash = ComputeHash(candidate.File.FullName, long.MaxValue, cancellationToken) ?? string.Empty
                };
            })
            .Where(candidate => candidate.Hash.Length > 0);
    }

    private static IEnumerable<CleanItem> BuildCleanItems(
        IGrouping<(long Length, string Hash), DuplicateCandidate> group)
    {
        var files = group
            .Select(candidate => candidate.File)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var keepFile = files[0];
        return files.Skip(1)
            .Select(file => new CleanItem(
                file.FullName,
                "重复文件",
                file.Length,
                CleanRisk.Safe,
                string.Create(
                    global::System.Globalization.CultureInfo.InvariantCulture,
                    $"内容完全相同，保留 {keepFile.FullName}。")))
            .ToArray();
    }

    private static string? ComputeHash(string path, long maxLength, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hashAlgorithm = SHA256.Create();
            var remaining = Math.Min(stream.Length, maxLength);
            var buffer = new byte[128 * 1024];
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    break;
                }

                hashAlgorithm.TransformBlock(buffer, 0, read, null, 0);
                remaining -= read;
            }

            hashAlgorithm.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(hashAlgorithm.Hash!);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct DuplicateCandidate(
        long Length,
        string PartialHash,
        string Hash,
        FileInfo File);
}
