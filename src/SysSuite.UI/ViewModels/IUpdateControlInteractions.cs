using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 更新控制的交互副作用（D1 模式：ViewModel 不直接碰 UI）。
///
/// 二次确认刻意做成两个方法而不是一个带 <c>isHighRisk</c> 参数的：
/// Service 层的确认文案、按钮形态与 L1 完全不同，合成一个只会让文案在两边都别扭。
/// </summary>
public interface IUpdateControlInteractions
{
    /// <summary>Policy 层变更确认（L1）。返回 false 表示用户放弃。</summary>
    bool ConfirmPolicyChange(UpdateMode mode);

    /// <summary>Service 层变更确认（L2，高风险）。必须明确提示"可能影响安全补丁"。</summary>
    bool ConfirmServiceLayerChange(UpdateMode mode);

    /// <summary>还原确认。返回 false 表示用户放弃。</summary>
    bool ConfirmRestore();

    /// <summary>提示一条信息（不可用时回退到状态文本）。</summary>
    void Notify(string message);

    /// <summary>把文本放进剪贴板（诊断 ID / 快照路径）。</summary>
    void SetClipboard(string text);
}
