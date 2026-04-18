#include "engine/engine_core.h"

#include <iostream>

int main() {
    const auto snapshot = engine::CollectDependencySnapshot("main", 7001, 42);

    std::cout << "Engine started." << std::endl;
    std::cout << "Greeting JSON: " << snapshot.greeting_json << std::endl;
    std::cout << "Loopback endpoint: " << snapshot.loopback_endpoint << std::endl;
    std::cout << "ZeroMQ version: " << snapshot.zero_mq_version << std::endl;
    std::cout << "KCP conversation: " << snapshot.kcp_conversation << std::endl;
    std::cout << "nethost parameter size: " << snapshot.dotnet_host_parameter_size << std::endl;
    return 0;
}
