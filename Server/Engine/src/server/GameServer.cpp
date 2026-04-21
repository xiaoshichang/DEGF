#include "GameServer.h"

#include "core/Logger.h"

#include <stdexcept>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		config::GameConfig ResolveGameConfig(const config::ClusterConfig& clusterConfig, std::string_view serverId)
		{
			const auto* gameConfig = config::FindGameConfig(clusterConfig, serverId);
			if (gameConfig == nullptr)
			{
				throw std::invalid_argument("Game config not found for server-id: " + std::string(serverId));
			}

			return *gameConfig;
		}
	}

	GameServer::GameServer(std::string serverId, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId)
		, config_(ResolveGameConfig(clusterConfig, serverId))
	{
	}

	GameServer::~GameServer()
	{
	}

	void GameServer::Init()
	{
		ServerBase::Init();
		Logger::Info("GameServer", "Init");
	}

	void GameServer::Uninit()
	{
		Logger::Info("GameServer", "Uninit");
		ServerBase::Uninit();
	}

	const config::TelnetConfig& GameServer::GetTelnetConfig() const
	{
		return config_.telnet;
	}
}
