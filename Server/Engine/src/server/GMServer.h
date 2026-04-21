#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"

namespace de::server::engine
{
	class GMServer : public ServerBase
	{
	public:
		GMServer(std::string serverId, const config::ClusterConfig& clusterConfig);
		~GMServer() override;
		void Init() override;
		void Uninit() override;

	private:
		const config::TelnetConfig& GetTelnetConfig() const override;

		config::GMConfig config_;
	};
}
