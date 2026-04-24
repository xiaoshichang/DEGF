#include "server/gm/GMServer.h"

#include "core/Logger.h"
#include "http/HttpService.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"
#include "server/gm/GMHttpHandler.h"

#include <utility>

namespace de::server::engine
{
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

		if (httpHandler_ != nullptr)
		{
			httpHandler_->UpdateNodePerformanceSnapshot(serverId, heartBeatMessage.performance);
		}

		Logger::Debug(
			"GMServer",
			"Updated heartbeat snapshot from " + serverId + ", workingSetBytes=" + std::to_string(heartBeatMessage.performance.workingSetBytes)
		);
	}

	void GMServer::HandleGameReadyNtf(const std::string& serverId)
	{
		readyGameServerIds_.insert(serverId);
		TryNotifyOpenGate();
	}

	void GMServer::OnInnerDisconnect(const std::string& serverId)
	{
		registeredNodeServerIds_.erase(serverId);
		readyGameServerIds_.erase(serverId);
		if (httpHandler_ != nullptr)
		{
			httpHandler_->ClearNodePerformanceSnapshot(serverId);
		}

		allNodeReadyNotified_ = false;
		openGateNotified_ = false;
		ServerBase::OnInnerDisconnect(serverId);
	}

	void GMServer::InitHttp()
	{
		if (httpService_ != nullptr || config_.http.listenEndpoint.host.empty() || config_.http.listenEndpoint.port == 0)
		{
			return;
		}

		httpHandler_ = std::make_unique<GMHttpHandler>(GetServerId(), GetClusterConfig());
		httpService_ = std::make_unique<HttpService>(
			GetIoContext(),
			GetServerId(),
			[this](const HttpRequest& request)
			{
				return httpHandler_->HandleRequest(request);
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
		httpHandler_.reset();
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

		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			Logger::Warn("GMServer", "Managed runtime service is not initialized.");
			return;
		}

		std::vector<std::string> gameServerIds;
		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			gameServerIds.push_back(serverId);
		}

		std::vector<std::byte> payload;
		if (!managedRuntimeService->TryBuildStubDistributePayload(gameServerIds, payload))
		{
			Logger::Warn("GMServer", "Failed to build stub distribute payload before AllNodeReadyNtf.");
			return;
		}

		auto& innerNetwork = GetInnerNetwork();
		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (!innerNetwork.Send(serverId, static_cast<std::uint32_t>(network::MessageID::AllNodeReadyNtf), payload))
			{
				Logger::Warn("GMServer", "Failed to send AllNodeReadyNtf to " + serverId + ".");
				return;
			}
		}

		allNodeReadyNotified_ = true;
		Logger::Info(
			"GMServer",
			"Sent AllNodeReadyNtf to all game nodes with stub distribute payload size " + std::to_string(payload.size()) + "."
		);
	}

	void GMServer::TryNotifyOpenGate()
	{
		if (openGateNotified_)
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

		auto& innerNetwork = GetInnerNetwork();
		for (const auto& [serverId, gateConfig] : clusterConfig.gate)
		{
			(void)gateConfig;
			if (!innerNetwork.Send(serverId, static_cast<std::uint32_t>(network::MessageID::OpenGateNtf), {}))
			{
				Logger::Warn("GMServer", "Failed to send OpenGateNtf to " + serverId + ".");
				return;
			}
		}

		openGateNotified_ = true;
		Logger::Info("GMServer", "Sent OpenGateNtf to all gate nodes.");
	}

}
