add_executable(engine_smoke_tests
    engine_smoke_tests.cpp
    "${PROJECT_SOURCE_DIR}/src/config/ClusterConfig.cpp"
    "${PROJECT_SOURCE_DIR}/src/core/Logger.cpp"
    "${PROJECT_SOURCE_DIR}/src/http/HttpService.cpp"
    "${PROJECT_SOURCE_DIR}/src/telnet/TelnetService.cpp"
    "${PROJECT_SOURCE_DIR}/src/timer/TimerManager.cpp"
)
target_compile_features(engine_smoke_tests PRIVATE cxx_std_17)
target_link_libraries(engine_smoke_tests
    PRIVATE
        de::thirdparty::boost_asio
        de::thirdparty::boost_json
        de::thirdparty::boost_log
        de::thirdparty::kcp
        de::thirdparty::nethost
        de::thirdparty::zeromq
)
target_include_directories(engine_smoke_tests
    PRIVATE
        "${PROJECT_SOURCE_DIR}/src"
        "${PROJECT_SOURCE_DIR}/3rd"
)

if (WIN32)
    target_link_libraries(engine_smoke_tests PRIVATE Psapi)
endif()

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
