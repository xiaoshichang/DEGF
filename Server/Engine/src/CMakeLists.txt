file(GLOB_RECURSE DE_SERVER_SOURCE_FILES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/*.h"
)

add_executable(DEServer
    ${DE_SERVER_SOURCE_FILES}
)

target_link_libraries(DEServer
    PUBLIC
        de::thirdparty::boost_asio
        de::thirdparty::boost_json
        de::thirdparty::boost_log
        de::thirdparty::kcp
        de::thirdparty::nethost
        de::thirdparty::zeromq
)

if (WIN32)
    target_link_libraries(DEServer PUBLIC Psapi)
endif()

target_compile_features(DEServer PUBLIC cxx_std_17)
target_include_directories(DEServer
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}"
)
target_include_directories(DEServer
    PUBLIC
        "${PROJECT_SOURCE_DIR}/3rd"
)
