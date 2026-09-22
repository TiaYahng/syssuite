using SysSuite.Core.Data;
using SysSuite.Core.Settings;
using Xunit;

namespace SysSuite.Tests;

public class ForceDeleteServiceTests
{
    [Fact]
    public async Task ForceDeleteServiceRequiresConfirmationAndExperimentalSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-{Guid.NewGuid():N}");
        var settingsRoot = Path.Combine(root, "settings");
        Directory.CreateDirectory(settingsRoot);
        var settings = new SettingsService(settingsRoot);
        var service = new ForceDeleteService(settings, Path.Combine(root, "backup"));
        try
        {
            var target = Path.Combine(root, "target.txt");
            await File.WriteAllTextAsync(target, "test");
            settings.Current.EnableExperimentalFeatures = true;
            settings.Current.EnableForceDelete = true;

            var rejected = await service.DeleteAsync(target, "wrong");
            Assert.False(rejected.IsSuccess);
            Assert.True(File.Exists(target));

            var result = await service.DeleteAsync(target, ForceDeleteService.CreateConfirmation(target));
            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(1, result.Value!.DeletedFiles);
            Assert.False(File.Exists(target));
        }
        finally
        {
            settings.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
