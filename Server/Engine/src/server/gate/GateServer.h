#pragma once

#include "server/ServerBase.h"
#include "config/ClusterConfig.h"
#include "network/client/ClientNetworkSession.h"

#include <memory>
#include <optional>

namespace de::server::engine
{
	namespace network
	{
		class ClientNetwork;
	}

	class GateServer : public ServerBase
	{
public:
		GateServer(std::string serverId, std::string configPath, const config::ClusterConfig& clusterConfig);
		~GateServer() override;
		void Init() override;
		void Uninit() override;

	private:
		const config::TelnetConfig& GetTelnetConfig() const override;
		const config::NetworkConfig& GetInnerNetworkConfig() const override;
		void OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data) override;
		void OnInnerDisconnect(const std::string& serverId) override;
		void InitClientNetwork();
		void UninitClientNetwork();
		void OnClientConnect(network::ClientNetworkSession::SessionId sessionId);
		void OnClientReceive(network::ClientNetworkSession::SessionId sessionId, std::uint32_t messageId, const std::vector<std::byte>& data);
		void OnClientDisconnect(network::ClientNetworkSession::SessionId sessionId);
		void ConnectToGm();
		void StartHeartbeatTimer();
		void StopHeartbeatTimer();
		void OnHeartbeatTimer(TimerManager::TimerID timerId);

		config::GateConfig config_;
		std::unique_ptr<network::ClientNetwork> clientNetwork_;
		std::optional<network::InnerNetwork::SessionId> gmSessionId_;
		std::optional<TimerManager::TimerID> heartbeatTimerId_;
		bool openGateReceived_ = false;
	};
}
