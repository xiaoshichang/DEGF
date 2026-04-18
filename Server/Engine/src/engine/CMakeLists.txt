add_library(engine_core
    engine_core.cpp
)
add_library(Engine::core ALIAS engine_core)

target_compile_features(engine_core PUBLIC cxx_std_17)
target_include_directories(engine_core
    PUBLIC
        "${CMAKE_CURRENT_LIST_DIR}/.."
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
    main.cpp
)
target_link_libraries(Engine PRIVATE Engine::core)
