#include "GateServer.h"

#include "core/Logger.h"

#include <stdexcept>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		config::GateConfig ResolveGateConfig(const config::ClusterConfig& clusterConfig, std::string_view serverId)
		{
			const auto* gateConfig = config::FindGateConfig(clusterConfig, serverId);
			if (gateConfig == nullptr)
			{
				throw std::invalid_argument("Gate config not found for server-id: " + std::string(serverId));
			}

			return *gateConfig;
		}
	}

	GateServer::GateServer(std::string serverId, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId)
		, config_(ResolveGateConfig(clusterConfig, serverId))
	{
	}

	GateServer::~GateServer()
	{
	}

	void GateServer::Init()
	{
		ServerBase::Init();
		Logger::Info("GateServer", "Init");
	}

	void GateServer::Uninit()
	{
		Logger::Info("GateServer", "Uninit");
		ServerBase::Uninit();
	}

	const config::TelnetConfig& GateServer::GetTelnetConfig() const
	{
		return config_.telnet;
	}
}
