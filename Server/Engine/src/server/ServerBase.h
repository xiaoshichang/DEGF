#pragma once

#include <asio/executor_work_guard.hpp>
#include <asio/io_context.hpp>

#include <string>

namespace de::server::engine
{
	class ServerBase
	{
	public:
		explicit ServerBase(std::string serverId);
		virtual ~ServerBase() = default;

		const std::string& GetServerId() const;
		virtual void Init() = 0;
		virtual void Run();
		virtual void Uninit() = 0;

	protected:
		asio::io_context& GetIoContext();
		void Stop();

	private:
		std::string serverId_;
		asio::io_context ioContext_;
		asio::executor_work_guard<asio::io_context::executor_type> workGuard_;
	};
}
