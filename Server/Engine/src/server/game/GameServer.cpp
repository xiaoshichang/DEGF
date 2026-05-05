#include "server/game/GameServer.h"

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

	GameServer::GameServer(std::string serverId, std::string configPath, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId, std::move(configPath), clusterConfig)
		, config_(ResolveGameConfig(clusterConfig, serverId))
	{
	}

	GameServer::~GameServer()
	{
	}

	void GameServer::Init()
	{
		ServerBase::Init();

		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService != nullptr)
		{
			managedRuntimeService->SetGameServerReadyCallback(
				[this]()
				{
					OnManagedGameServerReady();
				}
			);
			managedRuntimeService->SetCreateAvatarReqSender(
				[this](const std::string& targetServerId, const network::GuidBytes& avatarId)
				{
					network::CreateAvatarReqMessage message;
					message.avatarId = avatarId;
					return GetInnerNetwork().Send(
						targetServerId,
						static_cast<std::uint32_t>(network::MessageID::SS::CreateAvatarReq),
						message.Serialize()
					);
				}
			);
			managedRuntimeService->SetCreateAvatarRspSender(
				[this](const std::string& targetServerId, const network::CreateAvatarRspMessage& message)
				{
					return GetInnerNetwork().Send(
						targetServerId,
						static_cast<std::uint32_t>(network::MessageID::SS::CreateAvatarRsp),
						message.Serialize()
					);
				}
			);
			managedRuntimeService->SetAvatarRpcToServerSender(
				[this](const std::string& targetServerId, const std::vector<std::byte>& payload)
				{
					return GetInnerNetwork().Send(
						targetServerId,
						static_cast<std::uint32_t>(network::MessageID::SS::AvatarRpcNtf),
						payload
					);
				}
			);
			managedRuntimeService->SetServerRpcToServerSender(
				[this](const std::string& targetServerId, const std::vector<std::byte>& payload)
				{
					return GetInnerNetwork().Send(
						targetServerId,
						static_cast<std::uint32_t>(network::MessageID::SS::ServerRpcNtf),
						payload
					);
				}
			);
		}

		ConnectToGm();
		StartHeartbeatTimer();
		Logger::Info("GameServer", "Init");
	}

	void GameServer::Uninit()
	{
		Logger::Info("GameServer", "Uninit");
		StopHeartbeatTimer();
		gmSessionId_.reset();

		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService != nullptr)
		{
			managedRuntimeService->SetGameServerReadyCallback({});
			managedRuntimeService->SetCreateAvatarReqSender({});
			managedRuntimeService->SetCreateAvatarRspSender({});
			managedRuntimeService->SetAvatarRpcToServerSender({});
			managedRuntimeService->SetServerRpcToServerSender({});
		}

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

	void GameServer::OnInnerRegistered(const std::string& serverId)
	{
		ServerBase::OnInnerRegistered(serverId);

		if (const auto* gateConfig = config::FindGateConfig(GetClusterConfig(), serverId))
		{
			(void)gateConfig;
			TryNotifyGameReady();
		}
	}

	void GameServer::OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		switch (static_cast<network::MessageID::SS>(messageId))
		{
		case network::MessageID::SS::AllNodeReadyNtf:
			allNodeReadyReceived_ = true;
			managedStubsReady_ = false;

			if (auto* managedRuntimeService = GetManagedRuntimeService();
				managedRuntimeService == nullptr || !managedRuntimeService->HandleAllNodeReady(data))
			{
				Logger::Warn("GameServer", "Failed to process AllNodeReadyNtf payload in managed runtime.");
			}

			ConnectToAllGates();
			return;

		case network::MessageID::SS::CreateAvatarReq:
			HandleCreateAvatarReq(serverId, data);
			return;

		case network::MessageID::SS::AvatarRpcNtf:
			if (auto* managedRuntimeService = GetManagedRuntimeService();
				managedRuntimeService == nullptr || !managedRuntimeService->HandleServerAvatarRpc(serverId, data))
			{
				Logger::Warn("GameServer", "Failed to handle AvatarRpcNtf in managed runtime.");
			}
			return;

		case network::MessageID::SS::ServerRpcNtf:
			if (auto* managedRuntimeService = GetManagedRuntimeService();
				managedRuntimeService == nullptr || !managedRuntimeService->HandleServerRpc(serverId, data))
			{
				Logger::Warn("GameServer", "Failed to handle ServerRpcNtf in managed runtime.");
			}
			return;

		default:
			ServerBase::OnInnerMessage(serverId, messageId, data);
			return;
		}
	}

	void GameServer::OnInnerDisconnect(const std::string& serverId)
	{
		if (config::IsGmServerId(serverId))
		{
			gmSessionId_.reset();
			allNodeReadyReceived_ = false;
			managedStubsReady_ = false;
			gameReadyNotified_ = false;
		}
		else if (const auto* gateConfig = config::FindGateConfig(GetClusterConfig(), serverId))
		{
			(void)gateConfig;
			gameReadyNotified_ = false;
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

	void GameServer::ConnectToAllGates()
	{
		if (!allNodeReadyReceived_)
		{
			return;
		}

		auto& innerNetwork = GetInnerNetwork();
		const auto& clusterConfig = GetClusterConfig();
		for (const auto& [serverId, gateConfig] : clusterConfig.gate)
		{
			if (innerNetwork.HasRegisteredSession(serverId))
			{
				continue;
			}

			const auto gateEndpoint = BuildInnerNetworkEndpoint(gateConfig.innerNetwork.listenEndpoint);
			auto* session = innerNetwork.ConnectTo(gateEndpoint);
			if (session == nullptr)
			{
				Logger::Warn("GameServer", "Failed to connect inner network to gate " + serverId + ": " + gateEndpoint);
				continue;
			}

			(void)session;
			Logger::Info("GameServer", "Connecting inner network to gate " + serverId + " at " + gateEndpoint);
		}
	}

	bool GameServer::AreAllGateSessionsRegistered()
	{
		const auto& innerNetwork = GetInnerNetwork();
		for (const auto& [serverId, gateConfig] : GetClusterConfig().gate)
		{
			(void)gateConfig;
			if (!innerNetwork.HasRegisteredSession(serverId))
			{
				return false;
			}
		}

		return true;
	}

	void GameServer::TryNotifyGameReady()
	{
		if (!allNodeReadyReceived_ || !managedStubsReady_ || gameReadyNotified_)
		{
			return;
		}

		const std::string gmServerId(config::GetCanonicalGmServerId());
		auto& innerNetwork = GetInnerNetwork();
		if (!innerNetwork.HasRegisteredSession(gmServerId) || !AreAllGateSessionsRegistered())
		{
			return;
		}

		if (!innerNetwork.Send(gmServerId, static_cast<std::uint32_t>(network::MessageID::SS::GameReadyNtf), {}))
		{
			Logger::Warn("GameServer", "Failed to send GameReadyNtf to GM.");
			return;
		}

		gameReadyNotified_ = true;
		Logger::Info("GameServer", "Sent GameReadyNtf to GM.");
	}

	void GameServer::OnManagedGameServerReady()
	{
		managedStubsReady_ = true;
		Logger::Info("GameServer", "Managed stubs are ready.");
		TryNotifyGameReady();
	}

	void GameServer::HandleCreateAvatarReq(const std::string& serverId, const std::vector<std::byte>& data)
	{
		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			Logger::Warn("GameServer", "Managed runtime is not available for CreateAvatarReq.");
			return;
		}

		network::CreateAvatarReqMessage createAvatarReq;
		if (!network::CreateAvatarReqMessage::TryDeserialize(data.data(), data.size(), createAvatarReq))
		{
			Logger::Warn("GameServer", "Received invalid CreateAvatarReq payload.");
			return;
		}

		if (!managedRuntimeService->HandleCreateAvatarReq(serverId, createAvatarReq.avatarId))
		{
			Logger::Warn("GameServer", "Failed to handle CreateAvatarReq in managed runtime.");
			return;
		}
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
			Logger::Warn("GameServer", "OnHeartbeatTimer gm not registered.");
			return;
		}

		if (!innerNetwork.Send(
			gmServerId,
			static_cast<std::uint32_t>(network::MessageID::SS::HeartBeatWithDataNtf),
			[&]()
			{
				const network::HeartBeatWithDataNtfMessage heartBeatMessage{
					network::HeartBeatWithDataNtfMessage::kCurrentVersion,
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
