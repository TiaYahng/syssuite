using System.Globalization;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 更新控制的自检（M4 T4.2）。与 Snapshot.cs 拆开只为守住 300 行门禁。
///
/// 自检要回答的问题是"Windows 有没有把我们设的东西改回去"，而不是"当前设置是什么" ——
/// 后者由 ReadStatus 负责，两者别混。
/// </summary>
public sealed partial class WindowsUpdateControlService
{
    /// <summary>
    /// 对外自检：**只检查我们自己应用过档位的机器**。
    ///
    /// 没有快照、或快照里没有记录目标档位时直接返回空 —— 这意味着本程序从未改过这台机器，
    /// 那么"系统默认值"就是正常状态，不该被当成"设置被回滚"。早期版本按当前档位反推期望值，
    /// 实测在从未改过的机器上会把 UsoSvc 的 Auto 误报成漂移。
    /// </summary>
    private Result<IReadOnlyList<UpdateControlItem>> Verify()
    {
        var target = TryLoadSnapshot()?.TargetMode;
        if (target is null || target.Value == UpdateMode.Unknown)
        {
            return new Result<IReadOnlyList<UpdateControlItem>>(ErrorType.None, string.Empty, []);
        }

        return Verify(target.Value);
    }

    /// <summary>
    /// 按目标档位逐项比对（内部重载，供测试直接指定档位）。
    ///
    /// 期望值在**比对时**现算而不是写进快照：策略层当前档位决定服务层该处于什么状态，
    /// 把期望值固化下来会在用户手动改设置后过期。
    /// </summary>
    internal Result<IReadOnlyList<UpdateControlItem>> Verify(UpdateMode target)
    {
        var drifts = new List<UpdateControlItem>();

        foreach (var item in ReadStatus().Items)
        {
            var expected = ExpectedPolicyValue(target, item.Id);

            // Automatic 档的期望是"键值不存在"，这与"读不到"无法区分，跳过以免误报
            if (expected is null)
            {
                continue;
            }

            var withExpected = item with { Expected = expected };
            if (withExpected.IsConflicting)
            {
                drifts.Add(withExpected);
            }
        }

        foreach (var item in ReadServiceItems())
        {
            var expected = item.Id.StartsWith("service.", StringComparison.Ordinal)
                ? (target == UpdateMode.Disabled ? "Disabled" : "Manual")
                : (target == UpdateMode.Disabled ? "Disabled" : "Enabled");

            var withExpected = item with { Expected = expected };
            if (withExpected.IsConflicting)
            {
                drifts.Add(withExpected);
            }
        }

        return new Result<IReadOnlyList<UpdateControlItem>>(ErrorType.None, string.Empty, drifts);
    }

    /// <summary>策略项在目标档位下的期望值；返回 null 表示该项不可校验。</summary>
    private static string? ExpectedPolicyValue(UpdateMode target, string itemId)
    {
        if (!itemId.StartsWith("policy.", StringComparison.Ordinal))
        {
            return null;
        }

        // Automatic 档靠"删除键值"表达，无法与"读不到"区分
        if (target == UpdateMode.Automatic)
        {
            return null;
        }

        var (noAuto, auOptions) = ExpectedValues(target);
        return itemId.EndsWith("NoAutoUpdate", StringComparison.Ordinal)
            ? noAuto.ToString(CultureInfo.InvariantCulture)
            : auOptions.ToString(CultureInfo.InvariantCulture);
    }
}
