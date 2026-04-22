#include "GMServer.h"

#include "core/Logger.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"

#include <utility>

namespace de::server::engine
{
	GMServer::GMServer(std::string serverId, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId, clusterConfig)
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

	const config::NetworkConfig& GMServer::GetInnerNetworkConfig() const
	{
		return config_.innerNetwork;
	}

	void GMServer::OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		if (static_cast<network::MessageID>(messageId) == network::MessageID::HeartBeat)
		{
			network::HeartBeatMessage heartBeatMessage;
			if (!network::HeartBeatMessage::TryDeserialize(data.data(), data.size(), heartBeatMessage))
			{
				Logger::Warn("GMServer", "Received invalid heartbeat payload from " + serverId + ".");
				return;
			}

			latestNodePerformanceSnapshots_[serverId] = heartBeatMessage.performance;
			Logger::Debug(
				"GMServer",
				"Updated heartbeat snapshot from " + serverId + ", workingSetBytes=" + std::to_string(heartBeatMessage.performance.workingSetBytes)
			);
			return;
		}

		ServerBase::OnInnerMessage(serverId, messageId, data);
	}

	void GMServer::OnInnerDisconnect(const std::string& serverId)
	{
		latestNodePerformanceSnapshots_.erase(serverId);
		ServerBase::OnInnerDisconnect(serverId);
	}
}
