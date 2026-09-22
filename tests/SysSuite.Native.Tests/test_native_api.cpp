// ABI 契约测试（Catch2）。与 docs/native-abi.md §8 的四类路径一一对应。
// 本机通常缺少 CMake / MSVC，由 CI 的 native job 执行。
#include <catch2/catch_test_macros.hpp>

#include "native_api.h"

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
