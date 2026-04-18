add_executable(engine_smoke_tests
    engine_smoke_tests.cpp
)
target_link_libraries(engine_smoke_tests PRIVATE Engine::core)

add_test(NAME engine_smoke_tests COMMAND engine_smoke_tests)
