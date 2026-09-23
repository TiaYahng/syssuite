namespace SysSuite.Core.Abstractions.System;

/// <summary>一次提权重启尝试的结果。</summary>
public sealed record ElevationResult(
    bool Requested,
    bool UserDeclined,
    string? FailureReason)
{
    /// <summary>已成功拉起提权进程，当前进程应当退出。</summary>
    public static ElevationResult Started() => new(true, false, null);

    /// <summary>用户在 UAC 弹窗点了「否」。这不是错误，UI 不应报错。</summary>
    public static ElevationResult Declined() => new(false, true, null);

    public static ElevationResult Failed(string reason) => new(false, false, reason);
}

/// <summary>
/// 提权能力（T1.3 配套）。
///
/// 背景：SMART 读取需要 `CreateFileW(PhysicalDriveN, GENERIC_READ)`，
/// 非提权进程会拿到 `ERROR_ACCESS_DENIED`。部分场景（如从无 manifest 的宿主
/// 进程以 `asInvoker` 启动）无法靠 manifest 自动提权，必须支持运行期按需提权。
///
/// 实现约定：提权是"拉起一个新进程并退出自己"，**不是**在当前进程内取令牌，
/// 因为 Windows 不允许已启动进程原地提升完整性级别。
/// </summary>
public interface IElevationService
{
    /// <summary>当前进程是否已是管理员。</summary>
    bool IsElevated { get; }

    /// <summary>
    /// 以管理员身份重新启动当前应用。
    ///
    /// 调用方在收到 <see cref="ElevationResult.Requested"/> = true 后**必须**尽快退出当前进程，
    /// 否则会出现两个实例（`App` 里的 `Mutex` 会让新实例因"已在运行"而直接退出）。
    /// </summary>
    ElevationResult RestartElevated();
}
