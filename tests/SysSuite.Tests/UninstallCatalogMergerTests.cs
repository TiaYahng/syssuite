using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;
using Xunit;

namespace SysSuite.Tests;

public class UninstallCatalogMergerTests
{
    [Fact]
    public void MergePrefersMsiRecordAndFillsMissingFields()
    {
        var registryRecord = new AppRecord(
            0,
            "REG|HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{ABCD-0000-0000-0000-000000000000}",
            "Test Product 1.0",
            "Test Publisher",
            "1.0",
            "20260912",
            null,
            "uninstall.exe",
            null,
            AppSource.Registry,
            "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{ABCD-0000-0000-0000-000000000000}",
            "C:\\Test",
            "icon.ico",
            null,
            DateTimeOffset.UtcNow);
        var msiRecord = new AppRecord(
            0,
            "MSI|{ABCD-0000-0000-0000-000000000000}",
            "Test Product 1.0",
            "Test Publisher",
            "1.0",
            null,
            2048,
            "msiexec /x {ABCD-0000-0000-0000-000000000000}",
            "msiexec /x {ABCD-0000-0000-0000-000000000000} /qn",
            AppSource.Msi,
            "{ABCD-0000-0000-0000-000000000000}",
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        var merged = UninstallCatalogMerger.Merge([registryRecord, msiRecord]);
        var record = Assert.Single(merged);
        Assert.Equal(AppSource.Msi, record.Source);
        Assert.Equal(msiRecord.StableKey, record.StableKey);
        Assert.Equal("Test Publisher", record.Publisher);
        Assert.Equal(msiRecord.UninstallString, record.UninstallString);
        Assert.Equal("C:\\Test", record.InstallDir);
        Assert.Equal(2048, record.Size);
    }
    [Fact]
    public void MergeDoesNotCombineDifferentMsiProductCodesWithSameName()
    {
        var visibleRecord = new AppRecord(
            0,
            "MSI|{11111111-2222-3333-4444-555555555555}",
            "Runtime",
            "Publisher",
            null,
            null,
            null,
            "msiexec /x {11111111-2222-3333-4444-555555555555}",
            null,
            AppSource.Msi,
            "{11111111-2222-3333-4444-555555555555}",
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
        var hiddenRecord = new AppRecord(
            0,
            "MSI|{99999999-8888-7777-6666-555555555555}",
            "Runtime",
            "Publisher",
            null,
            null,
            null,
            "msiexec /x {99999999-8888-7777-6666-555555555555}",
            null,
            AppSource.Msi,
            "{99999999-8888-7777-6666-555555555555}",
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        var merged = UninstallCatalogMerger.Merge([visibleRecord, hiddenRecord]);

        Assert.Equal(2, merged.Count);
    }
}
