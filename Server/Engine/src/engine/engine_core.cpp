#include "engine/engine_core.h"

#include <asio/ip/address_v4.hpp>
#include <asio/ip/tcp.hpp>
#include <nethost.h>
#include <nlohmann/json.hpp>
#include <spdlog/logger.h>
#include <spdlog/sinks/ostream_sink.h>
#include <zmq.h>

#include <memory>
#include <ostream>
#include <sstream>
#include <string>

extern "C" {
#include "ikcp.h"
}

namespace engine {
namespace {

std::string BuildLogLine(std::string_view category, std::string_view message) {
    std::ostringstream stream;
    auto sink = std::make_shared<spdlog::sinks::ostream_sink_mt>(stream);
    spdlog::logger logger("engine_dependency_logger", sink);
    logger.set_pattern("[%l] %v");
    logger.info("{}: {}", category, message);
    logger.flush();
    return stream.str();
}

std::string GetZeroMqVersion() {
    int major = 0;
    int minor = 0;
    int patch = 0;
    zmq_version(&major, &minor, &patch);
    return std::to_string(major) + "." + std::to_string(minor) + "." + std::to_string(patch);
}

std::uint32_t CreateKcpConversation(std::uint32_t conversation) {
    ikcpcb* control_block = ikcp_create(conversation, nullptr);
    if (control_block == nullptr) {
        return 0;
    }

    const std::uint32_t resolved = control_block->conv;
    ikcp_release(control_block);
    return resolved;
}

}  // namespace

DependencySnapshot CollectDependencySnapshot(
    std::string_view instance_name,
    unsigned short port,
    std::uint32_t kcp_conversation
) {
    const nlohmann::json greeting = {
        {"engine", "Engine"},
        {"instance", instance_name},
        {"transport", "zeromq"}
    };

    const asio::ip::tcp::endpoint endpoint(asio::ip::make_address_v4("127.0.0.1"), port);

    return DependencySnapshot{
        greeting.dump(),
        endpoint.address().to_string() + ":" + std::to_string(endpoint.port()),
        BuildLogLine("engine", "dependencies ready"),
        GetZeroMqVersion(),
        CreateKcpConversation(kcp_conversation),
        sizeof(get_hostfxr_parameters)
    };
}

}  // namespace engine
