#pragma once

#include "server/ServerBase.h"
#include "config/ClusterConfig.h"

#include <optional>

namespace de::server::engine
{
	class GameServer : public ServerBase
	{
	public:
		GameServer(std::string serverId, std::string configPath, const config::ClusterConfig& clusterConfig);
		~GameServer() override;
		void Init() override;
		void Uninit() override;

	private:
		const config::TelnetConfig& GetTelnetConfig() const override;
		const config::NetworkConfig& GetInnerNetworkConfig() const override;
		void OnInnerRegistered(const std::string& serverId) override;
		void OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data) override;
		void OnInnerDisconnect(const std::string& serverId) override;
		void ConnectToGm();
		void ConnectToAllGates();
		void TryNotifyGameReady();
		bool AreAllGateSessionsRegistered();
		void StartHeartbeatTimer();
		void StopHeartbeatTimer();
		void OnHeartbeatTimer(TimerManager::TimerID timerId);

		config::GameConfig config_;
		std::optional<network::InnerNetwork::SessionId> gmSessionId_;
		std::optional<TimerManager::TimerID> heartbeatTimerId_;
		bool allNodeReadyReceived_ = false;
		bool gameReadyNotified_ = false;
	};
}
