using SysSuite.Interop;

namespace SysSuite.Tests.Native;

/// <summary>
/// FFI 冒烟：成功 / 缓冲区不足 / 参数非法 / 取消 / 未实现 / 缺失降级 六类路径。
/// 契约本体见 docs/native-abi.md。
/// </summary>
public class NativeAbiSmokeTests
{
    [NativeRequiredFact]
    public void AbiVersionMatchesManagedContract()
    {
        var status = NativeInterop.GetAbiVersion(out var version);

        Assert.Equal(NativeStatus.Ok, status);
        Assert.Equal(1, version);
    }

    [NativeRequiredFact]
    public void VersionReturnsNonEmptyVersionString()
    {
        var status = NativeInterop.GetVersion(out var version);

        Assert.Equal(NativeStatus.Ok, status);
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.StartsWith("1.0.0", version!, StringComparison.Ordinal);
    }

    [NativeRequiredFact]
    public void VersionWithTooSmallBufferReturnsBufferTooSmall()
    {
        var status = NativeInterop.GetVersion(new char[4], out var version);

        Assert.Equal(NativeStatus.BufferTooSmall, status);
        Assert.Null(version);
    }

    [NativeRequiredFact]
    public void VersionWithEmptyBufferReturnsInvalidArgument()
    {
        var status = NativeInterop.GetVersion(Span<char>.Empty, out var version);

        Assert.Equal(NativeStatus.InvalidArgument, status);
        Assert.Null(version);
    }

    [NativeRequiredFact]
    public void ScanVolumeHonoursCancellationBeforeWork()
    {
        var status = NativeInterop.ScanVolume(@"\\?\C:", () => true);

        Assert.Equal(NativeStatus.Cancelled, status);
    }

    [NativeRequiredFact]
    public void ScanVolumeReportsNotImplementedWhenNotCancelled()
    {
        var status = NativeInterop.ScanVolume(@"\\?\C:", () => false);

        Assert.Equal(NativeStatus.NotImplemented, status);
    }

    [NativeRequiredFact]
    public void GetSmbiosReportsNotImplementedAndZeroWritten()
    {
        var status = NativeInterop.GetSmbios(new byte[64], out var written);

        Assert.Equal(NativeStatus.NotImplemented, status);
        Assert.Equal(0, written);
    }

    [Fact]
    public void MissingLibraryDegradesToStatusInsteadOfException()
    {
        if (NativeInterop.IsLibraryAvailable)
        {
            // 原生库存在时，正常路径由上面的冒烟覆盖
            return;
        }

        var status = NativeInterop.GetVersion(out var version);

        Assert.Equal(NativeStatus.LibraryNotLoaded, status);
        Assert.Null(version);
        Assert.Contains("CMake", NativeInterop.Describe(status), StringComparison.Ordinal);
    }

    [Fact]
    public void ScanVolumeRejectsBlankVolume()
    {
        var status = NativeInterop.ScanVolume("   ");

        Assert.Equal(NativeStatus.InvalidArgument, status);
    }
}
