#include "GMServer.h"

#include "core/Logger.h"

#include <utility>

namespace de::server::engine
{
	GMServer::GMServer(std::string serverId, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId)
		, config_(clusterConfig.gm)
	{
	}

	GMServer::~GMServer()
	{
	}

	void GMServer::Init()
	{
		ServerBase::Init();
		Logger::Info("GMServer", "Init");
	}

	void GMServer::Uninit()
	{
		Logger::Info("GMServer", "Uninit");
		ServerBase::Uninit();
	}

	const config::TelnetConfig& GMServer::GetTelnetConfig() const
	{
		return config_.telnet;
	}
}
