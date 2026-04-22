cmake_minimum_required(VERSION 3.16)

project(Engine VERSION 0.1.0 LANGUAGES C CXX)

include(cmake/SetupBoostLibrary.cmake)
include(CTest)

add_subdirectory(3rd)
add_subdirectory(src)

if (BUILD_TESTING)
    add_subdirectory(tests)
endif()
