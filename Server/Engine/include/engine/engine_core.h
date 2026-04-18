#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

namespace engine {

struct DependencySnapshot {
    std::string greeting_json;
    std::string loopback_endpoint;
    std::string log_line;
    std::string zero_mq_version;
    std::uint32_t kcp_conversation;
    std::size_t dotnet_host_parameter_size;
};

DependencySnapshot CollectDependencySnapshot(
    std::string_view instance_name,
    unsigned short port,
    std::uint32_t kcp_conversation
);

}  // namespace engine
