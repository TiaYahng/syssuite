using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;

namespace SysSuite.Tests;

public class DiskInspectionServiceTests
{
    [Fact]
    public async Task DiskInspectionFindsDuplicatesAndEmptyFoldersWhileSkippingApplications()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            var dataDirectory = Path.Combine(root, "Data");
            var applicationDirectory = Path.Combine(root, "MyApplication");
            var emptyDirectory = Path.Combine(root, "Empty");
            var tempDirectory = Path.Combine(root, "Temp");
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(applicationDirectory);
            Directory.CreateDirectory(emptyDirectory);
            Directory.CreateDirectory(tempDirectory);

            var oldest = Path.Combine(dataDirectory, "old.dat");
            var newest = Path.Combine(dataDirectory, "new.dat");
            var applicationCopy = Path.Combine(applicationDirectory, "copy.dat");
            var payload = new byte[1024 * 1024 + 16];
            Random.Shared.NextBytes(payload);
            await File.WriteAllBytesAsync(oldest, payload);
            await File.WriteAllBytesAsync(newest, payload);
            await File.WriteAllBytesAsync(applicationCopy, payload);
            await File.WriteAllTextAsync(Path.Combine(applicationDirectory, "MyApplication.exe"), "MZ");
            var tempFile = Path.Combine(tempDirectory, "stale.log");
            await File.WriteAllTextAsync(tempFile, "stale");
            File.SetLastWriteTimeUtc(tempFile, DateTime.UtcNow.AddDays(-8));
            File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddDays(-1));

            var progressReports = new List<DiskScanProgress>();
            var progress = new Progress<DiskScanProgress>(progressReports.Add);
            var service = new DiskInspectionService();
            var result = await service.ScanAsync([root], progress);
            await Task.Delay(20);
            Assert.True(result.IsSuccess, result.Message);
            var report = result.Value!;
            var duplicate = Assert.Single(report.Items.Where(item => item.Category == "重复文件"));
            Assert.Equal(newest, duplicate.Path);
            Assert.DoesNotContain(report.Items, item => item.Path.StartsWith(applicationDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(report.Items, item => item.Category == "空文件夹" && item.Path == emptyDirectory);
            Assert.Contains(report.Items, item => item.Category == "临时文件" && item.Path == tempFile);
            Assert.Contains(report.Groups, group => group.Category == "临时文件" && group.Title == "stale.log");
            var lastProgress = progressReports[^1];
            Assert.Equal("扫描完成", lastProgress.Phase);
            Assert.Equal(1d, lastProgress.Progress!.Value, 5);

            var cleanResult = await service.CleanAsync(report, report.Items);
            Assert.True(cleanResult.IsSuccess, cleanResult.Message);
            Assert.Equal(3, cleanResult.Value!.DeletedCount);
            Assert.True(File.Exists(oldest));
            Assert.False(File.Exists(newest));
            Assert.False(File.Exists(tempFile));
            Assert.True(File.Exists(applicationCopy));
            Assert.False(Directory.Exists(emptyDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task DiskInspectionRejectsPathsOutsideScanReport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        var outsideRoot = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outsideRoot);
            var outsidePath = Path.Combine(outsideRoot, "outside.txt");
            await File.WriteAllTextAsync(outsidePath, "keep");
            var service = new DiskInspectionService();
            var scanResult = await service.ScanAsync([root]);
            Assert.True(scanResult.IsSuccess, scanResult.Message);
            var report = scanResult.Value!;
            var result = await service.CleanAsync(report, [
                new CleanItem(outsidePath, "空文件夹", 0, CleanRisk.Safe, "越界路径")
            ]);

            Assert.False(result.IsSuccess);
            Assert.Equal(SysSuite.Core.Abstractions.ErrorType.InvalidInput, result.Error);
            Assert.True(Directory.Exists(outsideRoot));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outsideRoot, true);
        }
    }
    [Fact]
    public async Task DiskInspectionSkipsInstalledApplicationDirectoryWithManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            var applicationDirectory = Path.Combine(root, "MyInstalledApp");
            var dataDirectory = Path.Combine(root, "Data");
            Directory.CreateDirectory(applicationDirectory);
            Directory.CreateDirectory(dataDirectory);
            await File.WriteAllTextAsync(Path.Combine(applicationDirectory, "MyInstalledApp.manifest"), "app");
            var dataFile = Path.Combine(dataDirectory, "data.dat");
            await File.WriteAllTextAsync(dataFile, "data");

            var service = new DiskInspectionService();
            var result = await service.ScanAsync([root]);
            Assert.True(result.IsSuccess, result.Message);
            Assert.DoesNotContain(result.Value!.Items, item => item.Path.StartsWith(applicationDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Value!.Items, item => item.Path == dataFile);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DiskInspectionSkipsApplicationRelatedDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        try
        {
            var mastercamDirectory = Path.Combine(root, "Mastercam");
            var myMastercamDirectory = Path.Combine(root, "My Mastercam 2024");
            var sharedDirectory = Path.Combine(root, "Shared My Mastercam 2024");
            Directory.CreateDirectory(mastercamDirectory);
            Directory.CreateDirectory(myMastercamDirectory);
            Directory.CreateDirectory(sharedDirectory);
            await File.WriteAllTextAsync(Path.Combine(mastercamDirectory, "Mastercam.exe"), "MZ");
            await File.WriteAllTextAsync(Path.Combine(myMastercamDirectory, "library.dat"), "library");
            await File.WriteAllTextAsync(Path.Combine(sharedDirectory, "shared.dat"), "shared");

            var service = new DiskInspectionService();
            var result = await service.ScanAsync([root]);
            Assert.True(result.IsSuccess, result.Message);
            Assert.DoesNotContain(result.Value!.Items, item =>
                item.Path.StartsWith(mastercamDirectory, StringComparison.OrdinalIgnoreCase)
                || item.Path.StartsWith(myMastercamDirectory, StringComparison.OrdinalIgnoreCase)
                || item.Path.StartsWith(sharedDirectory, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
