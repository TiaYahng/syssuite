using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 提权服务（T1.3 配套）。
///
/// 关键设计：Windows **不允许**已启动进程原地提升完整性级别，
/// 因此"获取权限"的唯一可行做法是 `ShellExecute(runas)` 拉起一个新进程，
/// 随后由调用方退出自己。用 `UseShellExecute = true` + `Verb = "runas"`
/// 才能触发 UAC；`UseShellExecute = false` 下 `Verb` 会被忽略。
/// </summary>
public sealed class ElevationService : IElevationService
{
    /// <summary>UAC 被用户拒绝时 ShellExecute 抛出的 Win32 错误码（ERROR_CANCELLED）。</summary>
    private const int ErrorCancelled = 1223;

    public bool IsElevated => Environment.IsPrivilegedProcess;

    public ElevationResult RestartElevated()
    {
        if (IsElevated)
        {
            // 已经是管理员，无需重启
            return ElevationResult.Started();
        }

        var executable = ResolveExecutablePath();
        if (executable is null)
        {
            return ElevationResult.Failed("无法定位当前可执行文件路径，请手动右键「以管理员身份运行」。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppContext.BaseDirectory,
            // 必须 UseShellExecute = true，否则 Verb="runas" 不生效、不会弹 UAC
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ElevationResult.Failed("提权进程未能启动。");
            }

            return ElevationResult.Started();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            // 用户在 UAC 弹窗点了「否」——这是正常操作，不是错误
            return ElevationResult.Declined();
        }
        catch (Win32Exception exception)
        {
            return ElevationResult.Failed(string.Create(
                CultureInfo.InvariantCulture,
                $"提权失败（Win32 错误 {exception.NativeErrorCode}）：{exception.Message}"));
        }
        catch (InvalidOperationException exception)
        {
            return ElevationResult.Failed($"提权失败：{exception.Message}");
        }
    }

    /// <summary>
    /// 解析当前进程的 exe 路径。
    ///
    /// 不用 <c>Assembly.Location</c>：单文件发布（PublishSingleFile）下它返回空串。
    /// <see cref="Environment.ProcessPath"/> 在 .NET 6+ 对单文件与常规部署都可靠。
    /// </summary>
    private static string? ResolveExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return path;
        }

        // 兜底：dotnet 宿主（dotnet run）场景下 ProcessPath 是 dotnet.exe，
        // 此时无法自提权，交给 UI 提示用户手动操作
        return null;
    }
}
