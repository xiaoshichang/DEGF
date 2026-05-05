#include "ServerBase.h"

#include "core/Logger.h"
#include "managed/ManagedRuntimeService.h"
#include "network/inner/InnerNetwork.h"
#include "network/protocal/MessageID.h"
#include "telnet/TelnetService.h"
#include "timer/TimerManager.h"

#include <chrono>
#include <algorithm>
#include <cctype>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>

namespace de::server::engine
{
	namespace
	{
		std::string TrimTelnetCommand(std::string_view value)
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

		std::string ToLowerTelnetCommand(std::string_view value)
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

		std::string BuildInnerNetworkEndpoint(const config::EndpointConfig& endpointConfig)
		{
			if (endpointConfig.host.empty() || endpointConfig.port == 0)
			{
				throw std::invalid_argument("Inner network endpoint is invalid.");
			}

			return "tcp://" + endpointConfig.host + ":" + std::to_string(endpointConfig.port);
		}
	}

	ServerBase::ServerBase(std::string serverId, std::string configPath, config::ClusterConfig clusterConfig)
		: serverId_(std::move(serverId))
		, configPath_(std::move(configPath))
		, clusterConfig_(std::move(clusterConfig))
		, ioContext_()
		, workGuard_(asio::make_work_guard(ioContext_))
	{
	}

	ServerBase::~ServerBase() = default;

	const std::string& ServerBase::GetServerId() const
	{
		return serverId_;
	}

	const config::ClusterConfig& ServerBase::GetClusterConfig() const
	{
		return clusterConfig_;
	}

	const std::string& ServerBase::GetConfigPath() const
	{
		return configPath_;
	}

	asio::io_context& ServerBase::GetIoContext()
	{
		return ioContext_;
	}

	TimerManager& ServerBase::GetTimerManager()
	{
		if (timerManager_ == nullptr)
		{
			throw std::runtime_error("TimerManager is not initialized.");
		}

		return *timerManager_;
	}

	network::InnerNetwork& ServerBase::GetInnerNetwork()
	{
		if (innerNetwork_ == nullptr)
		{
			throw std::runtime_error("InnerNetwork is not initialized.");
		}

		return *innerNetwork_;
	}

	ManagedRuntimeService* ServerBase::GetManagedRuntimeService()
	{
		return managedRuntimeService_.get();
	}

	void ServerBase::Init()
	{
		InitTimerManager();
		InitInnerNetwork();
		InitTelnet();
		InitManagedRuntimeService();
	}

	void ServerBase::Uninit()
	{
		UninitManagedRuntimeService();
		UninitTelnet();
		UninitInnerNetwork();
		UninitTimerManager();
	}

	void ServerBase::InitInnerNetwork()
	{
		if (innerNetwork_ != nullptr)
		{
			return;
		}

		innerNetwork_ = std::make_unique<network::InnerNetwork>(
			serverId_,
			ioContext_,
			network::InnerNetworkCallbacks{
				[this](const std::string& remoteServerId)
				{
					OnInnerNetworkRegistered(remoteServerId);
				},
				[this](const std::string& remoteServerId, std::uint32_t messageId, const std::vector<std::byte>& data)
				{
					OnInnerNetworkReceive(remoteServerId, messageId, data);
				},
				[this](const std::string& remoteServerId)
				{
					OnInnerNetworkDisconnect(remoteServerId);
				}
			}
		);

		const auto listenEndpoint = BuildInnerNetworkEndpoint(GetInnerNetworkConfig().listenEndpoint);
		if (!innerNetwork_->Listen(listenEndpoint))
		{
			throw std::runtime_error("Failed to start inner network listen endpoint: " + listenEndpoint);
		}
	}

	void ServerBase::UninitInnerNetwork()
	{
		innerNetwork_.reset();
	}

	void ServerBase::OnInnerNetworkRegistered(const std::string& serverId)
	{
		OnInnerRegistered(serverId);
	}

	void ServerBase::OnInnerNetworkReceive(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		OnInnerMessage(serverId, messageId, data);
	}

	void ServerBase::OnInnerNetworkDisconnect(const std::string& serverId)
	{
		OnInnerDisconnect(serverId);
	}

	void ServerBase::OnInnerRegistered(const std::string& serverId)
	{
		Logger::Info("ServerBase", "Inner network registered " + serverId + ".");
	}

	void ServerBase::OnInnerMessage(const std::string& serverId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		switch (static_cast<network::MessageID::SS>(messageId))
		{
		case network::MessageID::SS::HeartBeatWithDataNtf:
			Logger::Debug("ServerBase", "Received heartbeat from " + serverId + ".");
			return;

		default:
			Logger::Warn(
				"ServerBase",
				"Unhandled inner message from " + serverId + ", messageId=" + std::to_string(messageId) + ", payload=" + std::to_string(data.size())
			);
			return;
		}
	}

	void ServerBase::OnInnerDisconnect(const std::string& serverId)
	{
		Logger::Warn("ServerBase", "Inner network disconnected from " + serverId + ".");
	}

	TelnetCommandResult ServerBase::OnTelnetCommand(std::uint64_t sessionId, std::string_view commandLine)
	{
		(void)sessionId;
		if (telnetService_ == nullptr)
		{
			return {};
		}

		const auto trimmedCommand = TrimTelnetCommand(commandLine);
		if (trimmedCommand.empty())
		{
			return {};
		}

		const auto normalizedCommand = ToLowerTelnetCommand(trimmedCommand);
		if (trimmedCommand.front() == '$')
		{
			return telnetService_->HandleCommand(std::string_view(trimmedCommand).substr(1));
		}

		auto* managedRuntimeService = GetManagedRuntimeService();
		if (managedRuntimeService == nullptr)
		{
			return { "managed runtime service is not initialized", false, false, false };
		}

		std::string response;
		if (!managedRuntimeService->ExecuteTelnetCSharp(trimmedCommand, response))
		{
			return { "failed to execute C# code", false, false, false };
		}

		return { response, false, false, false };
	}

	bool ServerBase::ReplyToTelnetSession(std::uint64_t sessionId, std::string response, bool closeSession)
	{
		if (telnetService_ == nullptr)
		{
			return false;
		}

		return telnetService_->ReplyToSession(sessionId, std::move(response), closeSession);
	}

	void ServerBase::InitTelnet()
	{
		const auto& telnetConfig = GetTelnetConfig();
		if (telnetConfig.port == 0 || telnetService_ != nullptr)
		{
			return;
		}

		telnetService_ = std::make_unique<TelnetService>(
			ioContext_,
			serverId_,
			[this]()
			{
				Stop();
			}
		);
		telnetService_->SetCommandHandler(
			[this](std::uint64_t sessionId, std::string_view commandLine)
			{
				return OnTelnetCommand(sessionId, commandLine);
			}
		);
		telnetService_->Start(telnetConfig);
	}

	void ServerBase::UninitTelnet()
	{
		if (telnetService_ == nullptr)
		{
			return;
		}

		telnetService_->Stop();
		telnetService_.reset();
	}

	void ServerBase::InitManagedRuntimeService()
	{
		if (managedRuntimeService_ != nullptr)
		{
			return;
		}

		managedRuntimeService_ = std::make_unique<ManagedRuntimeService>(GetTimerManager());
		managedRuntimeService_->Start(
			GetServerId(),
			GetConfigPath(),
			GetClusterConfig().managed
		);
	}

	void ServerBase::UninitManagedRuntimeService()
	{
		if (managedRuntimeService_ == nullptr)
		{
			return;
		}

		managedRuntimeService_->Stop();
		managedRuntimeService_.reset();
	}

	void ServerBase::InitTimerManager()
	{
		if (timerManager_ != nullptr)
		{
			return;
		}

		timerManager_ = std::make_unique<TimerManager>(ioContext_);
	}

	void ServerBase::UninitTimerManager()
	{
		if (timerManager_ == nullptr)
		{
			return;
		}

		timerManager_->Shutdown();
		timerManager_.reset();
	}

	void ServerBase::Run()
	{
		Logger::Info("ServerBase", "Starting io_context.");
		ioContext_.run();
		Logger::Info("ServerBase", "io_context stopped.");
	}

	void ServerBase::Stop()
	{
		workGuard_.reset();
		ioContext_.stop();
	}
}
