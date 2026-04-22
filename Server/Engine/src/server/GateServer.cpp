#include "GateServer.h"

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
		constexpr std::string_view kGmServerId = "gm";
		constexpr auto kHeartBeatInterval = std::chrono::seconds(5);

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

	GateServer::GateServer(std::string serverId, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId, clusterConfig)
		, config_(ResolveGateConfig(clusterConfig, serverId))
	{
	}

	GateServer::~GateServer()
	{
	}

	void GateServer::Init()
	{
		ServerBase::Init();
		ConnectToGm();
		StartHeartbeatTimer();
		Logger::Info("GateServer", "Init");
	}

	void GateServer::Uninit()
	{
		Logger::Info("GateServer", "Uninit");
		StopHeartbeatTimer();
		gmSessionId_.reset();
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
		if (serverId == kGmServerId)
		{
			gmSessionId_.reset();
		}

		ServerBase::OnInnerDisconnect(serverId);
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
		auto& innerNetwork = GetInnerNetwork();
		if (!innerNetwork.Send(
			std::string(kGmServerId),
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
			Logger::Warn("GateServer", "Failed to send heartbeat to GM.");
		}
	}
}
