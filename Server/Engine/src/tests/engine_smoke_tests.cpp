#include <asio/ip/address_v4.hpp>
#include <asio/ip/tcp.hpp>
#include <nethost.h>
#include <nlohmann/json.hpp>
#include <spdlog/logger.h>
#include <spdlog/sinks/ostream_sink.h>
#include <zmq.h>

#include <exception>
#include <iostream>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>

extern "C" {
#include "ikcp.h"
}

namespace {

void Require(bool condition, const std::string& message) {
    if (!condition) {
        throw std::runtime_error(message);
    }
}

void RunTest(const char* name, void (*test)()) {
    test();
    std::cout << "[PASS] " << name << std::endl;
}

void TestNlohmannJson() {
    const nlohmann::json payload = {
        {"engine", "DEServer"},
        {"protocol", "zeromq"},
        {"port", 7001}
    };

    Require(payload.at("engine") == "DEServer", "Expected JSON engine field.");
    Require(payload.dump().find("\"protocol\":\"zeromq\"") != std::string::npos, "Expected JSON dump output.");
}

void TestAsio() {
    const asio::ip::tcp::endpoint endpoint(asio::ip::make_address_v4("127.0.0.1"), 7001);
    Require(endpoint.address().to_string() == "127.0.0.1", "Expected loopback address.");
    Require(endpoint.port() == 7001, "Expected loopback port.");
}

void TestSpdlog() {
    std::ostringstream stream;
    auto sink = std::make_shared<spdlog::sinks::ostream_sink_mt>(stream);
    spdlog::logger logger("engine_smoke_tests", sink);
    logger.set_pattern("[%l] %v");
    logger.info("third-party logging ready");
    logger.flush();

    Require(
        stream.str().find("third-party logging ready") != std::string::npos,
        "Expected spdlog to write to the test stream."
    );
}

void TestZeroMq() {
    int major = 0;
    int minor = 0;
    int patch = 0;
    zmq_version(&major, &minor, &patch);

    Require(major > 0, "Expected a valid ZeroMQ major version.");
    Require(minor >= 0, "Expected a valid ZeroMQ minor version.");
    Require(patch >= 0, "Expected a valid ZeroMQ patch version.");
}

void TestKcp() {
    ikcpcb* control_block = ikcp_create(77, nullptr);
    Require(control_block != nullptr, "Expected KCP control block creation to succeed.");
    Require(control_block->conv == 77, "Expected KCP conversation id to round-trip.");
    ikcp_release(control_block);
}

void TestNethost() {
    auto* get_hostfxr_path_fn = &get_hostfxr_path;
    Require(get_hostfxr_path_fn != nullptr, "Expected nethost entry point to be available.");
    Require(sizeof(get_hostfxr_parameters) > 0, "Expected nethost parameter structure to be defined.");
}

}  // namespace

int main() {
    try {
        RunTest("nlohmann_json", &TestNlohmannJson);
        RunTest("asio", &TestAsio);
        RunTest("spdlog", &TestSpdlog);
        RunTest("zeromq", &TestZeroMq);
        RunTest("kcp", &TestKcp);
        RunTest("nethost", &TestNethost);

        std::cout << "engine_smoke_tests passed" << std::endl;
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "engine_smoke_tests failed: " << exception.what() << std::endl;
        return 1;
    }
}
