#pragma once

#include "config/ClusterConfig.h"
#include "core/BoostAsio.h"
#include "managed/ManagedRuntimeService.h"
#include "network/inner/InnerNetwork.h"
#include "timer/TimerManager.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace de::server::engine
{
	class TelnetService;

	class ServerBase
	{
	public:
		ServerBase(std::string serverId, std::string configPath, config::ClusterConfig clusterConfig);
		virtual ~ServerBase();

		const std::string& GetServerId() const;
		virtual void Init();
		virtual void Run();
		virtual void Uninit();

	protected:
		asio::io_context& GetIoContext();
		TimerManager& GetTimerManager();
		network::InnerNetwork& GetInnerNetwork();
		void Stop();
		const config::ClusterConfig& GetClusterConfig() const;
		const std::string& GetConfigPath() const;
		virtual const config::TelnetConfig& GetTelnetConfig() const = 0;
		virtual const config::NetworkConfig& GetInnerNetworkConfig() const = 0;
		virtual void OnInnerRegistered(const std::string& serverId);
		virtual void OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data);
		virtual void OnInnerDisconnect(const std::string& serverId);

	private:
		void InitInnerNetwork();
		void UninitInnerNetwork();
		void OnInnerNetworkRegistered(const std::string& serverId);
		void OnInnerNetworkReceive(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data);
		void OnInnerNetworkDisconnect(const std::string& serverId);

		void InitTelnet();
		void UninitTelnet();
		void InitManagedRuntimeService();
		void UninitManagedRuntimeService();
		void InitTimerManager();
		void UninitTimerManager();

		std::string serverId_;
		std::string configPath_;
		config::ClusterConfig clusterConfig_;
		asio::io_context ioContext_;
		asio::executor_work_guard<asio::io_context::executor_type> workGuard_;
		std::unique_ptr<network::InnerNetwork> innerNetwork_;
		std::unique_ptr<TelnetService> telnetService_;
		std::unique_ptr<ManagedRuntimeService> managedRuntimeService_;
		std::unique_ptr<TimerManager> timerManager_;
	};
}
