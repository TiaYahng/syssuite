using SysSuite.Core.System;

namespace SysSuite.Tests;

/// <summary>
/// 硬件服务的数据清洗：WMI 原始值里有已知的溢出与格式噪声，必须在服务层收敛。
/// </summary>
public class HardwareInfoServiceTests
{
    [Theory]
    [InlineData("1920 x 1080 x 4294967296 种颜色", "1920 x 1080")]
    [InlineData("2560 x 1440 x 32 种颜色", "2560 x 1440")]
    [InlineData("1920x1080", "1920x1080")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeVideoModeKeepsResolutionOnly(string raw, string expected)
        => Assert.Equal(expected, WmiHardwareInfoService.NormalizeVideoMode(raw));

    [Fact]
    public void NormalizeVideoModeToleratesNull()
        => Assert.Equal(string.Empty, WmiHardwareInfoService.NormalizeVideoMode(null));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 2070 with Max-Q Design", "NVIDIA GeForce RTX 2070 with Max-Q Design")]
    [InlineData("nvidia geforce rtx 2070 with max-q design", "NVIDIA GeForce RTX 2070 with Max-Q Design")]
    [InlineData("Intel(R) UHD Graphics", "Intel(R) UHD Graphics 630")]
    [InlineData("NVIDIA GeForce RTX 2070", "RTX 2070")]
    public void MatchesAdapterUsesCaseInsensitiveContainment(string description, string adapterName)
        => Assert.True(WmiHardwareInfoService.MatchesAdapter(description, adapterName));

    [Theory]
    [InlineData("AMD Radeon RX 580", "NVIDIA GeForce RTX 2070")]
    [InlineData("Intel(R) UHD Graphics", "NVIDIA GeForce RTX 2070")]
    public void MatchesAdapterRejectsUnrelatedAdapters(string description, string adapterName)
        => Assert.False(WmiHardwareInfoService.MatchesAdapter(description, adapterName));

    [Fact]
    public void MatchesAdapterRejectsBlankAdapterName()
        => Assert.False(WmiHardwareInfoService.MatchesAdapter("NVIDIA GeForce", "   "));
}
