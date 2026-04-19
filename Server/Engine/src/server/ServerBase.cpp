#include "ServerBase.h"

#include "core/Logger.h"

#include <utility>

namespace de::server::engine
{
	ServerBase::ServerBase(std::string serverId)
		: serverId_(std::move(serverId))
		, ioContext_()
		, workGuard_(asio::make_work_guard(ioContext_))
	{
	}

	const std::string& ServerBase::GetServerId() const
	{
		return serverId_;
	}

	asio::io_context& ServerBase::GetIoContext()
	{
		return ioContext_;
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
