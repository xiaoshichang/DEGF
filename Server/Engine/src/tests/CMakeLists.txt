add_executable(engine_smoke_tests
    engine_smoke_tests.cpp
)
target_compile_features(engine_smoke_tests PRIVATE cxx_std_17)
target_link_libraries(engine_smoke_tests
    PRIVATE
        de::thirdparty::asio
        de::thirdparty::kcp
        de::thirdparty::nethost
        de::thirdparty::nlohmann_json
        de::thirdparty::spdlog
        de::thirdparty::zeromq
)

if (WIN32)
    add_custom_command(
        TARGET engine_smoke_tests
        POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${PROJECT_SOURCE_DIR}/3rd/dotnet_host/win-x64/nethost.dll"
            "$<TARGET_FILE_DIR:engine_smoke_tests>/nethost.dll"
    )
endif()

add_test(NAME engine_smoke_tests COMMAND engine_smoke_tests)
