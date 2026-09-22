using SysSuite.Core.System;

namespace SysSuite.Tests;

/// <summary>
/// G7 红线的兜底白名单：系统目录必须硬拒绝，正常用户数据不得误伤。
/// </summary>
public class ProtectedPathsTests
{
    private static string SamplePath => Path.Combine(Path.GetTempPath(), "syssuite-tests", "sample.tmp");

    [Fact]
    public void WindowsTreeIsProtected()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.True(ProtectedPaths.IsProtected(windows));
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(windows, "System32", "drivers", "etc", "hosts")));
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(windows, "WinSxS", "amd64", "payload.dll")));
    }

    [Fact]
    public void ProgramFilesTreeIsProtected()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Assert.True(ProtectedPaths.IsProtected(programFiles));
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(programFiles, "Some App", "app.exe")));
    }

    [Fact]
    public void BlankPathIsProtected()
    {
        Assert.True(ProtectedPaths.IsProtected(null));
        Assert.True(ProtectedPaths.IsProtected(string.Empty));
        Assert.True(ProtectedPaths.IsProtected("   "));
    }

    [Fact]
    public void DriveRootIsProtected()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var driveRoot = Path.GetPathRoot(windows);

        Assert.NotNull(driveRoot);
        Assert.True(ProtectedPaths.IsProtected(driveRoot));
    }

    [Fact]
    public void RecyclingAndVolumeSegmentsAreProtected()
    {
        var driveRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;

        Assert.True(ProtectedPaths.IsProtected(Path.Combine(driveRoot, "$Recycle.Bin", "S-1-5-21", "file.bin")));
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(driveRoot, "System Volume Information", "tracking.log")));
    }

    [Fact]
    public void UserProfileRootIsProtectedButChildrenAreNot()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.True(ProtectedPaths.IsProtected(profile));
        Assert.False(ProtectedPaths.IsProtected(Path.Combine(profile, "Downloads", "leftover.tmp")));
    }

    [Fact]
    public void ProgramDataRootIsProtectedButChildrenAreNot()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Assert.True(ProtectedPaths.IsProtected(programData));
        Assert.False(ProtectedPaths.IsProtected(Path.Combine(programData, "Contoso", "leftover.log")));
    }

    [Fact]
    public void OrdinaryTempPathIsNotProtected()
    {
        Assert.False(ProtectedPaths.IsProtected(SamplePath));
        Assert.Null(ProtectedPaths.DescribeMatch(SamplePath));
    }

    [Fact]
    public void DescribeMatchExplainsWhyPathIsRejected()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.Equal(windows, ProtectedPaths.DescribeMatch(windows));
        Assert.NotNull(ProtectedPaths.DescribeMatch(Path.Combine(windows, "System32")));
    }

    [Fact]
    public void RootsAreMaterialized()
    {
        Assert.NotEmpty(ProtectedPaths.Roots);
        Assert.All(ProtectedPaths.Roots, root => Assert.False(string.IsNullOrWhiteSpace(root)));
        Assert.All(ProtectedPaths.ExactOnlyRoots, root => Assert.False(string.IsNullOrWhiteSpace(root)));
    }
}
