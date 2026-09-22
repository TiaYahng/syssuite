using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;
using Xunit;

namespace SysSuite.Tests;

public class IconCacheTests
{
    [Fact]
    public async Task IconCacheIgnoresUnavailableDisplayIconAndMsiCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var unavailableIcon = Path.Combine(root, "missing.exe");
            var app = AppRecord.Create(
                "uninstall-command",
                "Unavailable Icon Test",
                AppSource.Registry,
                uninstallString: "msiexec /x {11111111-2222-3333-4444-555555555555}",
                iconPath: $"{unavailableIcon},0");
            var service = new IconCacheService(Path.Combine(root, "cache"));
            var result = await service.GetIconPathAsync(app);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Null(result.Value);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task IconCacheResolvesSystemDllIconReference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var app = AppRecord.Create("clickonce", "ClickOnce App", AppSource.Registry, iconPath: "dfshim.dll,2");
            var service = new IconCacheService(Path.Combine(root, "cache"));
            var result = await service.GetIconPathAsync(app);

            Assert.True(result.IsSuccess, result.Message);
            Assert.NotNull(result.Value);
            Assert.True(File.Exists(result.Value));
            Assert.Equal(".png", Path.GetExtension(result.Value).ToLowerInvariant());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task IconCacheResolvesUnquotedExecutableWithIconIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host path is unavailable.");
            var app = AppRecord.Create("driver-icon", "Driver Package", AppSource.Registry, iconPath: $"{executable},0");
            var service = new IconCacheService(Path.Combine(root, "cache"));
            var result = await service.GetIconPathAsync(app);

            Assert.True(result.IsSuccess, result.Message);
            Assert.NotNull(result.Value);
            Assert.True(File.Exists(result.Value));
            Assert.Equal(".png", Path.GetExtension(result.Value), ignoreCase: true);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task IconCacheWritesPngFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var iconPath = Environment.ProcessPath ?? throw new InvalidOperationException("Test host path is unavailable.");

            var app = AppRecord.Create("icon-test", "Icon Test", AppSource.Registry, iconPath: iconPath);
            var service = new IconCacheService(Path.Combine(root, "cache"));
            var result = await service.GetIconPathAsync(app);
            Assert.True(result.IsSuccess, result.Message);
            Assert.NotNull(result.Value);
            Assert.True(File.Exists(result.Value));
            Assert.Equal(".png", Path.GetExtension(result.Value), ignoreCase: true);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
