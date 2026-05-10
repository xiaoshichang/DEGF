#include "server/gate/GateServer.h"

#include "core/Logger.h"
#include "http/HttpService.h"
#include "network/client/ClientNetwork.h"
#include "core/ProcessPerformance.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"
#include "server/gate/GateHttpHandler.h"

#include <chrono>
#include <stdexcept>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		constexpr auto kHeartBeatInterval = std::chrono::seconds(5);
		constexpr auto kClientHeartbeatTimeout = std::chrono::minutes(2);
		constexpr auto kClientHeartbeatCheckInterval = std::chrono::seconds(10);

		std::string BuildInnerNetworkEndpoint(const config::EndpointConfig& endpointConfig)
		{
			if (endpointConfig.host.empty() || endpointConfig.port == 0)
			{
				throw std::invalid_argument("Inner network endpoint is invalid.");
			}

			return "tcp://" + endpointConfig.host + ":" + std::to_string(endpointConfig.port);
		}

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

	GateServer::GateServer(std::string serverId, std::string configPath, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId, std::move(configPath), clusterConfig)
		, config_(ResolveGateConfig(clusterConfig, serverId))
	{
	}

	GateServer::~GateServer()
	{
	}

	void GateServer::Init()
	{
		ServerBase::Init();
		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService != nullptr)
		{
			managedRuntimeService->SetCreateAvatarReqSender(
				[this](const std::string& targetServerId, const network::GuidBytes& avatarId, std::uint64_t clientSessionId)
				{
					network::CreateAvatarReqMessage message;
					message.avatarId = avatarId;
					message.clientSessionId = clientSessionId;
					return GetInnerNetwork().Send(
						targetServerId,
						static_cast<std::uint32_t>(network::MessageID::SS::CreateAvatarReq),
						message.Serialize()
					);
				}
			);
			managedRuntimeService->SetAvatarLoginRspSender(
				[this](std::uint64_t clientSessionId, const network::LoginRspMessage& message)
				{
					return SendLoginRspToClient(
						static_cast<network::ClientNetworkSession::SessionId>(clientSessionId),
						message.Serialize()
					);
				}
			);
			managedRuntimeService->SetActiveDisconnectClientSender(
				[this](std::uint64_t clientSessionId)
				{
					if (clientNetwork_ == nullptr)
					{
						return false;
					}

					return clientNetwork_->ActiveDisconnect(
						static_cast<network::ClientNetworkSession::SessionId>(clientSessionId)
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
			managedRuntimeService->SetAvatarRpcToClientSender(
				[this](std::uint64_t clientSessionId, const std::vector<std::byte>& payload)
				{
					if (clientNetwork_ == nullptr)
					{
						Logger::Warn("GateServer", "Cannot send avatar RPC because client network is not available.");
						return false;
					}

					return clientNetwork_->Send(
						static_cast<network::ClientNetworkSession::SessionId>(clientSessionId),
						static_cast<std::uint32_t>(network::MessageID::CS::RpcNtf),
						payload
					);
				}
			);
		}

		InitHttp();
		ConnectToGm();
		StartHeartbeatTimer();
		Logger::Info("GateServer", "Init");
	}

	void GateServer::Uninit()
	{
		Logger::Info("GateServer", "Uninit");
		StopHeartbeatTimer();
		StopClientSessionTimeoutTimer();
		gmSessionId_.reset();
		openGateReceived_ = false;
		UninitClientNetwork();
		clientSessionHeartbeats_.clear();

		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService != nullptr)
		{
			managedRuntimeService->SetCreateAvatarReqSender({});
			managedRuntimeService->SetAvatarLoginRspSender({});
			managedRuntimeService->SetActiveDisconnectClientSender({});
			managedRuntimeService->SetAvatarRpcToServerSender({});
			managedRuntimeService->SetAvatarRpcToClientSender({});
			managedRuntimeService->SetServerRpcToServerSender({});
		}

		UninitHttp();
		ServerBase::Uninit();
	}

	const config::TelnetConfig& GateServer::GetTelnetConfig() const
	{
		return config_.telnet;
	}

	const config::NetworkConfig& GateServer::GetInnerNetworkConfig() const
	{
		return config_.innerNetwork;
	}

	void GateServer::OnInnerDisconnect(const std::string& serverId)
	{
		if (config::IsGmServerId(serverId))
		{
			gmSessionId_.reset();
			openGateReceived_ = false;
			UninitClientNetwork();
			clientSessionHeartbeats_.clear();
		}

		ServerBase::OnInnerDisconnect(serverId);
	}

	void GateServer::OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		switch (static_cast<network::MessageID::SS>(messageId))
		{
		case network::MessageID::SS::OpenGateNtf:
			openGateReceived_ = true;
			InitClientNetwork();
			Logger::Info("GateServer", "Received OpenGateNtf and opened client network.");
			return;

		case network::MessageID::SS::CreateAvatarRsp:
			HandleCreateAvatarRsp(serverId, data);
			return;

		case network::MessageID::SS::StubDistributeNtf:
		{
			auto* managedRuntimeService = GetManagedRuntimeService();
			if (managedRuntimeService == nullptr || !managedRuntimeService->HandleStubDistribute(data))
			{
				Logger::Warn("GateServer", "Failed to process StubDistributeNtf payload in managed runtime.");
				return;
			}

			if (!GetInnerNetwork().Send(serverId, static_cast<std::uint32_t>(network::MessageID::SS::StubDistributeGateAck), {}))
			{
				Logger::Warn("GateServer", "Failed to send StubDistributeGateAck to " + serverId + ".");
				return;
			}

			Logger::Info("GateServer", "Sent StubDistributeGateAck to " + serverId + ".");
			return;
		}

		case network::MessageID::SS::AvatarRpcNtf:
			if (auto* managedRuntimeService = GetManagedRuntimeService();
				managedRuntimeService == nullptr || !managedRuntimeService->HandleServerAvatarRpc(serverId, data))
			{
				Logger::Warn("GateServer", "Failed to handle AvatarRpcNtf in managed runtime.");
			}
			return;

		case network::MessageID::SS::ServerRpcNtf:
			if (auto* managedRuntimeService = GetManagedRuntimeService();
				managedRuntimeService == nullptr || !managedRuntimeService->HandleServerRpc(serverId, data))
			{
				Logger::Warn("GateServer", "Failed to handle ServerRpcNtf in managed runtime.");
			}
			return;

		default:
			ServerBase::OnInnerMessage(serverId, messageId, data);
			return;
		}
	}

	void GateServer::InitClientNetwork()
	{
		if (clientNetwork_ != nullptr)
		{
			return;
		}

		clientNetwork_ = std::make_unique<network::ClientNetwork>(
			GetIoContext(),
			GetClusterConfig().kcp,
			network::ClientNetworkCallbacks{
				[this](network::ClientNetworkSession::SessionId sessionId)
				{
					OnClientConnect(sessionId);
				},
				[this](network::ClientNetworkSession::SessionId sessionId, std::uint32_t messageId, const std::vector<std::byte>& data)
				{
					OnClientReceive(sessionId, messageId, data);
				},
				[this](network::ClientNetworkSession::SessionId sessionId)
				{
					OnClientDisconnect(sessionId);
				}
			}
		);

		if (!clientNetwork_->Listen(config_.clientNetwork))
		{
			clientNetwork_.reset();
			throw std::runtime_error("Failed to start client network.");
		}

		StartClientSessionTimeoutTimer();
	}

	void GateServer::UninitClientNetwork()
	{
		StopClientSessionTimeoutTimer();
		clientSessionHeartbeats_.clear();
		clientNetwork_.reset();
	}

	void GateServer::InitHttp()
	{
		if (httpService_ != nullptr || config_.authNetwork.listenEndpoint.host.empty() || config_.authNetwork.listenEndpoint.port == 0)
		{
			return;
		}

		std::vector<std::string> gateServerIds;
		gateServerIds.reserve(GetClusterConfig().gate.size());
		for (const auto& [serverId, gateConfig] : GetClusterConfig().gate)
		{
			(void)gateConfig;
			gateServerIds.push_back(serverId);
		}

		httpHandler_ = std::make_unique<GateHttpHandler>(
			GetServerId(),
			config_.clientNetwork.listenEndpoint.port,
			[this]()
			{
				return openGateReceived_ && clientNetwork_ != nullptr;
			},
			[this, gateServerIds](const std::string& account, const std::string& password) -> GateAuthValidationResult
			{
				GateAuthValidationResult validationResult;
				auto* managedRuntimeService = GetManagedRuntimeService();
				if (managedRuntimeService == nullptr
					|| !managedRuntimeService->TryValidateGateAuth(account, password, gateServerIds, validationResult))
				{
					validationResult.StatusCode = 503;
					validationResult.Error = "auth validator unavailable";
				}

				return validationResult;
			},
			[this]() -> std::optional<network::AllocatedClientSession>
			{
				if (clientNetwork_ == nullptr)
				{
					return std::nullopt;
				}

				return clientNetwork_->AllocateSession();
			}
		);

		httpService_ = std::make_unique<HttpService>(
			GetIoContext(),
			GetServerId(),
			[this](const HttpRequest& request)
			{
				return httpHandler_->HandleRequest(request);
			}
		);
		httpService_->Start(config::HttpConfig{ config_.authNetwork.listenEndpoint });
	}

	void GateServer::UninitHttp()
	{
		if (httpService_ == nullptr)
		{
			return;
		}

		httpService_->Stop();
		httpService_.reset();
		httpHandler_.reset();
	}

	void GateServer::OnClientConnect(network::ClientNetworkSession::SessionId sessionId)
	{
		UpdateClientSessionHeartbeat(sessionId);
		Logger::Info("GateServer", "Client session connected: " + std::to_string(sessionId));
	}

	void GateServer::OnClientReceive(network::ClientNetworkSession::SessionId sessionId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		Logger::Info(
			"GateServer",
			"Client session received message, sessionId=" + std::to_string(sessionId)
			+ ", messageId=" + std::to_string(messageId)
			+ ", payload=" + std::to_string(data.size())
		);

		if (messageId == static_cast<std::uint32_t>(network::MessageID::CS::HeartBeatNtf))
		{
			if (!data.empty())
			{
				Logger::Warn("GateServer", "Received invalid client HeartBeatNtf payload.");
				return;
			}

			UpdateClientSessionHeartbeat(sessionId);
			return;
		}

		if (messageId == static_cast<std::uint32_t>(network::MessageID::CS::RpcNtf))
		{
			auto* managedRuntimeService = GetManagedRuntimeService();
			if (managedRuntimeService == nullptr || !managedRuntimeService->HandleClientAvatarRpc(sessionId, data))
			{
				Logger::Warn("GateServer", "Failed to handle client avatar RPC in managed runtime.");
			}

			return;
		}

		if (messageId != static_cast<std::uint32_t>(network::MessageID::CS::LoginReq))
		{
			Logger::Warn("GateServer", "Unhandled client messageId=" + std::to_string(messageId) + ".");
			return;
		}

		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			Logger::Warn("GateServer", "Managed runtime is not available for client message.");
			return;
		}

		network::LoginReqMessage loginReq;
		if (!network::LoginReqMessage::TryDeserialize(data.data(), data.size(), loginReq))
		{
			Logger::Warn("GateServer", "Received invalid LoginReq payload.");
			return;
		}

		if (!managedRuntimeService->HandleAvatarLoginReq(sessionId, loginReq.account))
		{
			Logger::Warn("GateServer", "Failed to handle AvatarLoginReq in managed runtime.");
			return;
		}
	}

	void GateServer::OnClientDisconnect(network::ClientNetworkSession::SessionId sessionId)
	{
		clientSessionHeartbeats_.erase(sessionId);
		Logger::Info("GateServer", "Client session disconnected: " + std::to_string(sessionId));
		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			return;
		}

		if (!managedRuntimeService->HandleClientDisconnect(sessionId))
		{
			Logger::Warn("GateServer", "Failed to handle client disconnect in managed runtime.");
		}
	}

	void GateServer::StartClientSessionTimeoutTimer()
	{
		if (clientSessionTimeoutTimerId_.has_value())
		{
			return;
		}

		clientSessionTimeoutTimerId_ = GetTimerManager().AddTimer(
			std::chrono::duration_cast<std::chrono::milliseconds>(kClientHeartbeatCheckInterval),
			[this](TimerManager::TimerID timerId)
			{
				OnClientSessionTimeoutTimer(timerId);
			},
			true
		);
	}

	void GateServer::StopClientSessionTimeoutTimer()
	{
		if (!clientSessionTimeoutTimerId_.has_value())
		{
			return;
		}

		GetTimerManager().CancelTimer(*clientSessionTimeoutTimerId_);
		clientSessionTimeoutTimerId_.reset();
	}

	void GateServer::OnClientSessionTimeoutTimer(TimerManager::TimerID timerId)
	{
		if (!clientSessionTimeoutTimerId_.has_value() || *clientSessionTimeoutTimerId_ != timerId)
		{
			return;
		}

		DisconnectExpiredClientSessions();
	}

	void GateServer::UpdateClientSessionHeartbeat(network::ClientNetworkSession::SessionId sessionId)
	{
		clientSessionHeartbeats_[sessionId] = std::chrono::steady_clock::now();
	}

	void GateServer::DisconnectExpiredClientSessions()
	{
		if (clientNetwork_ == nullptr)
		{
			return;
		}

		const auto now = std::chrono::steady_clock::now();
		std::vector<network::ClientNetworkSession::SessionId> expiredSessionIds;
		for (const auto& [sessionId, lastHeartbeat] : clientSessionHeartbeats_)
		{
			if (now - lastHeartbeat >= kClientHeartbeatTimeout)
			{
				expiredSessionIds.push_back(sessionId);
			}
		}

		for (const auto sessionId : expiredSessionIds)
		{
			Logger::Warn("GateServer", "Client session heartbeat timeout, disconnecting sessionId=" + std::to_string(sessionId) + ".");
			clientNetwork_->ActiveDisconnect(sessionId);
		}
	}

	void GateServer::HandleCreateAvatarRsp(const std::string& serverId, const std::vector<std::byte>& data)
	{
		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			Logger::Warn("GateServer", "Managed runtime is not available for CreateAvatarRsp.");
			return;
		}

		network::CreateAvatarRspMessage createAvatarRsp;
		if (!network::CreateAvatarRspMessage::TryDeserialize(data.data(), data.size(), createAvatarRsp))
		{
			Logger::Warn("GateServer", "Received invalid CreateAvatarRsp payload.");
			return;
		}

		if (!managedRuntimeService->HandleCreateAvatarRsp(
			serverId,
			createAvatarRsp.avatarId,
			createAvatarRsp.clientSessionId,
			createAvatarRsp.isSuccess,
			createAvatarRsp.statusCode,
			createAvatarRsp.error,
			createAvatarRsp.avatarData
		))
		{
			Logger::Warn("GateServer", "Failed to handle CreateAvatarRsp in managed runtime.");
			return;
		}
	}

	bool GateServer::SendLoginRspToClient(network::ClientNetworkSession::SessionId sessionId, const std::vector<std::byte>& payload)
	{
		if (clientNetwork_ == nullptr)
		{
			Logger::Warn("GateServer", "Cannot send LoginRsp because client network is not available.");
			return false;
		}

		if (!clientNetwork_->Send(sessionId, static_cast<std::uint32_t>(network::MessageID::CS::LoginRsp), payload))
		{
			Logger::Warn("GateServer", "Failed to send LoginRsp to client session " + std::to_string(sessionId) + ".");
			return false;
		}

		return true;
	}

	void GateServer::ConnectToGm()
	{
		if (gmSessionId_.has_value())
		{
			return;
		}

		const auto gmEndpoint = BuildInnerNetworkEndpoint(GetClusterConfig().gm.innerNetwork.listenEndpoint);
		auto* session = GetInnerNetwork().ConnectTo(gmEndpoint);
		if (session == nullptr)
		{
			Logger::Warn("GateServer", "Failed to connect inner network to GM: " + gmEndpoint);
			return;
		}

		gmSessionId_ = session->GetSessionId();
		Logger::Info("GateServer", "Connecting inner network to GM at " + gmEndpoint);
	}

	void GateServer::StartHeartbeatTimer()
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

	void GateServer::StopHeartbeatTimer()
	{
		if (!heartbeatTimerId_.has_value())
		{
			return;
		}

		GetTimerManager().CancelTimer(*heartbeatTimerId_);
		heartbeatTimerId_.reset();
	}

	void GateServer::OnHeartbeatTimer(TimerManager::TimerID timerId)
	{
		if (!heartbeatTimerId_.has_value() || *heartbeatTimerId_ != timerId)
		{
			return;
		}

		auto& innerNetwork = GetInnerNetwork();
		const std::string gmServerId(config::GetCanonicalGmServerId());
		if (!innerNetwork.HasRegisteredSession(gmServerId))
		{
			Logger::Warn("Gate", "OnHeartbeatTimer gm not registered.");
			return;
		}

		if (!innerNetwork.Send(
			gmServerId,
			static_cast<std::uint32_t>(network::MessageID::SS::HeartBeatWithDataNtf),
			[&]()
			{
				const network::HeartBeatWithDataNtfMessage heartBeatMessage
				{
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
			Logger::Warn("GateServer", "Failed to send heartbeat to GM.");
		}
	}
}
