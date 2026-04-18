cmake_minimum_required(VERSION 3.16)

project(Engine VERSION 0.1.0 LANGUAGES C CXX)

include(CTest)

add_subdirectory(3rd)
add_subdirectory(src/engine)

if (BUILD_TESTING)
    add_subdirectory(src/tests)
endif()
