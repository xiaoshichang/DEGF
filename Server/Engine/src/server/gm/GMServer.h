#pragma once

#include "server/ServerBase.h"
#include "config/ClusterConfig.h"

#include <memory>
#include <string>
#include <unordered_set>

namespace de::server::engine
{
	class GMHttpHandler;
	class HttpService;

	class GMServer : public ServerBase
	{
	public:
		GMServer(std::string serverId, std::string configPath, const config::ClusterConfig& clusterConfig);
		~GMServer() override;
		void Init() override;
		void Uninit() override;

	private:
		const config::TelnetConfig& GetTelnetConfig() const override;
		const config::NetworkConfig& GetInnerNetworkConfig() const override;
		void OnInnerRegistered(const std::string& serverId) override;
		void OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data) override;
		void OnInnerDisconnect(const std::string& serverId) override;
		void HandleHeartBeatWithDataNtf(const std::string& serverId, const std::vector<std::byte>& data);
		void HandleGameReadyNtf(const std::string& serverId);
		void InitHttp();
		void UninitHttp();
		void TryNotifyAllNodeReady();
		void TryNotifyOpenGate();

		config::GMConfig config_;
		std::unique_ptr<GMHttpHandler> httpHandler_;
		std::unique_ptr<HttpService> httpService_;
		std::unordered_set<std::string> registeredNodeServerIds_;
		std::unordered_set<std::string> readyGameServerIds_;
		bool allNodeReadyNotified_ = false;
		bool openGateNotified_ = false;
	};
}
