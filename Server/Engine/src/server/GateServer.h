#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"

namespace de::server::engine
{
	class GateServer : public ServerBase
	{
	public:
		GateServer(std::string serverId, config::GateConfig config);
		~GateServer() override;
		void Init() override;
		void Uninit() override;

	private:
		config::GateConfig config_;
	};
}
