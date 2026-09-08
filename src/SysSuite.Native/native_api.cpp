#include <cstring>
#include <cwchar>
#include "native_api.h"

extern "C" __declspec(dllexport) int Native_Version(wchar_t* buffer, int length)
{
    if (buffer == nullptr || length < 8) return -1;
    const wchar_t version[] = L"1.0.0-m0";
    const int required = static_cast<int>(std::wcslen(version)) + 1;
    if (length < required) return -2;
    std::memcpy(buffer, version, sizeof(wchar_t) * required);
    return 0;
}
