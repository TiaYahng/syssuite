using SysSuite.Interop;
using Xunit;

namespace SysSuite.Tests.Native;

/// <summary>
/// 仅在 SysSuite.Native.dll 已部署时执行。
/// 本机未安装 CMake/MSVC 时自动跳过，避免把"未构建原生模块"误报为测试失败。
/// </summary>
public sealed class NativeRequiredFactAttribute : FactAttribute
{
    public NativeRequiredFactAttribute()
    {
        if (!NativeInterop.IsLibraryAvailable)
        {
            Skip = "SysSuite.Native.dll 未部署，跳过 FFI 冒烟（需先执行 CMake 构建）";
        }
    }
}
