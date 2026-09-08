#pragma once

#ifdef _WIN32
#define SYS_SUITE_NATIVE_EXPORT extern "C" __declspec(dllexport)
#else
#define SYS_SUITE_NATIVE_EXPORT extern "C"
#endif

SYS_SUITE_NATIVE_EXPORT int Native_Version(wchar_t* buffer, int length);
