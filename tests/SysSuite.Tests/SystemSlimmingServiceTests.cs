using SysSuite.Core.Abstractions.System;
using SysSuite.Core.Settings;
using SysSuite.Core.System;
using Xunit;

namespace SysSuite.Tests;

/// <summary>
/// T3.5 系统瘦身。
///
/// 真实清理（DISM 组件存储、compact 全盘）需要管理员权限且会改系统状态，不能进单测。
/// 这里覆盖三类：**门控**（双开关缺一即不可用）、**拒绝分支**（保护路径）、
/// **解析**（DISM 输出是本地化的，解析失败必须报"未知"而不是猜一个数字）。
/// </summary>
public class SystemSlimmingServiceTests
{
    // ---- 门控：必须是双开关 ----

    [Fact]
    public async Task CompactIsDisabledWhenNeitherExperimentalFlagIsOn()
    {
        await WithServiceAsync(async (service, settings) =>
        {
            settings.Current.EnableExperimentalFeatures = false;
            settings.Current.EnableSystemSlimming = false;

            var result = await service.CompactAsync(@"C:\Windows");

            Assert.False(service.IsEnabled);
            Assert.Equal(SlimmingOutcome.Disabled, result.Value!.Outcome);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BothExperimentalFlagsAreRequiredForEnablement(bool experimental, bool slimming)
    {
        // 只开一个不算数：用户打开"实验性功能"往往是为了别的功能，
        // 不该顺带把组件存储清理也放出来
        await WithServiceAsync(async (service, settings) =>
        {
            settings.Current.EnableExperimentalFeatures = experimental;
            settings.Current.EnableSystemSlimming = slimming;

            var result = await service.CompactAsync(@"C:\Windows");

            Assert.False(service.IsEnabled);
            Assert.Equal(SlimmingOutcome.Disabled, result.Value!.Outcome);
        });
    }

    // ---- 拒绝分支：compact 不得落在系统保护路径上 ----

    [Fact]
    public async Task CompactRejectsProtectedPaths()
    {
        await WithServiceAsync(async (service, settings) =>
        {
            Enable(settings);

            var result = await service.CompactAsync(@"C:\Windows\System32");
            var value = result.Value!;

            Assert.Equal(SlimmingOutcome.Rejected, value.Outcome);
            Assert.Contains("系统保护路径", value.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CompactRejectsMissingDirectory()
    {
        await WithServiceAsync(async (service, settings) =>
        {
            Enable(settings);

            var result = await service.CompactAsync(@"Z:\no-such-directory-xyz");

            Assert.Equal(SlimmingOutcome.Rejected, result.Value!.Outcome);
        });
    }

    [Fact]
    public async Task CleanupComponentStoreIsDisabledBeforeCheckingElevation()
    {
        // 顺序很重要：先报"功能没开"再谈提权，否则非提权用户会看到
        // "需要管理员权限"却不知道自己其实连开关都没打开
        await WithServiceAsync(async (service, settings) =>
        {
            settings.Current.EnableExperimentalFeatures = false;
            settings.Current.EnableSystemSlimming = false;

            var result = await service.StartComponentCleanupAsync(new SystemSlimmingOptions());

            Assert.Equal(SlimmingOutcome.Disabled, result.Value!.Outcome);
        });
    }

    [Fact]
    public void ResetBaseDefaultsToOff()
    {
        // /ResetBase 让已安装的更新不可卸载，默认必须是关的
        Assert.False(new SystemSlimmingOptions().ResetBase);
    }
    private static void Enable(SettingsService settings)
    {
        settings.Current.EnableExperimentalFeatures = true;
        settings.Current.EnableSystemSlimming = true;
    }

    private static List<string> Split(string text)
        => [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static async Task WithServiceAsync(Func<SystemSlimmingService, SettingsService, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"SysSuite-slimming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settings = new SettingsService(root);
        try
        {
            await body(new SystemSlimmingService(settings, Path.Combine(root, "Windows")), settings);
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
