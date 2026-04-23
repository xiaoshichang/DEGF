#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"

#include <optional>

namespace de::server::engine
{
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
		void OnInnerDisconnect(const std::string& serverId) override;
		void ConnectToGm();
		void StartHeartbeatTimer();
		void StopHeartbeatTimer();
		void OnHeartbeatTimer(TimerManager::TimerID timerId);

		config::GateConfig config_;
		std::optional<network::InnerNetwork::SessionId> gmSessionId_;
		std::optional<TimerManager::TimerID> heartbeatTimerId_;
	};
}
