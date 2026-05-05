#pragma once

#include "server/ServerBase.h"
#include "config/ClusterConfig.h"

#include <memory>
#include <string>
#include <string_view>
#include <unordered_map>
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
		TelnetCommandResult OnTelnetCommand(std::uint64_t sessionId, std::string_view commandLine) override;
		void HandleHeartBeatWithDataNtf(const std::string& serverId, const std::vector<std::byte>& data);
		void HandleGameReadyNtf(const std::string& serverId);
		void HandleGmTotalEntityCountRsp(const std::string& serverId, const std::vector<std::byte>& data);
		void InitHttp();
		void UninitHttp();
		void TryNotifyAllNodeReady();
		void TryNotifyOpenGate();
		TelnetCommandResult BeginTotalEntityCountCommand(std::uint64_t sessionId);
		void CompleteTotalEntityCountCommand(std::uint64_t requestId, std::string response);
		void FailTotalEntityCountCommand(std::uint64_t requestId, std::string response);
		std::vector<std::string> GetGameServerIds() const;

		struct PendingGmCommand
		{
			std::uint64_t requestId = 0;
			std::uint64_t telnetSessionId = 0;
			TimerManager::TimerID timeoutTimerId = 0;
		};

		config::GMConfig config_;
		std::unique_ptr<GMHttpHandler> httpHandler_;
		std::unique_ptr<HttpService> httpService_;
		std::unordered_set<std::string> registeredNodeServerIds_;
		std::unordered_set<std::string> readyGameServerIds_;
		std::unordered_map<std::uint64_t, PendingGmCommand> pendingGmCommands_;
		std::uint64_t nextGmCommandRequestId_ = 1;
		bool allNodeReadyNotified_ = false;
		bool openGateNotified_ = false;
	};
}
