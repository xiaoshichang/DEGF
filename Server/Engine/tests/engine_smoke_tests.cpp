#include "config/ClusterConfig.h"
#include "core/BoostAsio.h"
#include "core/Logger.h"
#include "http/HttpService.h"
#include "telnet/TelnetService.h"
#include "timer/TimerManager.h"

#include <nethost.h>
#include <boost/json.hpp>
#include <zmq.h>

#include <chrono>
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

namespace json = boost::json;

void Require(bool condition, const std::string& message) {
    if (!condition) {
        throw std::runtime_error(message);
    }
}

void RunTest(const char* name, void (*test)()) {
    test();
    std::cout << "[PASS] " << name << std::endl;
}

void TestBoostJson() {
    const json::object payload = {
        {"engine", "DEServer"},
        {"protocol", "zeromq"},
        {"port", 7001}
    };

    Require(json::value_to<std::string>(payload.at("engine")) == "DEServer", "Expected JSON engine field.");
    Require(json::serialize(payload).find("\"protocol\":\"zeromq\"") != std::string::npos, "Expected JSON dump output.");
}

void TestAsio() {
    const asio::ip::tcp::endpoint endpoint(asio::ip::make_address_v4("127.0.0.1"), 7001);
    Require(endpoint.address().to_string() == "127.0.0.1", "Expected loopback address.");
    Require(endpoint.port() == 7001, "Expected loopback port.");
}

void TestBoostLog() {
    const auto logRoot = std::filesystem::temp_directory_path() / "degf_engine_log_smoke";
    std::filesystem::remove_all(logRoot);

    de::server::engine::config::LoggingConfig loggingConfig;
    loggingConfig.rootDir = logRoot.string();
    loggingConfig.minLevel = "Info";
    loggingConfig.flushIntervalMs = 1000;
    loggingConfig.enableConsole = false;
    loggingConfig.rotateDaily = false;
    loggingConfig.maxFileSizeMB = 8;
    loggingConfig.maxRetainedFiles = 4;

    de::server::engine::Logger::Init("engine_smoke_tests", loggingConfig);
    de::server::engine::Logger::Info("SmokeTest", "third-party logging ready");

    const auto logFile = logRoot / "engine_smoke_tests.log";
    std::ifstream stream(logFile);
    Require(stream.is_open(), "Expected boost log file to be created.");

    std::ostringstream content;
    content << stream.rdbuf();
    Require(
        content.str().find("third-party logging ready") != std::string::npos,
        "Expected boost log to write to the test file."
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
    "frameworkDll": "../Framework/DE.Server/bin/Debug/net10.0/DE.Server.dll",
    "gameplayDll": "../Framework/Demo.Server/bin/Debug/net10.0/Demo.Server.dll"
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
    "http": {
      "listenEndpoint": {
        "host": "127.0.0.1",
        "port": 5101
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
    Require(
        de::server::engine::config::GetCanonicalGmServerId() == "GM",
        "Expected canonical gm server id to be GM."
    );
    Require(
        de::server::engine::config::IsGmServerId("gm"),
        "Expected lowercase gm id to remain compatible."
    );
    Require(
        de::server::engine::config::IsGmServerId("GM"),
        "Expected uppercase GM id to be recognized."
    );
    Require(
        clusterConfig.managed.frameworkDll == "../Framework/DE.Server/bin/Debug/net10.0/DE.Server.dll",
        "Expected managed framework dll to parse."
    );
    Require(
        clusterConfig.managed.gameplayDll == "../Framework/Demo.Server/bin/Debug/net10.0/Demo.Server.dll",
        "Expected managed gameplay dll to parse."
    );
    Require(clusterConfig.gm.telnet.port == 5200, "Expected gm telnet port to parse.");
    Require(clusterConfig.gm.http.listenEndpoint.port == 5101, "Expected gm http port to parse.");

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

void TestHttpServiceRouting() {
    asio::io_context ioContext;
    de::server::engine::HttpService httpService(
        ioContext,
        "GM",
        []() {
            return std::string(R"({"nodes":{"GM":{"serverId":"GM","workingSetBytes":123}}})");
        }
    );

    const auto performanceResult = httpService.HandleRequest("GET", "/performance");
    Require(performanceResult.statusCode == 200, "Expected /performance to return 200.");
    Require(performanceResult.body.find("\"workingSetBytes\":123") != std::string::npos, "Expected performance payload to be returned.");

    const auto apiPerformanceResult = httpService.HandleRequest("GET", "/api/performance");
    Require(apiPerformanceResult.statusCode == 200, "Expected /api/performance to return 200.");

    const auto missingResult = httpService.HandleRequest("GET", "/missing");
    Require(missingResult.statusCode == 404, "Expected unknown path to return 404.");

    const auto methodNotAllowedResult = httpService.HandleRequest("POST", "/performance");
    Require(methodNotAllowedResult.statusCode == 405, "Expected POST /performance to return 405.");
}

void TestTimerManager() {
    using namespace std::chrono_literals;

    asio::io_context ioContext;
    de::server::engine::TimerManager timerManager(ioContext);

    int oneShotCount = 0;
    int repeatingCount = 0;
    int cancelledCount = 0;

    de::server::engine::TimerManager::TimerID oneShotTimerId = 0;
    oneShotTimerId = timerManager.AddTimer(
        10ms,
        [&](de::server::engine::TimerManager::TimerID timerId) {
            ++oneShotCount;
            Require(timerId == oneShotTimerId, "Expected one-shot timer id to round-trip.");
        }
    );

    de::server::engine::TimerManager::TimerID repeatingTimerId = 0;
    repeatingTimerId = timerManager.AddTimer(
        5ms,
        [&](de::server::engine::TimerManager::TimerID timerId) {
            ++repeatingCount;
            Require(timerId == repeatingTimerId, "Expected repeating timer id to round-trip.");
            if (repeatingCount >= 3) {
                Require(timerManager.CancelTimer(timerId), "Expected repeating timer cancellation to succeed.");
            }
        },
        true
    );

    const auto cancelledTimerId = timerManager.AddTimer(
        30ms,
        [&](de::server::engine::TimerManager::TimerID) {
            ++cancelledCount;
        }
    );
    Require(timerManager.HasTimer(cancelledTimerId), "Expected cancelled timer to be registered.");
    Require(timerManager.CancelTimer(cancelledTimerId), "Expected cancelled timer removal to succeed.");
    Require(!timerManager.HasTimer(cancelledTimerId), "Expected cancelled timer to be gone.");

    asio::steady_timer stopTimer(ioContext);
    stopTimer.expires_after(80ms);
    stopTimer.async_wait([&](const boost::system::error_code&) {
        ioContext.stop();
    });

    ioContext.run();

    Require(oneShotCount == 1, "Expected one-shot timer to fire once.");
    Require(repeatingCount == 3, "Expected repeating timer to fire three times before cancellation.");
    Require(cancelledCount == 0, "Expected cancelled timer callback not to run.");
    Require(!timerManager.HasTimer(oneShotTimerId), "Expected one-shot timer to be removed after firing.");
    Require(!timerManager.HasTimer(repeatingTimerId), "Expected repeating timer to be removed after cancellation.");
    Require(timerManager.CancelAllTimers() == 0, "Expected no timers to remain.");
}

}  // namespace

int main() {
    try {
        RunTest("boost_json", &TestBoostJson);
        RunTest("asio", &TestAsio);
        RunTest("boost_log", &TestBoostLog);
        RunTest("zeromq", &TestZeroMq);
        RunTest("kcp", &TestKcp);
        RunTest("nethost", &TestNethost);
        RunTest("cluster_config_telnet", &TestClusterConfigTelnet);
        RunTest("telnet_command_handling", &TestTelnetCommandHandling);
        RunTest("http_service_routing", &TestHttpServiceRouting);
        RunTest("timer_manager", &TestTimerManager);

        std::cout << "engine_smoke_tests passed" << std::endl;
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "engine_smoke_tests failed: " << exception.what() << std::endl;
        return 1;
    }
}
