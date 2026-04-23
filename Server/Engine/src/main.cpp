#include "config/ClusterConfig.h"
#include "core/Logger.h"
#include "server/game/GameServer.h"
#include "server/gate/GateServer.h"
#include "server/gm/GMServer.h"

#include <exception>
#include <iostream>
#include <memory>
#include <string>

namespace
{
	std::unique_ptr<de::server::engine::ServerBase> CreateServer(
		const de::server::engine::config::ClusterConfig& clusterConfig,
		const std::string& configPath,
		const std::string& serverId
	)
	{
		using namespace de::server::engine;

		if (config::IsGmServerId(serverId))
		{
			return std::make_unique<GMServer>(serverId, configPath, clusterConfig);
		}

		if (const auto* gateConfig = config::FindGateConfig(clusterConfig, serverId))
		{
			(void)gateConfig;
			return std::make_unique<GateServer>(serverId, configPath, clusterConfig);
		}

		if (const auto* gameConfig = config::FindGameConfig(clusterConfig, serverId))
		{
			(void)gameConfig;
			return std::make_unique<GameServer>(serverId, configPath, clusterConfig);
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
		std::string serverId = argv[2];
		if (de::server::engine::config::IsGmServerId(serverId))
		{
			serverId = std::string(de::server::engine::config::GetCanonicalGmServerId());
		}

		const auto clusterConfig = de::server::engine::config::LoadClusterConfig(configPath);
		de::server::engine::Logger::Init(serverId, clusterConfig.logging);

		auto server = CreateServer(clusterConfig, configPath, serverId);
		if (server == nullptr)
		{
			de::server::engine::Logger::Error("Main", "Unknown server-id: " + serverId);
			return 1;
		}

		de::server::engine::Logger::Info("Main", "Starting server.");
		server->Init();
		server->Run();
		server->Uninit();
		de::server::engine::Logger::Info("Main", "Server stopped.");
		return 0;
	}
	catch (const std::exception& exception)
	{
		std::cerr << "Failed to start DEServer: " << exception.what() << std::endl;
		return 1;
	}
}
