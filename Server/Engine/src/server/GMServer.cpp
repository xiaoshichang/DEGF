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

	GMServer::GMServer(std::string serverId, std::string configPath, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId, std::move(configPath), clusterConfig)
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

	void GMServer::OnInnerRegistered(const std::string& serverId)
	{
		registeredNodeServerIds_.insert(serverId);
		ServerBase::OnInnerRegistered(serverId);
		TryNotifyAllNodeReady();
	}

	void GMServer::OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		switch (static_cast<network::MessageID>(messageId))
		{
		case network::MessageID::HeartBeatWithDataNtf:
			HandleHeartBeatWithDataNtf(serverId, data);
			return;

		case network::MessageID::GameReadyNtf:
			HandleGameReadyNtf(serverId);
			return;

		default:
			ServerBase::OnInnerMessage(serverId, messageId, data);
			return;
		}
	}

	void GMServer::HandleHeartBeatWithDataNtf(const std::string& serverId, const std::vector<std::byte>& data)
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
	}

	void GMServer::HandleGameReadyNtf(const std::string& serverId)
	{
		readyGameServerIds_.insert(serverId);
		TryLogAllGameReady();
	}

	void GMServer::OnInnerDisconnect(const std::string& serverId)
	{
		registeredNodeServerIds_.erase(serverId);
		readyGameServerIds_.erase(serverId);
		latestNodePerformanceSnapshots_.erase(serverId);
		allNodeReadyNotified_ = false;
		allGameReadyLogged_ = false;
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

	void GMServer::TryNotifyAllNodeReady()
	{
		if (allNodeReadyNotified_)
		{
			return;
		}

		const auto& clusterConfig = GetClusterConfig();
		for (const auto& [serverId, gateConfig] : clusterConfig.gate)
		{
			(void)gateConfig;
			if (registeredNodeServerIds_.find(serverId) == registeredNodeServerIds_.end())
			{
				return;
			}
		}

		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (registeredNodeServerIds_.find(serverId) == registeredNodeServerIds_.end())
			{
				return;
			}
		}

		auto& innerNetwork = GetInnerNetwork();
		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (!innerNetwork.Send(serverId, static_cast<std::uint32_t>(network::MessageID::AllNodeReadyNtf), {}))
			{
				Logger::Warn("GMServer", "Failed to send AllNodeReadyNtf to " + serverId + ".");
				return;
			}
		}

		allNodeReadyNotified_ = true;
		Logger::Info("GMServer", "Sent AllNodeReadyNtf to all game nodes.");
	}

	void GMServer::TryLogAllGameReady()
	{
		if (allGameReadyLogged_)
		{
			return;
		}

		const auto& clusterConfig = GetClusterConfig();
		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (readyGameServerIds_.find(serverId) == readyGameServerIds_.end())
			{
				return;
			}
		}

		allGameReadyLogged_ = true;
		Logger::Info("GMServer", "all game ready");
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
