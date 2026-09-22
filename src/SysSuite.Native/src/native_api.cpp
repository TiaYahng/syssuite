// SysSuite.Native —— ABI 实现
//
// T0.2 只落地 Native_Version（验证调用链的探针）。
// 其余入口按 G10 要求保留签名并返回 NATIVE_ERR_NOT_IMPL，
// 让 C# 侧可以提前对接与测试，而不必等到 T1.3 / T3.2 实现完成。
#include "native_api.h"

#include <cstring>
#include <cstddef>
#include <cwchar>

namespace {

// 版本串遵循 <主>.<次>.<修订>-<里程碑>；与 docs/native-abi.md 的版本兼容策略一致
constexpr wchar_t kNativeVersion[] = L"1.0.0-m0";

}  // namespace

extern "C" int NATIVE_CALL Native_AbiVersion(void)
{
    return NATIVE_ABI_VERSION;
}

extern "C" int NATIVE_CALL Native_Version(wchar_t* buffer, int capacity)
{
    if (buffer == nullptr || capacity <= 0)
    {
        return NATIVE_ERR_INVALID_ARG;
    }

    const std::size_t required = std::wcslen(kNativeVersion) + 1u;
    if (static_cast<std::size_t>(capacity) < required)
    {
        // 契约：缓冲区不足时不得写入任何字节，避免调用方拿到截断串
        return NATIVE_ERR_BUFFER_SMALL;
    }

    std::memcpy(buffer, kNativeVersion, required * sizeof(wchar_t));
    return NATIVE_OK;
}

extern "C" int NATIVE_CALL Native_GetSmbios(uint8_t* buffer, int capacity, int* written)
{
    // 未实现占位（T1.3）：参数仍需校验，防止下游误用掩盖真实错误
    if (buffer == nullptr || capacity <= 0 || written == nullptr)
    {
        return NATIVE_ERR_INVALID_ARG;
    }

    *written = 0;
    return NATIVE_ERR_NOT_IMPL;
}

extern "C" int NATIVE_CALL Native_ScanVolume(const wchar_t* volume,
                                             NativeScanCallback onBatch,
                                             void* context,
                                             NativeCancelCheck isCancelled)
{
    if (volume == nullptr || volume[0] == L'\0')
    {
        return NATIVE_ERR_INVALID_ARG;
    }

    // 契约：进入扫描前必须先做一次取消检查，保证"请求即取消"可被确定性观测
    if (isCancelled != nullptr && isCancelled(context) != 0)
    {
        return NATIVE_ERR_CANCELLED;
    }

    (void)onBatch;
    return NATIVE_ERR_NOT_IMPL;
}
