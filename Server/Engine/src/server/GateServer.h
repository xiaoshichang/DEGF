#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"

namespace de::server::engine
{
	class GateServer : public ServerBase
	{
	public:
		GateServer(std::string serverId, const config::ClusterConfig& clusterConfig);
		~GateServer() override;
		void Init() override;
		void Uninit() override;

	private:
		const config::TelnetConfig& GetTelnetConfig() const override;

		config::GateConfig config_;
	};
}
