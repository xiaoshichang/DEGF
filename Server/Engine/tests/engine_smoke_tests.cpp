#include "config/ClusterConfig.h"
#include "core/BoostAsio.h"
#include "core/Logger.h"
#include "http/HttpService.h"
#include "network/client/ClientNetwork.h"
#include "network/protocal/Header.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"
#include "server/gate/GateHttpHandler.h"
#include "telnet/TelnetService.h"
#include "timer/TimerManager.h"

#include <nethost.h>
#include <boost/json.hpp>
#include <zmq.h>

#include <chrono>
#include <cstring>
#include <exception>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

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
    loggingConfig.enableFile = false;
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
    "EnableFile": true,
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
        [](const de::server::engine::HttpRequest& request) {
            if (request.method != "GET") {
                return de::server::engine::HttpResponse{
                    405,
                    "Method Not Allowed",
                    "application/json; charset=utf-8",
                    R"({"error":"method not allowed"})"
                };
            }

            if (request.target == "/performance" || request.target == "/api/performance") {
                return de::server::engine::HttpResponse{
                    200,
                    "OK",
                    "application/json; charset=utf-8",
                    R"({"nodes":{"GM":{"serverId":"GM","workingSetBytes":123}}})"
                };
            }

            return de::server::engine::HttpResponse{
                404,
                "Not Found",
                "application/json; charset=utf-8",
                R"({"error":"not found"})"
            };
        }
    );

    const auto performanceResult = httpService.HandleRequest({"GET", "/performance", "HTTP/1.1", ""});
    Require(performanceResult.statusCode == 200, "Expected /performance to return 200.");
    Require(performanceResult.body.find("\"workingSetBytes\":123") != std::string::npos, "Expected performance payload to be returned.");

    const auto apiPerformanceResult = httpService.HandleRequest({"GET", "/api/performance", "HTTP/1.1", ""});
    Require(apiPerformanceResult.statusCode == 200, "Expected /api/performance to return 200.");

    const auto missingResult = httpService.HandleRequest({"GET", "/missing", "HTTP/1.1", ""});
    Require(missingResult.statusCode == 404, "Expected unknown path to return 404.");

    const auto methodNotAllowedResult = httpService.HandleRequest({"POST", "/performance", "HTTP/1.1", ""});
    Require(methodNotAllowedResult.statusCode == 405, "Expected POST /performance to return 405.");
}

void TestGateHttpHandlerAuth() {
    bool allocateCalled = false;
    de::server::engine::GateHttpHandler handler(
        "Gate0",
        4000,
        []() {
            return true;
        },
        [](const std::string& account, const std::string& password) {
            if (account.empty() || password.empty()) {
                return de::server::engine::GateAuthValidationResult{
                    false,
                    401,
                    "invalid account or password",
                    ""
                };
            }

            if (account == "test") {
                return de::server::engine::GateAuthValidationResult{
                    false,
                    403,
                    "account routed to another gate",
                    "Gate1"
                };
            }

            return de::server::engine::GateAuthValidationResult{
                true,
                200,
                "",
                ""
            };
        },
        [&]() -> std::optional<de::server::engine::network::AllocatedClientSession> {
            allocateCalled = true;
            return de::server::engine::network::AllocatedClientSession{
                100,
                9527
            };
        }
    );

    const auto response = handler.HandleRequest({
        "POST",
        "/auth",
        "HTTP/1.1",
        R"({"account":"demo","password":"demo"})"
    });
    Require(allocateCalled, "Expected auth request to allocate client session.");
    Require(response.statusCode == 200, "Expected auth request to succeed.");
    Require(response.body.find("\"sessionId\":100") != std::string::npos, "Expected auth response to contain session id.");
    Require(response.body.find("\"conv\":9527") != std::string::npos, "Expected auth response to contain conv.");
    Require(response.body.find("\"clientPort\":4000") != std::string::npos, "Expected auth response to contain client port.");

    allocateCalled = false;
    const auto wrongGateResponse = handler.HandleRequest({
        "POST",
        "/auth",
        "HTTP/1.1",
        R"({"account":"test","password":"demo"})"
    });
    Require(!allocateCalled, "Expected mismatched gate auth request not to allocate client session.");
    Require(wrongGateResponse.statusCode == 403, "Expected mismatched gate auth request to be rejected.");
    Require(
        wrongGateResponse.body.find("\"expectedServerId\":\"Gate1\"") != std::string::npos,
        "Expected mismatched gate auth response to indicate the selected gate."
    );
}

struct TestClientKcpOutputContext {
    asio::ip::udp::socket* socket = nullptr;
    asio::ip::udp::endpoint remoteEndpoint;
    int sentCount = 0;
};

int TestClientKcpOutput(const char* buffer, int length, ikcpcb* kcp, void* user) {
    (void)kcp;

    if (buffer == nullptr || length <= 0 || user == nullptr) {
        return -1;
    }

    auto* context = static_cast<TestClientKcpOutputContext*>(user);
    boost::system::error_code error;
    const auto sent = context->socket->send_to(
        asio::buffer(buffer, static_cast<std::size_t>(length)),
        context->remoteEndpoint,
        0,
        error
    );
    if (error) {
        return -1;
    }

    ++context->sentCount;
    return static_cast<int>(sent);
}

void ConfigureTestKcp(ikcpcb* kcp, const de::server::engine::config::KcpConfig& config) {
    Require(kcp != nullptr, "Expected valid kcp instance.");
    ikcp_setmtu(kcp, config.mtu);
    ikcp_wndsize(kcp, config.sndwnd, config.rcvwnd);
    ikcp_nodelay(
        kcp,
        config.nodelay ? 1 : 0,
        config.intervalMs,
        config.fastResend,
        config.noCongestionWindow ? 1 : 0
    );
    kcp->rx_minrto = static_cast<IUINT32>(config.minRtoMs);
    kcp->dead_link = static_cast<IUINT32>(config.deadLinkCount);
    kcp->stream = config.streamMode ? 1 : 0;
}

void TestClientNetworkHandshake() {
    using namespace std::chrono_literals;

    asio::io_context ioContext;
    de::server::engine::config::KcpConfig kcpConfig;
    kcpConfig.mtu = 1200;
    kcpConfig.sndwnd = 128;
    kcpConfig.rcvwnd = 128;
    kcpConfig.nodelay = true;
    kcpConfig.intervalMs = 10;
    kcpConfig.fastResend = 2;
    kcpConfig.noCongestionWindow = false;
    kcpConfig.minRtoMs = 30;
    kcpConfig.deadLinkCount = 20;
    kcpConfig.streamMode = false;

    bool connected = false;
    de::server::engine::network::ClientNetwork::SessionId connectedSessionId = 0;
    ikcpcb* clientKcp = nullptr;

    de::server::engine::network::ClientNetwork clientNetwork(
        ioContext,
        kcpConfig,
        {
            [&](de::server::engine::network::ClientNetwork::SessionId sessionId) {
                connected = true;
                connectedSessionId = sessionId;
                ioContext.stop();
            },
            [&](de::server::engine::network::ClientNetwork::SessionId sessionId, std::uint32_t messageId, const std::vector<std::byte>& data) {
                (void)sessionId;
                (void)messageId;
                (void)data;
            },
            [&](de::server::engine::network::ClientNetwork::SessionId) {
            }
        }
    );

    de::server::engine::config::NetworkConfig listenConfig;
    listenConfig.listenEndpoint.host = "127.0.0.1";
    listenConfig.listenEndpoint.port = 0;
    Require(clientNetwork.Listen(listenConfig), "Expected client network listen to succeed.");
    Require(clientNetwork.GetListenPort() != 0, "Expected client network to bind an actual port.");

    const auto allocatedSession = clientNetwork.AllocateSession();
    Require(allocatedSession.has_value(), "Expected client network to allocate a session.");

    asio::ip::udp::socket clientSocket(ioContext);
    clientSocket.open(asio::ip::udp::v4());
    const asio::ip::udp::endpoint serverEndpoint(asio::ip::make_address_v4("127.0.0.1"), clientNetwork.GetListenPort());

    TestClientKcpOutputContext outputContext;
    outputContext.socket = &clientSocket;
    outputContext.remoteEndpoint = serverEndpoint;
    clientKcp = ikcp_create(allocatedSession->conv, &outputContext);
    Require(clientKcp != nullptr, "Expected client kcp to be created.");
    ConfigureTestKcp(clientKcp, kcpConfig);
    ikcp_setoutput(clientKcp, &TestClientKcpOutput);

    const de::server::engine::network::ClientHandShakeMessage handShakeMessage{
        de::server::engine::network::ClientHandShakeMessage::kCurrentVersion,
        0,
        allocatedSession->sessionId
    };
    const auto handShakeBytes = handShakeMessage.Serialize();
    const auto handShakeHeader = de::server::engine::network::Header::CreateClient(
        static_cast<std::uint32_t>(de::server::engine::network::MessageID::CS::HandShakeReq),
        static_cast<std::uint32_t>(handShakeBytes.size())
    );
    const auto serializedHandShakeHeader = handShakeHeader.Serialize();
    std::vector<std::byte> handShakeFrame(serializedHandShakeHeader.size() + handShakeBytes.size());
    std::memcpy(handShakeFrame.data(), serializedHandShakeHeader.data(), serializedHandShakeHeader.size());
    for (std::size_t index = 0; index < handShakeBytes.size(); ++index) {
        handShakeFrame[serializedHandShakeHeader.size() + index] = static_cast<std::byte>(handShakeBytes[index]);
    }

    Require(
        ikcp_send(clientKcp, reinterpret_cast<const char*>(handShakeFrame.data()), static_cast<int>(handShakeFrame.size())) >= 0,
        "Expected test client handshake send to succeed."
    );
    for (std::uint32_t current = 0; current <= 50; current += 10) {
        ikcp_update(clientKcp, current);
    }
    Require(outputContext.sentCount > 0, "Expected test client kcp to flush at least one UDP packet.");

    asio::steady_timer stopTimer(ioContext);
    stopTimer.expires_after(200ms);
    stopTimer.async_wait([&](const boost::system::error_code&) {
        ioContext.stop();
    });

    ioContext.run();

    ikcp_release(clientKcp);

    Require(connected, "Expected client network to establish a session.");
    Require(connectedSessionId == allocatedSession->sessionId, "Expected client network session id to match allocated session.");
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
        RunTest("gate_http_handler_auth", &TestGateHttpHandlerAuth);
        RunTest("client_network_handshake", &TestClientNetworkHandshake);
        RunTest("timer_manager", &TestTimerManager);

        std::cout << "engine_smoke_tests passed" << std::endl;
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "engine_smoke_tests failed: " << exception.what() << std::endl;
        return 1;
    }
}
