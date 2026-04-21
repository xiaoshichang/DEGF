#pragma once

#include "config/ClusterConfig.h"

#include <asio/io_context.hpp>
#include <asio/ip/tcp.hpp>

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <string_view>
#include <unordered_map>

namespace de::server::engine
{
	struct TelnetCommandResult
	{
		std::string response;
		bool closeSession = false;
		bool stopServer = false;
	};

	class TelnetService
	{
	public:
		using StopCallback = std::function<void()>;

		TelnetService(asio::io_context& ioContext, std::string serverId, StopCallback stopCallback = {});
		~TelnetService();

		void Start(const config::TelnetConfig& config);
		void Stop();

		bool IsRunning() const;
		std::uint16_t GetListenPort() const;
		TelnetCommandResult HandleCommand(std::string_view commandLine);

	private:
		class Session;

		struct ProcessMetricsSnapshot
		{
			std::uint32_t processId = 0;
			std::uint64_t uptimeMs = 0;
			std::uint64_t workingSetBytes = 0;
			std::uint64_t privateBytes = 0;
			std::uint64_t kernelTimeMs = 0;
			std::uint64_t userTimeMs = 0;
			std::uint32_t handleCount = 0;
			std::size_t activeSessions = 0;
			std::size_t totalSessions = 0;
			std::size_t totalCommands = 0;
		};

		void StartAccept();
		void OnSessionClosed(std::uint64_t sessionId);
		ProcessMetricsSnapshot CollectProcessMetrics() const;
		std::string BuildHelpResponse() const;
		std::string BuildStatusResponse() const;
		std::string BuildMetricsResponse() const;

		asio::io_context& ioContext_;
		asio::ip::tcp::acceptor acceptor_;
		std::string serverId_;
		StopCallback stopCallback_;
		bool running_ = false;
		std::uint16_t listenPort_ = 0;
		std::chrono::steady_clock::time_point startTime_;
		std::uint64_t nextSessionId_ = 1;
		std::unordered_map<std::uint64_t, std::shared_ptr<Session>> sessions_;
		std::size_t totalSessions_ = 0;
		std::size_t totalCommands_ = 0;
	};
}
