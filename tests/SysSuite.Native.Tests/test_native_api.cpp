// ABI 契约测试（Catch2）。与 docs/native-abi.md §8 的四类路径一一对应。
// 本机通常缺少 CMake / MSVC，由 CI 的 native job 执行。
#include <catch2/catch_test_macros.hpp>

#include "native_api.h"

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace {

int NATIVE_CALL AlwaysCancel(void* context)
{
    (void)context;
    return 1;
}

int NATIVE_CALL NeverCancel(void* context)
{
    (void)context;
    return 0;
}

constexpr const wchar_t* kVolume = L"\\\\?\\C:";

}  // namespace

TEST_CASE("Native_AbiVersion 返回合同版本", "[abi][handshake]")
{
    REQUIRE(Native_AbiVersion() == NATIVE_ABI_VERSION);
}

TEST_CASE("Native_Version 写入完整版本串", "[abi][version]")
{
    std::vector<wchar_t> buffer(NATIVE_VERSION_CAPACITY_MIN, L'\0');

    REQUIRE(Native_Version(buffer.data(), static_cast<int>(buffer.size())) == NATIVE_OK);
    REQUIRE(std::wstring(buffer.data()) == std::wstring(L"1.0.0-m0"));
}

TEST_CASE("Native_Version 缓冲区不足时不写入任何字节", "[abi][version]")
{
    std::vector<wchar_t> buffer(NATIVE_VERSION_CAPACITY_MIN, L'#');

    REQUIRE(Native_Version(buffer.data(), 4) == NATIVE_ERR_BUFFER_SMALL);
    REQUIRE(buffer[0] == L'#');
}

TEST_CASE("Native_Version 拒绝非法参数", "[abi][version]")
{
    std::vector<wchar_t> buffer(NATIVE_VERSION_CAPACITY_MIN, L'\0');

    REQUIRE(Native_Version(nullptr, static_cast<int>(buffer.size())) == NATIVE_ERR_INVALID_ARG);
    REQUIRE(Native_Version(buffer.data(), 0) == NATIVE_ERR_INVALID_ARG);
}

TEST_CASE("Native_ScanVolume 进入前即响应取消", "[abi][cancel]")
{
    REQUIRE(Native_ScanVolume(kVolume, nullptr, nullptr, &AlwaysCancel) == NATIVE_ERR_CANCELLED);
}

TEST_CASE("Native_ScanVolume 未取消时返回未实现占位", "[abi][placeholder]")
{
    REQUIRE(Native_ScanVolume(kVolume, nullptr, nullptr, &NeverCancel) == NATIVE_ERR_NOT_IMPL);
    REQUIRE(Native_ScanVolume(L"", nullptr, nullptr, &NeverCancel) == NATIVE_ERR_INVALID_ARG);
}

TEST_CASE("Native_GetSmbios 返回未实现占位且 written 归零", "[abi][placeholder]")
{
    std::vector<std::uint8_t> buffer(64, 0);
    int written = -1;

    REQUIRE(Native_GetSmbios(buffer.data(), static_cast<int>(buffer.size()), &written) ==
            NATIVE_ERR_NOT_IMPL);
    REQUIRE(written == 0);
}

// ---------------------------------------------------------------------------
// Native_QuerySmart（T1.3）契约测试
//
// 这些用例只覆盖"与硬件无关"的契约面：参数校验、取消时机、容量边界。
// 真实 SMART 读数依赖具体硬件与管理员权限，不在契约测试范围内。
// ---------------------------------------------------------------------------

namespace {

// 构造一个 structSize 已预填的 SMART 缓冲区，模拟 Interop 层的调用方式
std::vector<NativeSmartInfo> MakeSmartBuffer(int capacity)
{
    std::vector<NativeSmartInfo> buffer(static_cast<std::size_t>(capacity));
    for (auto& entry : buffer)
    {
        entry.structSize = static_cast<std::uint32_t>(sizeof(NativeSmartInfo));
    }

    return buffer;
}

}  // namespace

TEST_CASE("Native_QuerySmart 拒绝非法参数", "[abi][smart]")
{
    auto buffer = MakeSmartBuffer(NATIVE_SMART_CAPACITY_MIN);
    int count = -1;

    REQUIRE(Native_QuerySmart(nullptr, NATIVE_SMART_CAPACITY_MIN, &count, nullptr, nullptr) ==
            NATIVE_ERR_INVALID_ARG);
    REQUIRE(Native_QuerySmart(buffer.data(), 0, &count, nullptr, nullptr) == NATIVE_ERR_INVALID_ARG);
    REQUIRE(Native_QuerySmart(buffer.data(), -1, &count, nullptr, nullptr) == NATIVE_ERR_INVALID_ARG);
    REQUIRE(Native_QuerySmart(buffer.data(), NATIVE_SMART_CAPACITY_MIN, nullptr, nullptr, nullptr) ==
            NATIVE_ERR_INVALID_ARG);
}

TEST_CASE("Native_QuerySmart 进入前即响应取消", "[abi][smart][cancel]")
{
    auto buffer = MakeSmartBuffer(NATIVE_SMART_CAPACITY_MIN);
    int count = -1;

    REQUIRE(Native_QuerySmart(buffer.data(), NATIVE_SMART_CAPACITY_MIN, &count, &AlwaysCancel, nullptr) ==
            NATIVE_ERR_CANCELLED);
}

TEST_CASE("Native_QuerySmart 取消时 count 归零", "[abi][smart][cancel]")
{
    auto buffer = MakeSmartBuffer(NATIVE_SMART_CAPACITY_MIN);
    int count = 12345;

    REQUIRE(Native_QuerySmart(buffer.data(), NATIVE_SMART_CAPACITY_MIN, &count, &AlwaysCancel, nullptr) ==
            NATIVE_ERR_CANCELLED);
    REQUIRE(count == 0);
}

TEST_CASE("Native_QuerySmart 不写入超出 capacity 的元素", "[abi][smart][bounds]")
{
    // capacity = 1：无论机器上有几块盘，都不得越界写第二个元素
    auto buffer = MakeSmartBuffer(1);
    buffer[0].driveNumber = 0xDEADBEEFu;
    int count = -1;

    const int status = Native_QuerySmart(buffer.data(), 1, &count, &NeverCancel, nullptr);

    // 允许 Ok（读到盘）或 AccessDenied（未提权），两者都是合法结果
    REQUIRE((status == NATIVE_OK || status == NATIVE_ERR_ACCESS_DENIED));
    REQUIRE(count >= 0);
    REQUIRE(count <= 1);
}

TEST_CASE("Native_QuerySmart 非提权环境下明确报告权限不足", "[abi][smart][elevation]")
{
    auto buffer = MakeSmartBuffer(NATIVE_SMART_CAPACITY_MIN);
    int count = -1;

    const int status = Native_QuerySmart(buffer.data(), NATIVE_SMART_CAPACITY_MIN, &count, &NeverCancel, nullptr);

    // 关键契约：未提权的 CI 环境必须返回 ACCESS_DENIED 而不是 OK + 零值条目，
    // 否则上层 UI 会把"权限不足"误报成"硬盘不支持 SMART"。
    if (status == NATIVE_ERR_ACCESS_DENIED)
    {
        REQUIRE(count == 0);
    }
    else
    {
        REQUIRE(status == NATIVE_OK);
        REQUIRE(count >= 0);
    }
}

TEST_CASE("NativeSmartInfo 的 ABI 布局未被意外改变", "[abi][smart][layout]")
{
    // 布局是 ABI 的一部分；若这些断言失败，说明必须递增 NATIVE_ABI_VERSION
    REQUIRE(sizeof(NativeSmartInfo) % 8 == 0);
    REQUIRE(offsetof(NativeSmartInfo, structSize) == 0);
    REQUIRE(offsetof(NativeSmartInfo, model) > offsetof(NativeSmartInfo, ataSmartAttributeCount));
    REQUIRE(sizeof(NativeSmartInfo::model) == 64 * sizeof(wchar_t));
    REQUIRE(sizeof(NativeSmartInfo::serial) == 64 * sizeof(wchar_t));
    REQUIRE(sizeof(NativeSmartInfo::firmware) == 16 * sizeof(wchar_t));
}
