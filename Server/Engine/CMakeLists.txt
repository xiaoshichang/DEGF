cmake_minimum_required(VERSION 3.16)

project(Engine VERSION 0.1.0 LANGUAGES CXX)

add_executable(Engine
    src/main.cpp
)

target_compile_features(Engine PRIVATE cxx_std_17)
