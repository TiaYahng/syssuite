namespace SysSuite.Core.Abstractions.System;

/// <summary>
/// 系统瘦身（T3.5）。
/// </summary>
/// <remarks>
/// 风险分级 L2 / 实验性：**必须同时满足** <c>EnableExperimentalFeatures</c> 与
/// <c>EnableSystemSlimming</c> 才可用，且实际写入动作还需要管理员权限。
/// 这是本套件里唯一会改动系统组件存储的能力，MVP0 阶段禁止开放。
/// </remarks>
public interface ISystemSlimmingService
{
    /// <summary>两个实验性开关是否都已打开。</summary>
    bool IsEnabled { get; }

    /// <summary>分析各项可回收空间。分析本身只读，非提权也能跑（组件存储部分会缺数据）。</summary>
    Task<Result<SystemSlimmingReport>> AnalyzeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>dism /Online /Cleanup-Image /StartComponentCleanup</c>，可选 <c>/ResetBase</c>。
    /// </summary>
    Task<Result<SystemSlimmingResult>> StartComponentCleanupAsync(
        SystemSlimmingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>compact /c /exe</c> 按文件压缩。
    /// 只接受**非系统保护路径**：系统临界文件的排除清单直接复用 G7 白名单，
    /// 而不是另立一份必然过期的文件列表。
    /// </summary>
    Task<Result<SystemSlimmingResult>> CompactAsync(
        string directory,
        bool executableOnly = true,
        CancellationToken cancellationToken = default);
}
