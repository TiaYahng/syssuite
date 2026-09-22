cmake_minimum_required(VERSION 3.20)

project(SysSuiteNative LANGUAGES CXX)

# 契约：SysSuite.Native 仅支持 Windows x64（见 app.manifest 与 docs/native-abi.md）
if(NOT WIN32)
  message(FATAL_ERROR "SysSuite.Native 仅支持 Windows 平台")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "SysSuite.Native 仅支持 x64；请勿使用 Win32 生成器")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(SysSuite.Native SHARED
  src/native_api.cpp
)

target_include_directories(SysSuite.Native PUBLIC
  ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_definitions(SysSuite.Native PRIVATE SYSSUITE_NATIVE_EXPORTS)

set_target_properties(SysSuite.Native PROPERTIES
  OUTPUT_NAME "SysSuite.Native"
  WINDOWS_EXPORT_ALL_SYMBOLS OFF
)

if(MSVC)
  # /W4 全开；刻意不启用 /WX —— 编译器版本差异不应让 CI 因新告警直接红
  target_compile_options(SysSuite.Native PRIVATE /W4 /utf-8 /EHsc)
else()
  target_compile_options(SysSuite.Native PRIVATE -Wall -Wextra)
endif()
