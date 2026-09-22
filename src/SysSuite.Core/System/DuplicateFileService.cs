using System.IO;
using System.Security.Cryptography;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed class DuplicateFileService : IDuplicateFileService
{
    private readonly string userTempRoot;
    private readonly string windowsTempRoot;

    public DuplicateFileService(string? userTempRoot = null, string? windowsTempRoot = null)
    {
        this.userTempRoot = userTempRoot ?? Path.GetTempPath();
        this.windowsTempRoot = windowsTempRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Temp");
    }

    public async Task<Result<IReadOnlyList<DuplicateGroup>>> ScanAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                return new Result<IReadOnlyList<DuplicateGroup>>(ErrorType.None, string.Empty, Scan(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                return new Result<IReadOnlyList<DuplicateGroup>>(ErrorType.Cancelled, "重复文件扫描已取消。");
            }
        }, cancellationToken);
    }

    private List<DuplicateGroup> Scan(CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        return new[] { userTempRoot, windowsTempRoot }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .SelectMany(root => EnumerateFiles(root, options))
            .Where(file => file.Length >= 1024 * 1024)
            .GroupBy(file => file.Length)
            .AsParallel()
            .WithCancellation(cancellationToken)
            .Select(group => new { Size = group.Key, Files = group.ToArray() })
            .Where(group => group.Files.Length > 1)
            .Select(group => new
            {
                group.Size,
                Files = group.Files
                    .AsParallel()
                    .WithCancellation(cancellationToken)
                    .Select(file => new
                    {
                        File = file,
                        Hash = ComputeHash(file.FullName, cancellationToken)
                    })
                    .Where(item => item.Hash is not null)
                    .ToArray()
            })
            .SelectMany(group => group.Files
                .GroupBy(item => item.Hash!)
                .Where(hashGroup => hashGroup.Count() > 1)
                .Select(hashGroup =>
                {
                    var files = hashGroup
                        .Select(item => new DuplicateFile(item.File.FullName, item.File.Length, item.File.LastWriteTimeUtc))
                        .OrderBy(file => file.LastWriteTimeUtc)
                        .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return new DuplicateGroup(hashGroup.Key, group.Size, files[0].Path, files);
                }))
            .OrderByDescending(group => group.SizeBytes * (group.Files.Count - 1))
            .ToList();
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string root, EnumerationOptions options)
    {
        var directory = new DirectoryInfo(root);
        return directory.Exists && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
            ? directory.EnumerateFiles("*", options)
            : Enumerable.Empty<FileInfo>();
    }

    private static string? ComputeHash(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream));
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
}
