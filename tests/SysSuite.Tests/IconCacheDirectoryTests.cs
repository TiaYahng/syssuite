using System.Drawing;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;
using Xunit;

namespace SysSuite.Tests;

public class IconCacheDirectoryTests
{
    [Fact]
    public async Task IconCacheResolvesShortcutIconWhenRegistrySourcesAreUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var app = AppRecord.Create("shortcut-icon", "Notepad++", AppSource.Registry);
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
    public async Task IconCacheDoesNotPreferArbitraryInstallDirectoryImage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        var installDirectory = Path.Combine(root, "install");
        Directory.CreateDirectory(installDirectory);
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host path is unavailable.");
            File.Copy(executable, Path.Combine(installDirectory, "main.exe"));
            await File.WriteAllBytesAsync(
                Path.Combine(installDirectory, "aaa.png"),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

            var app = AppRecord.Create("install-directory-icon", "Install Directory Icon", AppSource.Registry, installDir: installDirectory);
            using var service = new IconCacheService(Path.Combine(root, "cache"));
            var result = await service.GetIconPathAsync(app);

            Assert.True(result.IsSuccess, result.Message);
            Assert.NotNull(result.Value);
            using var image = Image.FromFile(result.Value);
            Assert.True(image.Width > 1 || image.Height > 1);
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
    public async Task IconCacheDoesNotPreferUninstallerDirectoryImage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        var installDirectory = Path.Combine(root, "install");
        var uninstallerDirectory = Path.Combine(installDirectory, "Uninstaller");
        Directory.CreateDirectory(uninstallerDirectory);
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host path is unavailable.");
            File.Copy(executable, Path.Combine(installDirectory, "product.exe"));
            await File.WriteAllBytesAsync(
                Path.Combine(uninstallerDirectory, "productIcon.png"),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

            var app = AppRecord.Create("uninstaller-directory-icon", "Product", AppSource.Registry, installDir: installDirectory);
            using var service = new IconCacheService(Path.Combine(root, "cache"));
            var result = await service.GetIconPathAsync(app);

            Assert.True(result.IsSuccess, result.Message);
            Assert.NotNull(result.Value);
            using var image = Image.FromFile(result.Value);
            Assert.True(image.Width > 1 || image.Height > 1);
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
    public async Task IconCacheResolvesRelativeIconWithQuotedInstallDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        var installDirectory = Path.Combine(root, "install");
        Directory.CreateDirectory(installDirectory);
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host path is unavailable.");
            File.Copy(executable, Path.Combine(installDirectory, "main.exe"));

            var app = AppRecord.Create(
                "quoted-install-directory",
                "Quoted Install Directory",
                AppSource.Registry,
                installDir: $"\"{installDirectory}\"",
                iconPath: "main.exe,0");
            using var service = new IconCacheService(Path.Combine(root, "cache"));
            var result = await service.GetIconPathAsync(app);

            Assert.True(result.IsSuccess, result.Message);
            Assert.NotNull(result.Value);
            Assert.True(File.Exists(result.Value));
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
