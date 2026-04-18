#include "GameServer.h"

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
	}

	void GameServer::Run()
	{
	}

	void GameServer::Uninit()
	{
	}
}
