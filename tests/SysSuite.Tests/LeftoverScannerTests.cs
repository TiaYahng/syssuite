using Microsoft.Win32;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;
using Xunit;

namespace SysSuite.Tests;

public class LeftoverScannerTests
{
    [Fact]
    public async Task LeftoverScannerFindsAndDeletesSimilarDataAndEmptyInstallDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        var installDirectory = Path.Combine(root, "install", "Test Application");
        var localRoot = Path.Combine(root, "local");
        var commonRoot = Path.Combine(root, "common");
        var localDirectory = Path.Combine(localRoot, "Test Application Data");
        Directory.CreateDirectory(installDirectory);
        Directory.CreateDirectory(localDirectory);
        try
        {
            var app = AppRecord.Create("test", "Test Application", AppSource.Registry, installDir: installDirectory);
            var scanner = new LeftoverScanner(localRoot, commonRoot);
            var result = await scanner.ScanAsync(app);
            Assert.True(result.IsSuccess, result.Message);
            Assert.Contains(result.Value!, item => item.Kind == LeftoverKind.InstallDirectory && item.Path == installDirectory);
            Assert.Contains(result.Value!, item => item.Kind == LeftoverKind.AppDataDirectory && item.Path == localDirectory);

            var deleteResult = await scanner.DeleteAsync(result.Value!);
            Assert.True(deleteResult.IsSuccess, deleteResult.Message);
            Assert.Equal(2, deleteResult.Value);
            Assert.False(Directory.Exists(installDirectory));
            Assert.False(Directory.Exists(localDirectory));
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
    public async Task LeftoverScannerDeletesRegistryKeyAfterUninstall()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var keyPath = @"Software\SysSuiteTests\{D6C9ED6B-1C36-4A0B-BBE4-5C2D259286B0}";
        using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
        {
            key.SetValue("DisplayName", "Leftover Registry Test");
        }

        try
        {
            var app = AppRecord.Create(keyPath, "Leftover Registry Test", AppSource.Registry, keyPath: $@"HKEY_CURRENT_USER\{keyPath}");
            var scanner = new LeftoverScanner(root, root);
            var scanResult = await scanner.ScanAsync(app);
            Assert.True(scanResult.IsSuccess, scanResult.Message);
            Assert.Contains(scanResult.Value!, item => item.Kind == LeftoverKind.RegistryKey && item.Path == $@"HKEY_CURRENT_USER\{keyPath}");

            var deleteResult = await scanner.DeleteAsync(scanResult.Value!);
            Assert.True(deleteResult.IsSuccess, deleteResult.Message);
            Assert.True(deleteResult.Value >= 1);
            Assert.Null(Registry.CurrentUser.OpenSubKey(keyPath));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\SysSuiteTests", false);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
