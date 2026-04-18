cmake_minimum_required(VERSION 3.16)

project(Engine VERSION 0.1.0 LANGUAGES C CXX)

include(CTest)

add_subdirectory(3rd)

add_library(engine_core
    src/engine_core.cpp
)
add_library(Engine::core ALIAS engine_core)

target_compile_features(engine_core PUBLIC cxx_std_17)
target_include_directories(engine_core
    PUBLIC
        "${CMAKE_CURRENT_LIST_DIR}/include"
)
target_link_libraries(engine_core
    PUBLIC
        de::thirdparty::asio
        de::thirdparty::kcp
        de::thirdparty::nethost
        de::thirdparty::nlohmann_json
        de::thirdparty::spdlog
        de::thirdparty::zeromq
)

add_executable(Engine
    src/main.cpp
)
target_link_libraries(Engine PRIVATE Engine::core)

if (BUILD_TESTING)
    add_executable(engine_smoke_tests
        tests/engine_smoke_tests.cpp
    )
    target_link_libraries(engine_smoke_tests PRIVATE Engine::core)
    add_test(NAME engine_smoke_tests COMMAND engine_smoke_tests)
endif()
