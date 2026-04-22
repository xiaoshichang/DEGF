#pragma once

#include "config/ClusterConfig.h"
#include "core/BoostAsio.h"

#include <memory>
#include <string>

namespace de::server::engine
{
	class TelnetService;
	class TimerManager;

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
		TimerManager& GetTimerManager();
		void Stop();
		virtual const config::TelnetConfig& GetTelnetConfig() const = 0;

	private:
		void InitTelnet();
		void UninitTelnet();
		void InitTimerManager();
		void UninitTimerManager();

		std::string serverId_;
		asio::io_context ioContext_;
		asio::executor_work_guard<asio::io_context::executor_type> workGuard_;
		std::unique_ptr<TelnetService> telnetService_;
		std::unique_ptr<TimerManager> timerManager_;
	};
}
