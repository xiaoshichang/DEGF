#include "GameServer.h"

#include "core/Logger.h"
#include "core/ProcessPerformance.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"

#include <chrono>
#include <stdexcept>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		constexpr auto kHeartBeatInterval = std::chrono::seconds(5);

		std::string BuildInnerNetworkEndpoint(const config::EndpointConfig& endpointConfig)
		{
			if (endpointConfig.host.empty() || endpointConfig.port == 0)
			{
				throw std::invalid_argument("Inner network endpoint is invalid.");
			}

			return "tcp://" + endpointConfig.host + ":" + std::to_string(endpointConfig.port);
		}

		config::GameConfig ResolveGameConfig(const config::ClusterConfig& clusterConfig, std::string_view serverId)
		{
			const auto* gameConfig = config::FindGameConfig(clusterConfig, serverId);
			if (gameConfig == nullptr)
			{
				throw std::invalid_argument("Game config not found for server-id: " + std::string(serverId));
			}

			return *gameConfig;
		}
	}

	GameServer::GameServer(std::string serverId, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId, clusterConfig)
		, config_(ResolveGameConfig(clusterConfig, serverId))
	{
	}

	GameServer::~GameServer()
	{
	}

	void GameServer::Init()
	{
		ServerBase::Init();
		ConnectToGm();
		StartHeartbeatTimer();
		Logger::Info("GameServer", "Init");
	}

	void GameServer::Uninit()
	{
		Logger::Info("GameServer", "Uninit");
		StopHeartbeatTimer();
		gmSessionId_.reset();
		ServerBase::Uninit();
	}

	const config::TelnetConfig& GameServer::GetTelnetConfig() const
	{
		return config_.telnet;
	}

	const config::NetworkConfig& GameServer::GetInnerNetworkConfig() const
	{
		return config_.innerNetwork;
	}

	void GameServer::OnInnerDisconnect(const std::string& serverId)
	{
		if (config::IsGmServerId(serverId))
		{
			gmSessionId_.reset();
		}

		ServerBase::OnInnerDisconnect(serverId);
	}

	void GameServer::ConnectToGm()
	{
		if (gmSessionId_.has_value())
		{
			return;
		}

		const auto gmEndpoint = BuildInnerNetworkEndpoint(GetClusterConfig().gm.innerNetwork.listenEndpoint);
		auto* session = GetInnerNetwork().ConnectTo(gmEndpoint);
		if (session == nullptr)
		{
			Logger::Warn("GameServer", "Failed to connect inner network to GM: " + gmEndpoint);
			return;
		}

		gmSessionId_ = session->GetSessionId();
		Logger::Info("GameServer", "Connecting inner network to GM at " + gmEndpoint);
	}

	void GameServer::StartHeartbeatTimer()
	{
		if (heartbeatTimerId_.has_value())
		{
			return;
		}

		heartbeatTimerId_ = GetTimerManager().AddTimer(
			std::chrono::duration_cast<std::chrono::milliseconds>(kHeartBeatInterval),
			[this](TimerManager::TimerID timerId)
			{
				OnHeartbeatTimer(timerId);
			},
			true
		);
	}

	void GameServer::StopHeartbeatTimer()
	{
		if (!heartbeatTimerId_.has_value())
		{
			return;
		}

		GetTimerManager().CancelTimer(*heartbeatTimerId_);
		heartbeatTimerId_.reset();
	}

	void GameServer::OnHeartbeatTimer(TimerManager::TimerID timerId)
	{
		if (!heartbeatTimerId_.has_value() || *heartbeatTimerId_ != timerId)
		{
			return;
		}

		auto& innerNetwork = GetInnerNetwork();
		const std::string gmServerId(config::GetCanonicalGmServerId());
		if (!innerNetwork.HasRegisteredSession(gmServerId))
		{
			if (!gmSessionId_.has_value())
			{
				ConnectToGm();
			}

			return;
		}

		if (!innerNetwork.Send(
			gmServerId,
			static_cast<std::uint32_t>(network::MessageID::HeartBeat),
			[&]()
			{
				const network::HeartBeatMessage heartBeatMessage{
					network::HeartBeatMessage::kCurrentVersion,
					0,
					CollectProcessPerformanceSnapshot()
				};
				const auto bytes = heartBeatMessage.Serialize();
				std::vector<std::byte> payload(bytes.size());
				for (std::size_t index = 0; index < bytes.size(); ++index)
				{
					payload[index] = static_cast<std::byte>(bytes[index]);
				}
				return payload;
			}()
		))
		{
			Logger::Warn("GameServer", "Failed to send heartbeat to GM.");
		}
	}
}
