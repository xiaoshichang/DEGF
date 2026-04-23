#include "server/gm/GMHttpHandler.h"

#include <boost/json.hpp>

#include <string>
#include <string_view>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		boost::json::object BuildSnapshotJson(std::string_view serverId, const ProcessPerformanceSnapshot& snapshot)
		{
			return boost::json::object{
				{ "serverId", std::string(serverId) },
				{ "workingSetBytes", snapshot.workingSetBytes }
			};
		}

		boost::json::object BuildMissingSnapshotJson(std::string_view serverId)
		{
			return boost::json::object{
				{ "serverId", std::string(serverId) },
				{ "workingSetBytes", static_cast<std::uint64_t>(0) }
			};
		}
	}

	GMHttpHandler::GMHttpHandler(std::string serverId, const config::ClusterConfig& clusterConfig)
		: serverId_(std::move(serverId))
		, clusterConfig_(clusterConfig)
	{
	}

	HttpResponse GMHttpHandler::HandleRequest(const HttpRequest& request) const
	{
		if (request.method != "GET")
		{
			return HttpResponse{
				405,
				"Method Not Allowed",
				"application/json; charset=utf-8",
				R"({"error":"method not allowed"})"
			};
		}

		if (request.target == "/performance" || request.target == "/api/performance")
		{
			return HttpResponse{
				200,
				"OK",
				"application/json; charset=utf-8",
				BuildPerformanceResponse()
			};
		}

		return HttpResponse{
			404,
			"Not Found",
			"application/json; charset=utf-8",
			R"({"error":"not found"})"
		};
	}

	void GMHttpHandler::UpdateNodePerformanceSnapshot(const std::string& serverId, const ProcessPerformanceSnapshot& snapshot)
	{
		latestNodePerformanceSnapshots_[serverId] = snapshot;
	}

	void GMHttpHandler::ClearNodePerformanceSnapshot(const std::string& serverId)
	{
		latestNodePerformanceSnapshots_.erase(serverId);
	}

	std::string GMHttpHandler::BuildPerformanceResponse() const
	{
		boost::json::object nodes;

		nodes.emplace(serverId_, BuildSnapshotJson(serverId_, CollectProcessPerformanceSnapshot()));

		for (const auto& [nodeServerId, gateConfig] : clusterConfig_.gate)
		{
			(void)gateConfig;

			const auto snapshotIterator = latestNodePerformanceSnapshots_.find(nodeServerId);
			if (snapshotIterator == latestNodePerformanceSnapshots_.end())
			{
				nodes.emplace(nodeServerId, BuildMissingSnapshotJson(nodeServerId));
				continue;
			}

			nodes.emplace(nodeServerId, BuildSnapshotJson(nodeServerId, snapshotIterator->second));
		}

		for (const auto& [nodeServerId, gameConfig] : clusterConfig_.game)
		{
			(void)gameConfig;

			const auto snapshotIterator = latestNodePerformanceSnapshots_.find(nodeServerId);
			if (snapshotIterator == latestNodePerformanceSnapshots_.end())
			{
				nodes.emplace(nodeServerId, BuildMissingSnapshotJson(nodeServerId));
				continue;
			}

			nodes.emplace(nodeServerId, BuildSnapshotJson(nodeServerId, snapshotIterator->second));
		}

		boost::json::object payload;
		payload.emplace("nodes", std::move(nodes));
		return boost::json::serialize(payload);
	}
}
