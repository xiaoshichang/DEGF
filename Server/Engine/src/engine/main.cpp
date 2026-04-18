#include "config/ClusterConfig.h"
#include "server/GameServer.h"
#include "server/GateServer.h"
#include "server/GMServer.h"

#include <exception>
#include <iostream>
#include <memory>
#include <string>

namespace
{
	std::unique_ptr<de::server::engine::ServerBase> CreateServer(
		const de::server::engine::config::ClusterConfig& clusterConfig,
		const std::string& serverId
	)
	{
		using namespace de::server::engine;

		if (config::IsGmServerId(serverId))
		{
			return std::make_unique<GMServer>(serverId, clusterConfig.gm);
		}

		if (const auto* gateConfig = config::FindGateConfig(clusterConfig, serverId))
		{
			return std::make_unique<GateServer>(serverId, *gateConfig);
		}

		if (const auto* gameConfig = config::FindGameConfig(clusterConfig, serverId))
		{
			return std::make_unique<GameServer>(serverId, *gameConfig);
		}

		return nullptr;
	}
}

int main(int argc, char* argv[])
{
	if (argc != 3)
	{
		std::cerr << "Usage: DEServer.exe {path to config} {server-id}" << std::endl;
		return 1;
	}

	try
	{
		const std::string configPath = argv[1];
		const std::string serverId = argv[2];
		const auto clusterConfig = de::server::engine::config::LoadClusterConfig(configPath);

		auto server = CreateServer(clusterConfig, serverId);
		if (server == nullptr)
		{
			std::cerr << "Unknown server-id: " << serverId << std::endl;
			return 1;
		}

		server->Init();
		server->Run();
		server->Uninit();
		return 0;
	}
	catch (const std::exception& exception)
	{
		std::cerr << "Failed to start DEServer: " << exception.what() << std::endl;
		return 1;
	}
}
