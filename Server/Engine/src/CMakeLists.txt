file(GLOB_RECURSE DE_SERVER_SOURCE_FILES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/*.h"
)

add_executable(DEServer
    ${DE_SERVER_SOURCE_FILES}
)

target_link_libraries(DEServer
    PUBLIC
        de::thirdparty::asio
        de::thirdparty::kcp
        de::thirdparty::nethost
        de::thirdparty::nlohmann_json
        de::thirdparty::spdlog
        de::thirdparty::zeromq
)

target_compile_features(DEServer PUBLIC cxx_std_17)
target_include_directories(DEServer
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}"
)
