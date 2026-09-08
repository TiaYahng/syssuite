cmake_minimum_required(VERSION 3.20)
project(SysSuiteNative LANGUAGES CXX)

add_library(SysSuite.Native SHARED native_api.cpp)
target_compile_features(SysSuite.Native PRIVATE cxx_std_20)
if(MSVC)
    target_compile_options(SysSuite.Native PRIVATE /W4 /EHsc)
endif()
