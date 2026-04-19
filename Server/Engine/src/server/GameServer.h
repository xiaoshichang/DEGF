#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"

namespace de::server::engine
{
	class GameServer : public ServerBase
	{
	public:
		GameServer(std::string serverId, config::GameConfig config);
		~GameServer() override;
		void Init() override;
		void Uninit() override;

	private:
		config::GameConfig config_;
	};
}
