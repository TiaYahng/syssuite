cmake_minimum_required(VERSION 3.20)

project(SysSuiteNativeTests LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "SysSuite.Native.Tests 仅支持 Windows（x64）")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# 统一输出到单一目录，保证测试可执行文件能找到同目录的 SysSuite.Native.dll
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")

add_subdirectory(
  ${CMAKE_CURRENT_SOURCE_DIR}/../../src/SysSuite.Native
  ${CMAKE_BINARY_DIR}/SysSuite.Native
)

include(FetchContent)
FetchContent_Declare(
  Catch2
  GIT_REPOSITORY https://github.com/catchorg/Catch2.git
  GIT_TAG v3.4.0
  GIT_SHALLOW TRUE
)
FetchContent_MakeAvailable(Catch2)

add_executable(SysSuite.Native.Tests test_native_api.cpp)
target_link_libraries(SysSuite.Native.Tests PRIVATE SysSuite.Native Catch2::Catch2WithMain)

if(MSVC)
  target_compile_options(SysSuite.Native.Tests PRIVATE /W4 /utf-8 /EHsc)
else()
  target_compile_options(SysSuite.Native.Tests PRIVATE -Wall -Wextra)
endif()

enable_testing()
list(APPEND CMAKE_MODULE_PATH "${catch2_SOURCE_DIR}/extras")
include(Catch)
catch_discover_tests(SysSuite.Native.Tests)
