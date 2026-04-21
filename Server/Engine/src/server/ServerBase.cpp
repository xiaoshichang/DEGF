#include "ServerBase.h"

#include "core/Logger.h"
#include "telnet/TelnetService.h"

#include <utility>

namespace de::server::engine
{
	ServerBase::ServerBase(std::string serverId)
		: serverId_(std::move(serverId))
		, ioContext_()
		, workGuard_(asio::make_work_guard(ioContext_))
	{
	}

	ServerBase::~ServerBase() = default;

	const std::string& ServerBase::GetServerId() const
	{
		return serverId_;
	}

	asio::io_context& ServerBase::GetIoContext()
	{
		return ioContext_;
	}

	void ServerBase::Init()
	{
		InitTelnet();
	}

	void ServerBase::Uninit()
	{
		UninitTelnet();
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
