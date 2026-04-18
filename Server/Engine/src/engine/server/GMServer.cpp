#include "GMServer.h"
#include "core/Logger.h"
#include <thread>
#include <utility>

namespace de::server::engine
{
	GMServer::GMServer(std::string serverId, config::GMConfig config)
		: ServerBase(std::move(serverId))
		, config_(std::move(config))
	{
	}

	GMServer::~GMServer()
	{
	}

	void GMServer::Init()
	{
		Logger::Info("GMServer", "Init");
	}

	void GMServer::Run()
	{
		Logger::Info("GMServer", "Run");
		while (true)
		{
			std::this_thread::sleep_for(std::chrono::seconds(1));
		}
	}

	void GMServer::Uninit()
	{
		Logger::Info("GMServer", "Uninit");
	}
}
