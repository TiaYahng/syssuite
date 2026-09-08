cmake_minimum_required(VERSION 3.20)
project(SysSuiteNativeTests LANGUAGES CXX)

include(FetchContent)
FetchContent_Declare(
    Catch2
    GIT_REPOSITORY https://github.com/catchorg/Catch2.git
    GIT_TAG v3.7.1
)
FetchContent_MakeAvailable(Catch2)

add_executable(SysSuite.Native.Tests
    tests.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../../src/SysSuite.Native/native_api.cpp
)
target_include_directories(SysSuite.Native.Tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../../src/SysSuite.Native)
target_link_libraries(SysSuite.Native.Tests PRIVATE Catch2::Catch2WithMain)
target_compile_features(SysSuite.Native.Tests PRIVATE cxx_std_20)
if(MSVC)
    target_compile_options(SysSuite.Native.Tests PRIVATE /W4 /WX /analyze /EHsc)
endif()
add_test(NAME SysSuite.Native.Tests COMMAND SysSuite.Native.Tests)
