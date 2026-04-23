#pragma once

#include "config/ClusterConfig.h"
#include "core/ProcessPerformance.h"
#include "http/HttpService.h"

#include <string>
#include <unordered_map>

namespace de::server::engine
{
	class GMHttpHandler
	{
	public:
		GMHttpHandler(std::string serverId, const config::ClusterConfig& clusterConfig);

		HttpResponse HandleRequest(const HttpRequest& request) const;
		void UpdateNodePerformanceSnapshot(const std::string& serverId, const ProcessPerformanceSnapshot& snapshot);
		void ClearNodePerformanceSnapshot(const std::string& serverId);

	private:
		std::string BuildPerformanceResponse() const;

		std::string serverId_;
		const config::ClusterConfig& clusterConfig_;
		std::unordered_map<std::string, ProcessPerformanceSnapshot> latestNodePerformanceSnapshots_;
	};
}
