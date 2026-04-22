#pragma once

#include "ServerBase.h"
#include "config/ClusterConfig.h"
#include "core/ProcessPerformance.h"

#include <unordered_map>

namespace de::server::engine
{
	class GMServer : public ServerBase
	{
	public:
		GMServer(std::string serverId, const config::ClusterConfig& clusterConfig);
		~GMServer() override;
		void Init() override;
		void Uninit() override;

	private:
		const config::TelnetConfig& GetTelnetConfig() const override;
		const config::NetworkConfig& GetInnerNetworkConfig() const override;
		void OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data) override;
		void OnInnerDisconnect(const std::string& serverId) override;

		config::GMConfig config_;
		std::unordered_map<std::string, ProcessPerformanceSnapshot> latestNodePerformanceSnapshots_;
	};
}
