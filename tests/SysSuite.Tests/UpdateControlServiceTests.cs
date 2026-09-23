using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;

namespace SysSuite.Tests;

/// <summary>
/// M4 T4.1 更新控制：档位推导、期望值映射、快照还原的纯逻辑部分。
///
/// 这些测试**不碰真实注册表与服务**（那需要管理员权限且会改本机配置），
/// 全部走 <see cref="WindowsUpdateControlService"/> 的探针注入点。
/// 覆盖的重点不是"能不能写"，而是"读不到时会不会误判" —— 那才是这类功能的翻车点。
/// </summary>
public class UpdateControlServiceTests
{
    [Theory]
    [InlineData(UpdateMode.Automatic, 0, 4)]
    [InlineData(UpdateMode.NotifyOnly, 1, 2)]
    [InlineData(UpdateMode.Disabled, 1, 1)]
    public void ExpectedValuesMatchDocumentedPolicySemantics(UpdateMode mode, int noAuto, int auOptions)
    {
        var (actualNoAuto, actualOptions) = WindowsUpdateControlService.ExpectedValues(mode);

        Assert.Equal(noAuto, actualNoAuto);
        Assert.Equal(auOptions, actualOptions);
    }

    [Theory]
    [InlineData("1", "2", UpdateMode.NotifyOnly)]
    [InlineData("1", "1", UpdateMode.Disabled)]
    [InlineData("0", "4", UpdateMode.Automatic)]
    [InlineData("0", "3", UpdateMode.NotifyOnly)]
    [InlineData("0", "2", UpdateMode.NotifyOnly)]
    public void DeriveModeIsTheInverseOfExpectedValues(string noAuto, string auOptions, UpdateMode expected)
    {
        var status = StatusWithItems(
            new UpdateControlItem("policy.NoAutoUpdate", "不自动更新", noAuto, noAuto),
            new UpdateControlItem("policy.AUOptions", "更新方式", auOptions, auOptions));

        Assert.Equal(expected, WindowsUpdateControlService.DeriveMode(status));
    }

    /// <summary>
    /// 应用 → 读回的闭环：我们写出去的那对值，读回来必须仍是同一档位。
    /// 这条断了就会出现"设置显示已改成仅通知，刷新后又变回自动更新"。
    /// </summary>
    [Theory]
    [InlineData(UpdateMode.NotifyOnly)]
    [InlineData(UpdateMode.Disabled)]
    public void ApplyThenReadBackYieldsTheSameMode(UpdateMode mode)
    {
        var (noAuto, auOptions) = WindowsUpdateControlService.ExpectedValues(mode);
        var status = StatusWithItems(
            new UpdateControlItem(
                "policy.NoAutoUpdate", "不自动更新",
                noAuto.ToString(System.Globalization.CultureInfo.InvariantCulture), null),
            new UpdateControlItem(
                "policy.AUOptions", "更新方式",
                auOptions.ToString(System.Globalization.CultureInfo.InvariantCulture), null));

        Assert.Equal(mode, WindowsUpdateControlService.DeriveMode(status));
    }

    [Fact]
    public void DeriveModeHonorsAuOptionsEvenWhenNoAutoUpdateIsAbsent()
    {
        // 实测本机形态：NoAutoUpdate 缺失、AUOptions=2。只看 NoAutoUpdate 会误报成"自动更新"，
        // 而 Windows 的真实行为是"通知但不自动安装"。
        var status = StatusWithItems(
            new UpdateControlItem("policy.NoAutoUpdate", "不自动更新", null, null),
            new UpdateControlItem("policy.AUOptions", "更新方式", "2", "2"));

        Assert.Equal(UpdateMode.NotifyOnly, WindowsUpdateControlService.DeriveMode(status));
    }

    [Fact]
    public void DeriveModeTreatsScheduledInstallAsAutomatic()
    {
        var status = StatusWithItems(
            new UpdateControlItem("policy.NoAutoUpdate", "不自动更新", "0", "0"),
            new UpdateControlItem("policy.AUOptions", "更新方式", "4", "4"));

        Assert.Equal(UpdateMode.Automatic, WindowsUpdateControlService.DeriveMode(status));
    }

    [Fact]
    public void DeriveModePrefersPolicyLayerOverServiceLayer()
    {
        // 域管机型的典型形态：策略说停用，服务仍是 Auto。真实行为是"停用"。
        var status = StatusWithItems(
            new UpdateControlItem("policy.NoAutoUpdate", "不自动更新", "1", "1"),
            new UpdateControlItem("policy.AUOptions", "更新方式", "2", "2"),
            new UpdateControlItem("service.wuauserv", "服务", "Auto", "Manual"));

        Assert.Equal(UpdateMode.NotifyOnly, WindowsUpdateControlService.DeriveMode(status));
    }

    [Fact]
    public void DeriveModeReturnsUnknownWhenNothingReadable()
    {
        var status = StatusWithItems(
            new UpdateControlItem("policy.NoAutoUpdate", "不自动更新", null, null),
            new UpdateControlItem("policy.AUOptions", "更新方式", null, null));

        Assert.Equal(UpdateMode.Unknown, WindowsUpdateControlService.DeriveMode(status));
    }

    [Fact]
    public void UnreadableItemIsNotTreatedAsConflict()
    {
        // 这是本功能最核心的一条：读不到 ≠ 被系统回滚。
        var unreadable = new UpdateControlItem("service.wuauserv", "服务", null, "Disabled");

        Assert.False(unreadable.IsReadable);
        Assert.False(unreadable.IsConflicting);
        Assert.False(unreadable.IsApplied);
    }

    [Fact]
    public void DivergentReadableItemIsConflict()
    {
        var drifted = new UpdateControlItem("service.wuauserv", "服务", "Manual", "Disabled");

        Assert.True(drifted.IsConflicting);
    }

    [Fact]
    public void MatchingValueIsAppliedRegardlessOfCase()
    {
        var applied = new UpdateControlItem("service.wuauserv", "服务", "disabled", "Disabled");

        Assert.True(applied.IsApplied);
        Assert.False(applied.IsConflicting);
    }

    private static UpdateControlStatus StatusWithItems(params UpdateControlItem[] items)
        => new(UpdateMode.Unknown, UpdateModeScope.Unknown, IsElevated: true, items);
}
