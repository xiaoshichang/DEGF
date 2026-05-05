#include "server/gm/GMServer.h"

#include "core/Logger.h"
#include "http/HttpService.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"
#include "server/gm/GMHttpHandler.h"

#include <algorithm>
#include <boost/json.hpp>
#include <chrono>
#include <cctype>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		constexpr auto kGmCommandTimeout = std::chrono::seconds(5);

		std::string TrimCommand(std::string_view value)
		{
			std::size_t begin = 0;
			while (begin < value.size() && std::isspace(static_cast<unsigned char>(value[begin])) != 0)
			{
				++begin;
			}

			std::size_t end = value.size();
			while (end > begin && std::isspace(static_cast<unsigned char>(value[end - 1])) != 0)
			{
				--end;
			}

			return std::string(value.substr(begin, end - begin));
		}

		std::string ToLowerCommand(std::string_view value)
		{
			std::string result(value);
			std::transform(
				result.begin(),
				result.end(),
				result.begin(),
				[](unsigned char character)
				{
					return static_cast<char>(std::tolower(character));
				}
			);
			return result;
		}

		bool TryParseGmTelnetCommandResult(std::string_view payload, std::uint64_t& requestId, std::string& response)
		{
			requestId = 0;
			response.clear();
			boost::json::error_code error;
			const auto value = boost::json::parse(payload, error);
			if (error || !value.is_object())
			{
				return false;
			}

			const auto& object = value.as_object();
			const auto* requestIdValue = object.if_contains("requestId");
			const auto* responseValue = object.if_contains("response");
			if (requestIdValue == nullptr || responseValue == nullptr || !responseValue->is_string())
			{
				return false;
			}

			if (requestIdValue->is_uint64())
			{
				requestId = requestIdValue->as_uint64();
			}
			else if (requestIdValue->is_int64() && requestIdValue->as_int64() > 0)
			{
				requestId = static_cast<std::uint64_t>(requestIdValue->as_int64());
			}
			else
			{
				return false;
			}

			response = responseValue->as_string().c_str();
			return requestId != 0;
		}
	}

	GMServer::GMServer(std::string serverId, std::string configPath, const config::ClusterConfig& clusterConfig)
		: ServerBase(serverId, std::move(configPath), clusterConfig)
		, config_(clusterConfig.gm)
	{
	}

	GMServer::~GMServer()
	{
	}

	void GMServer::Init()
	{
		ServerBase::Init();
		InitHttp();
		Logger::Info("GMServer", "Init");
	}

	void GMServer::Uninit()
	{
		Logger::Info("GMServer", "Uninit");
		UninitHttp();
		ServerBase::Uninit();
	}

	const config::TelnetConfig& GMServer::GetTelnetConfig() const
	{
		return config_.telnet;
	}

	const config::NetworkConfig& GMServer::GetInnerNetworkConfig() const
	{
		return config_.innerNetwork;
	}

	void GMServer::OnInnerRegistered(const std::string& serverId)
	{
		registeredNodeServerIds_.insert(serverId);
		ServerBase::OnInnerRegistered(serverId);
		TryNotifyAllNodeReady();
	}

	void GMServer::OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		switch (static_cast<network::MessageID::SS>(messageId))
		{
		case network::MessageID::SS::HeartBeatWithDataNtf:
			HandleHeartBeatWithDataNtf(serverId, data);
			return;

		case network::MessageID::SS::GameReadyNtf:
			HandleGameReadyNtf(serverId);
			return;

		case network::MessageID::SS::GmTotalEntityCountRsp:
			HandleGmTotalEntityCountRsp(serverId, data);
			return;

		default:
			ServerBase::OnInnerMessage(serverId, messageId, data);
			return;
		}
	}

	void GMServer::HandleHeartBeatWithDataNtf(const std::string& serverId, const std::vector<std::byte>& data)
	{
		network::HeartBeatWithDataNtfMessage heartBeatMessage;
		if (!network::HeartBeatWithDataNtfMessage::TryDeserialize(data.data(), data.size(), heartBeatMessage))
		{
			Logger::Warn("GMServer", "Received invalid heartbeat payload from " + serverId + ".");
			return;
		}

		if (httpHandler_ != nullptr)
		{
			httpHandler_->UpdateNodePerformanceSnapshot(serverId, heartBeatMessage.performance);
		}

		Logger::Debug(
			"GMServer",
			"Updated heartbeat snapshot from " + serverId + ", workingSetBytes=" + std::to_string(heartBeatMessage.performance.workingSetBytes)
		);
	}

	void GMServer::HandleGameReadyNtf(const std::string& serverId)
	{
		readyGameServerIds_.insert(serverId);
		TryNotifyOpenGate();
	}

	void GMServer::HandleGmTotalEntityCountRsp(const std::string& serverId, const std::vector<std::byte>& data)
	{
		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			Logger::Warn("GMServer", "Managed runtime service is not initialized.");
			return;
		}

		std::string telnetResponse;
		if (!managedRuntimeService->HandleGmTotalEntityCountRsp(serverId, data, telnetResponse))
		{
			Logger::Warn("GMServer", "Failed to handle GmTotalEntityCountRsp from " + serverId + ".");
			return;
		}

		if (telnetResponse.empty())
		{
			return;
		}

		std::uint64_t requestId = 0;
		std::string response;
		if (!TryParseGmTelnetCommandResult(telnetResponse, requestId, response))
		{
			Logger::Warn("GMServer", "Managed runtime returned invalid GM telnet command result.");
			return;
		}

		CompleteTotalEntityCountCommand(requestId, std::move(response));
	}

	void GMServer::OnInnerDisconnect(const std::string& serverId)
	{
		registeredNodeServerIds_.erase(serverId);
		readyGameServerIds_.erase(serverId);
		if (httpHandler_ != nullptr)
		{
			httpHandler_->ClearNodePerformanceSnapshot(serverId);
		}

		allNodeReadyNotified_ = false;
		openGateNotified_ = false;
		ServerBase::OnInnerDisconnect(serverId);
	}

	TelnetCommandResult GMServer::OnTelnetCommand(std::uint64_t sessionId, std::string_view commandLine)
	{
		const auto command = ToLowerCommand(TrimCommand(commandLine));
		if (command == "total_entity_count" || command == "totalentitycount")
		{
			return BeginTotalEntityCountCommand(sessionId);
		}

		return ServerBase::OnTelnetCommand(sessionId, commandLine);
	}

	void GMServer::InitHttp()
	{
		if (httpService_ != nullptr || config_.http.listenEndpoint.host.empty() || config_.http.listenEndpoint.port == 0)
		{
			return;
		}

		httpHandler_ = std::make_unique<GMHttpHandler>(GetServerId(), GetClusterConfig());
		httpService_ = std::make_unique<HttpService>(
			GetIoContext(),
			GetServerId(),
			[this](const HttpRequest& request)
			{
				return httpHandler_->HandleRequest(request);
			}
		);
		httpService_->Start(config_.http);
	}

	void GMServer::UninitHttp()
	{
		if (httpService_ == nullptr)
		{
			return;
		}

		httpService_->Stop();
		httpService_.reset();
		httpHandler_.reset();
	}

	TelnetCommandResult GMServer::BeginTotalEntityCountCommand(std::uint64_t sessionId)
	{
		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			return { "managed runtime service is not initialized", false, false, false };
		}

		auto gameServerIds = GetGameServerIds();
		if (gameServerIds.empty())
		{
			return { "no game server configured", false, false, false };
		}

		const auto requestId = nextGmCommandRequestId_++;
		if (requestId == 0)
		{
			nextGmCommandRequestId_ = 1;
		}

		if (!managedRuntimeService->BeginGmTotalEntityCountCommand(requestId, gameServerIds))
		{
			return { "failed to begin TotalEntityCount command", false, false, false };
		}

		network::GmTotalEntityCountReqMessage requestMessage;
		requestMessage.requestId = requestId;
		const auto payload = requestMessage.Serialize();

		auto& timerManager = GetTimerManager();
		const auto timeoutTimerId = timerManager.AddTimer(
			kGmCommandTimeout,
			[this, requestId](TimerManager::TimerID)
			{
				FailTotalEntityCountCommand(
					requestId,
					"TotalEntityCount timeout, requestId=" + std::to_string(requestId)
				);
			},
			false
		);

		pendingGmCommands_.emplace(
			requestId,
			PendingGmCommand{
				requestId,
				sessionId,
				timeoutTimerId
			}
		);

		auto& innerNetwork = GetInnerNetwork();
		for (const auto& gameServerId : gameServerIds)
		{
			if (!innerNetwork.Send(gameServerId, static_cast<std::uint32_t>(network::MessageID::SS::GmTotalEntityCountReq), payload))
			{
				pendingGmCommands_.erase(requestId);
				GetTimerManager().CancelTimer(timeoutTimerId);
				managedRuntimeService->CancelGmCommand(requestId);
				return {
					"failed to send TotalEntityCount request to " + gameServerId + ", requestId=" + std::to_string(requestId),
					false,
					false,
					false
				};
			}
		}

		return {
			"TotalEntityCount started, requestId=" + std::to_string(requestId),
			false,
			false,
			true
		};
	}

	void GMServer::CompleteTotalEntityCountCommand(std::uint64_t requestId, std::string response)
	{
		const auto commandIterator = pendingGmCommands_.find(requestId);
		if (commandIterator == pendingGmCommands_.end())
		{
			Logger::Warn("GMServer", "TotalEntityCount command not found, requestId=" + std::to_string(requestId) + ".");
			return;
		}

		const auto command = commandIterator->second;
		pendingGmCommands_.erase(commandIterator);
		GetTimerManager().CancelTimer(command.timeoutTimerId);
		ReplyToTelnetSession(command.telnetSessionId, std::move(response));
	}

	void GMServer::FailTotalEntityCountCommand(std::uint64_t requestId, std::string response)
	{
		const auto commandIterator = pendingGmCommands_.find(requestId);
		if (commandIterator == pendingGmCommands_.end())
		{
			return;
		}

		const auto command = commandIterator->second;
		pendingGmCommands_.erase(commandIterator);
		GetTimerManager().CancelTimer(command.timeoutTimerId);

		if (auto* managedRuntimeService = GetManagedRuntimeService(); managedRuntimeService != nullptr)
		{
			managedRuntimeService->CancelGmCommand(requestId);
		}

		ReplyToTelnetSession(command.telnetSessionId, std::move(response));
	}

	std::vector<std::string> GMServer::GetGameServerIds() const
	{
		std::vector<std::string> gameServerIds;
		for (const auto& [serverId, gameConfig] : GetClusterConfig().game)
		{
			(void)gameConfig;
			gameServerIds.push_back(serverId);
		}

		return gameServerIds;
	}

	void GMServer::TryNotifyAllNodeReady()
	{
		if (allNodeReadyNotified_)
		{
			return;
		}

		const auto& clusterConfig = GetClusterConfig();
		for (const auto& [serverId, gateConfig] : clusterConfig.gate)
		{
			(void)gateConfig;
			if (registeredNodeServerIds_.find(serverId) == registeredNodeServerIds_.end())
			{
				return;
			}
		}

		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (registeredNodeServerIds_.find(serverId) == registeredNodeServerIds_.end())
			{
				return;
			}
		}

		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			Logger::Warn("GMServer", "Managed runtime service is not initialized.");
			return;
		}

		std::vector<std::string> gameServerIds;
		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			gameServerIds.push_back(serverId);
		}

		std::vector<std::byte> payload;
		if (!managedRuntimeService->TryBuildStubDistributePayload(gameServerIds, payload))
		{
			Logger::Warn("GMServer", "Failed to build stub distribute payload before AllNodeReadyNtf.");
			return;
		}

		auto& innerNetwork = GetInnerNetwork();
		for (const auto& [serverId, gateConfig] : clusterConfig.gate)
		{
			(void)gateConfig;
			if (!innerNetwork.Send(serverId, static_cast<std::uint32_t>(network::MessageID::SS::StubDistributeNtf), payload))
			{
				Logger::Warn("GMServer", "Failed to send StubDistributeNtf to " + serverId + ".");
				return;
			}
		}

		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (!innerNetwork.Send(serverId, static_cast<std::uint32_t>(network::MessageID::SS::StubDistributeNtf), payload))
			{
				Logger::Warn("GMServer", "Failed to send StubDistributeNtf to " + serverId + ".");
				return;
			}
		}

		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (!innerNetwork.Send(serverId, static_cast<std::uint32_t>(network::MessageID::SS::AllNodeReadyNtf), {}))
			{
				Logger::Warn("GMServer", "Failed to send AllNodeReadyNtf to " + serverId + ".");
				return;
			}
		}

		allNodeReadyNotified_ = true;
		Logger::Info(
			"GMServer",
			"Sent StubDistributeNtf to all game/gate nodes and AllNodeReadyNtf to all game nodes with stub distribute payload size " + std::to_string(payload.size()) + "."
		);
	}

	void GMServer::TryNotifyOpenGate()
	{
		if (openGateNotified_)
		{
			return;
		}

		const auto& clusterConfig = GetClusterConfig();
		for (const auto& [serverId, gameConfig] : clusterConfig.game)
		{
			(void)gameConfig;
			if (readyGameServerIds_.find(serverId) == readyGameServerIds_.end())
			{
				return;
			}
		}

		auto& innerNetwork = GetInnerNetwork();
		for (const auto& [serverId, gateConfig] : clusterConfig.gate)
		{
			(void)gateConfig;
			if (!innerNetwork.Send(serverId, static_cast<std::uint32_t>(network::MessageID::SS::OpenGateNtf), {}))
			{
				Logger::Warn("GMServer", "Failed to send OpenGateNtf to " + serverId + ".");
				return;
			}
		}

		openGateNotified_ = true;
		Logger::Info("GMServer", "Sent OpenGateNtf to all gate nodes.");
	}

}
