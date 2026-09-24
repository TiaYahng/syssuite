using SysSuite.Core.System;
using Xunit;

namespace SysSuite.Tests;

/// <summary>
/// T3.4：清理前系统还原点的判定逻辑。
///
/// 只测纯判定（<see cref="SystemRestoreService.Decide"/>）：真正调 srclient 需要管理员权限
/// 且会在本机留下还原点，不能进单测。抽成纯函数就是为了让"该不该建"这件事可验证。
/// </summary>
public class SystemRestoreServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 15, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void DecideBlocksWhenRestoreIsDisabledByPolicy()
    {
        var decision = SystemRestoreService.Decide(disabledByPolicy: true, lastDate: null, Now);

        Assert.NotNull(decision);
        Assert.Equal(SystemRestoreOutcome.Disabled, decision.Outcome);
        Assert.False(decision.IsCreated);
    }

    [Fact]
    public void PolicyTakesPrecedenceOverSameDayRecord()
    {
        // 策略关闭时即使今天已经建过，也要报"被策略关闭"——否则用户会以为功能可用
        var decision = SystemRestoreService.Decide(disabledByPolicy: true, lastDate: "2026-09-24", Now);

        Assert.Equal(SystemRestoreOutcome.Disabled, decision!.Outcome);
    }

    [Fact]
    public void DecideSkipsWhenOneWasAlreadyCreatedToday()
    {
        var decision = SystemRestoreService.Decide(disabledByPolicy: false, lastDate: "2026-09-24", Now);

        Assert.NotNull(decision);
        Assert.Equal(SystemRestoreOutcome.SkippedSameDay, decision.Outcome);
        Assert.Contains("2026-09-24", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecideAllowsCreationWhenLastRecordIsFromAnotherDay()
    {
        Assert.Null(SystemRestoreService.Decide(disabledByPolicy: false, lastDate: "2026-09-23", Now));
    }

    [Fact]
    public void DecideAllowsCreationWhenThereIsNoRecord()
    {
        Assert.Null(SystemRestoreService.Decide(disabledByPolicy: false, lastDate: null, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("2026/09/24")]
    [InlineData("24-09-2026")]
    public void CorruptedStateValueDoesNotBlockCreation(string stored)
    {
        // "读不到 ≠ 已应用"的同一条原则：状态值脏了宁可多建一个还原点，也不让用户失去这道保险
        Assert.Null(SystemRestoreService.Decide(disabledByPolicy: false, lastDate: stored, Now));
    }

    [Fact]
    public void SameDayComparisonIgnoresTimeOfDay()
    {
        // 只记日期不记时间：否则"差几小时就又建一个"，一天能堆出好几个还原点
        var lastNight = new DateTimeOffset(2026, 9, 24, 0, 1, 0, TimeSpan.FromHours(8));

        var decision = SystemRestoreService.Decide(false, lastNight.ToString("yyyy-MM-dd", null), Now);

        Assert.Equal(SystemRestoreOutcome.SkippedSameDay, decision!.Outcome);
    }

    [Fact]
    public void TruncateKeepsShortDescriptionsIntact()
    {
        Assert.Equal("SysSuite 清理", SystemRestoreService.Truncate("SysSuite 清理"));
    }

    [Fact]
    public void TruncateCutsAtTheFixedBufferBoundary()
    {
        // srclient 的 Description 是定长缓冲，超长会被静默截断 —— 自己不截就会被系统截得莫名其妙
        var result = SystemRestoreService.Truncate(new string('x', 1000));

        Assert.Equal(255, result.Length);
    }
}
