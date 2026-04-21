#pragma once

#include "config/ClusterConfig.h"

#include <asio/executor_work_guard.hpp>
#include <asio/io_context.hpp>

#include <memory>
#include <string>

namespace de::server::engine
{
	class TelnetService;

	class ServerBase
	{
	public:
		explicit ServerBase(std::string serverId);
		virtual ~ServerBase();

		const std::string& GetServerId() const;
		virtual void Init();
		virtual void Run();
		virtual void Uninit();

	protected:
		asio::io_context& GetIoContext();
		void Stop();
		virtual const config::TelnetConfig& GetTelnetConfig() const = 0;

	private:
		void InitTelnet();
		void StopRuntimeServices();

		std::string serverId_;
		asio::io_context ioContext_;
		asio::executor_work_guard<asio::io_context::executor_type> workGuard_;
		std::unique_ptr<TelnetService> telnetService_;
	};
}
