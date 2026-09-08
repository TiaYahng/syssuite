#include <catch2/catch_test_macros.hpp>
#include <cwchar>
#include "native_api.h"

TEST_CASE("Native_Version writes a null-terminated version string")
{
    wchar_t buffer[32]{};
    REQUIRE(Native_Version(buffer, 32) == 0);
    REQUIRE(std::wcscmp(buffer, L"1.0.0-m0") == 0);
}

TEST_CASE("Native_Version rejects null and undersized buffers")
{
    wchar_t buffer[2]{};
    REQUIRE(Native_Version(nullptr, 32) == -1);
    REQUIRE(Native_Version(buffer, 2) == -1);
}
