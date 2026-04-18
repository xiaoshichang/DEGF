#include "engine/engine_core.h"

#include <nlohmann/json.hpp>

#include <exception>
#include <iostream>
#include <stdexcept>
#include <string>

namespace {

void Require(bool condition, const std::string& message) {
    if (!condition) {
        throw std::runtime_error(message);
    }
}

}  // namespace

int main() {
    try {
        const auto snapshot = engine::CollectDependencySnapshot("smoke-test", 7001, 77);
        const auto json = nlohmann::json::parse(snapshot.greeting_json);

        Require(json.at("engine") == "Engine", "Expected engine name in greeting JSON.");
        Require(json.at("instance") == "smoke-test", "Expected instance name in greeting JSON.");
        Require(json.at("transport") == "zeromq", "Expected transport in greeting JSON.");
        Require(snapshot.loopback_endpoint == "127.0.0.1:7001", "Unexpected loopback endpoint.");
        Require(
            snapshot.log_line.find("dependencies ready") != std::string::npos,
            "Expected spdlog output to contain the readiness message."
        );
        Require(
            snapshot.zero_mq_version.find('.') != std::string::npos,
            "Expected a dotted ZeroMQ version string."
        );
        Require(snapshot.kcp_conversation == 77, "Expected KCP conversation to round-trip.");
        Require(snapshot.dotnet_host_parameter_size > 0, "Expected nethost metadata to be available.");

        std::cout << "engine_smoke_tests passed" << std::endl;
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "engine_smoke_tests failed: " << exception.what() << std::endl;
        return 1;
    }
}
