#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"

namespace de::server::engine
{
	class GMServer : public ServerBase
	{
	public:
		GMServer(std::string serverId, config::GMConfig config);
		~GMServer() override;
		void Init() override;
		void Uninit() override;

	private:
		config::GMConfig config_;
	};
}
