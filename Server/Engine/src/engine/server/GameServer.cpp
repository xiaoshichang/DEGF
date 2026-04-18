#include "GameServer.h"

#include "core/Logger.h"

#include <utility>

namespace de::server::engine
{
	GameServer::GameServer(std::string serverId, config::GameConfig config)
		: ServerBase(std::move(serverId))
		, config_(std::move(config))
	{
	}

	GameServer::~GameServer()
	{
	}

	void GameServer::Init()
	{
		Logger::Info("GameServer", "Init");
	}

	void GameServer::Run()
	{
		Logger::Info("GameServer", "Run");
	}

	void GameServer::Uninit()
	{
		Logger::Info("GameServer", "Uninit");
	}
}
