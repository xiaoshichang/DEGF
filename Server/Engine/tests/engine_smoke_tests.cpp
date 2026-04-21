#include "config/ClusterConfig.h"
#include "telnet/TelnetService.h"

#include <asio/ip/address_v4.hpp>
#include <asio/ip/tcp.hpp>
#include <nethost.h>
#include <nlohmann/json.hpp>
#include <spdlog/logger.h>
#include <spdlog/sinks/ostream_sink.h>
#include <zmq.h>

#include <exception>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>

#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#endif

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

void TestClusterConfigTelnet() {
    const auto tempPath = std::filesystem::temp_directory_path() / "degf_engine_telnet_config_test.json";
    const char* json = R"json({
  "env": {
    "id": "test-env",
    "environment": "dev"
  },
  "logging": {
    "rootDir": "logs",
    "minLevel": "Info",
    "flushIntervalMs": 1000,
    "enableConsole": true,
    "rotateDaily": true,
    "maxFileSizeMB": 64,
    "maxRetainedFiles": 10
  },
  "kcp": {
    "mtu": 1200,
    "sndwnd": 128,
    "rcvwnd": 128,
    "nodelay": true,
    "intervalMs": 10,
    "fastResend": 2,
    "noCongestionWindow": false,
    "minRtoMs": 30,
    "deadLinkCount": 20,
    "streamMode": false
  },
  "managed": {
    "assemblyName": "Managed",
    "assemblyPath": "Managed.dll",
    "runtimeConfigPath": "Managed.runtimeconfig.json",
    "searchAssemblyPaths": [ "Managed.dll" ]
  },
  "gm": {
    "innerNetwork": {
      "listenEndpoint": {
        "host": "127.0.0.1",
        "port": 5000
      }
    },
    "controlNetwork": {
      "listenEndpoint": {
        "host": "127.0.0.1",
        "port": 5100
      }
    },
    "telnet": {
      "port": 5200
    }
  },
  "gate": {
    "Gate0": {
      "innerNetwork": {
        "listenEndpoint": {
          "host": "127.0.0.1",
          "port": 7000
        }
      },
      "authNetwork": {
        "listenEndpoint": {
          "host": "0.0.0.0",
          "port": 4100
        }
      },
      "clientNetwork": {
        "listenEndpoint": {
          "host": "0.0.0.0",
          "port": 4000
        }
      },
      "telnet": {
        "port": 5201
      }
    }
  },
  "game": {
    "Game0": {
      "innerNetwork": {
        "listenEndpoint": {
          "host": "127.0.0.1",
          "port": 7100
        }
      },
      "telnet": {
        "port": 5300
      }
    }
  }
})json";

    {
        std::ofstream stream(tempPath);
        Require(stream.is_open(), "Expected temp config file to open.");
        stream << json;
    }

    const auto clusterConfig = de::server::engine::config::LoadClusterConfig(tempPath.string());
    Require(clusterConfig.gm.telnet.port == 5200, "Expected gm telnet port to parse.");

    const auto* gateConfig = de::server::engine::config::FindGateConfig(clusterConfig, "Gate0");
    Require(gateConfig != nullptr, "Expected Gate0 config to be present.");
    Require(gateConfig->telnet.port == 5201, "Expected gate telnet port to parse.");

    const auto* gameConfig = de::server::engine::config::FindGameConfig(clusterConfig, "Game0");
    Require(gameConfig != nullptr, "Expected Game0 config to be present.");
    Require(gameConfig->telnet.port == 5300, "Expected game telnet port to parse.");

    std::filesystem::remove(tempPath);
}

void TestTelnetCommandHandling() {
    asio::io_context ioContext;
    de::server::engine::TelnetService telnetService(ioContext, "Gate0");

    const auto helpResult = telnetService.HandleCommand("help");
    Require(helpResult.response.find("metrics") != std::string::npos, "Expected help command to mention metrics.");
    Require(!helpResult.closeSession, "Expected help command to keep the session open.");

    const auto statusResult = telnetService.HandleCommand("status");
    Require(statusResult.response.find("serverId: Gate0") != std::string::npos, "Expected status command to include server id.");

    const auto pidResult = telnetService.HandleCommand("pid");
#ifdef _WIN32
    const auto expectedPid = std::to_string(static_cast<unsigned long>(::GetCurrentProcessId()));
#else
    const auto expectedPid = std::to_string(static_cast<long>(::getpid()));
#endif
    Require(pidResult.response.find(expectedPid) != std::string::npos, "Expected pid command to include the current process id.");

    const auto metricsResult = telnetService.HandleCommand("metrics");
    Require(metricsResult.response.find("workingSetMB:") != std::string::npos, "Expected metrics command to include memory usage.");
    Require(metricsResult.response.find("totalCommands: 4") != std::string::npos, "Expected metrics command to reflect handled commands.");

    const auto stopResult = telnetService.HandleCommand("stop");
    Require(stopResult.closeSession, "Expected stop command to close the telnet session.");
    Require(stopResult.stopServer, "Expected stop command to request server shutdown.");

    const auto unknownResult = telnetService.HandleCommand("unknown-command");
    Require(unknownResult.response.find("unknown command") != std::string::npos, "Expected unknown command message.");
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
        RunTest("cluster_config_telnet", &TestClusterConfigTelnet);
        RunTest("telnet_command_handling", &TestTelnetCommandHandling);

        std::cout << "engine_smoke_tests passed" << std::endl;
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "engine_smoke_tests failed: " << exception.what() << std::endl;
        return 1;
    }
}
