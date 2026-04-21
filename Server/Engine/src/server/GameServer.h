#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"

namespace de::server::engine
{
	class GameServer : public ServerBase
	{
	public:
		GameServer(std::string serverId, const config::ClusterConfig& clusterConfig);
		~GameServer() override;
		void Init() override;
		void Uninit() override;

	private:
		const config::TelnetConfig& GetTelnetConfig() const override;

		config::GameConfig config_;
	};
}
