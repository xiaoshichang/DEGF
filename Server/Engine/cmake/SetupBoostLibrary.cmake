
message(STATUS "Boost Library...")
if (POLICY CMP0144)
    cmake_policy(SET CMP0144 NEW)
endif()

if (POLICY CMP0167)
    cmake_policy(SET CMP0167 NEW)
endif()

if (WIN32)
    add_compile_definitions(_WIN32_WINNT=0x0602)
    set(BOOST_ROOT ${CMAKE_SOURCE_DIR}/3rd/Boost/Bin/Windows64)
    list(PREPEND CMAKE_PREFIX_PATH ${BOOST_ROOT})
else()
endif()

find_package(Boost CONFIG REQUIRED COMPONENTS json log log_setup system)
message("Boost_LIBRARIES: ${Boost_LIBRARIES}")
