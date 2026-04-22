#include "GMServer.h"

#include "http/HttpService.h"
#include "core/Logger.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"

#include <boost/json.hpp>

#include <string>
#include <string_view>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		boost::json::object BuildSnapshotJson(std::string_view serverId, const ProcessPerformanceSnapshot& snapshot)
		{
			return boost::json::object{
				{ "serverId", std::string(serverId) },
				{ "workingSetBytes", snapshot.workingSetBytes }
			};
		}

		boost::json::object BuildMissingSnapshotJson(std::string_view serverId)
		{
			return boost::json::object{
				{ "serverId", std::string(serverId) },
				{ "workingSetBytes", static_cast<std::uint64_t>(0) }
			};
		}
	}

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
		InitHttp();
		Logger::Info("GMServer", "Init");
	}

	void GMServer::Uninit()
	{
		Logger::Info("GMServer", "Uninit");
		UninitHttp();
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
		if (static_cast<network::MessageID>(messageId) == network::MessageID::HeartBeatWithDataNtf)
		{
			network::HeartBeatWithDataNtfMessage heartBeatMessage;
			if (!network::HeartBeatWithDataNtfMessage::TryDeserialize(data.data(), data.size(), heartBeatMessage))
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

	void GMServer::InitHttp()
	{
		if (httpService_ != nullptr || config_.http.listenEndpoint.host.empty() || config_.http.listenEndpoint.port == 0)
		{
			return;
		}

		httpService_ = std::make_unique<HttpService>(
			GetIoContext(),
			GetServerId(),
			[this]()
			{
				return BuildPerformanceResponse();
			}
		);
		httpService_->Start(config_.http);
	}

	void GMServer::UninitHttp()
	{
		if (httpService_ == nullptr)
		{
			return;
		}

		httpService_->Stop();
		httpService_.reset();
	}

	std::string GMServer::BuildPerformanceResponse() const
	{
		boost::json::object nodes;
		const auto& clusterConfig = GetClusterConfig();

		nodes.emplace(GetServerId(), BuildSnapshotJson(GetServerId(), CollectProcessPerformanceSnapshot()));

		for (const auto& [serverId, gateConfig] : clusterConfig.gate)
		{
			(void)gateConfig;

			const auto snapshotIterator = latestNodePerformanceSnapshots_.find(serverId);
			if (snapshotIterator == latestNodePerformanceSnapshots_.end())
			{
				nodes.emplace(serverId, BuildMissingSnapshotJson(serverId));
				continue;
			}

			nodes.emplace(serverId, BuildSnapshotJson(serverId, snapshotIterator->second));
		}

		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;

			const auto snapshotIterator = latestNodePerformanceSnapshots_.find(serverId);
			if (snapshotIterator == latestNodePerformanceSnapshots_.end())
			{
				nodes.emplace(serverId, BuildMissingSnapshotJson(serverId));
				continue;
			}

			nodes.emplace(serverId, BuildSnapshotJson(serverId, snapshotIterator->second));
		}

		boost::json::object payload;
		payload.emplace("nodes", std::move(nodes));
		return boost::json::serialize(payload);
	}
}
