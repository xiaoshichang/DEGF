#include "GateServer.h"

#include "core/Logger.h"

#include <utility>

namespace de::server::engine
{
	GateServer::GateServer(std::string serverId, config::GateConfig config)
		: ServerBase(std::move(serverId))
		, config_(std::move(config))
	{
	}

	GateServer::~GateServer()
	{
	}

	void GateServer::Init()
	{
		Logger::Info("GateServer", "Init");
	}

	void GateServer::Run()
	{
		Logger::Info("GateServer", "Run");
	}

	void GateServer::Uninit()
	{
		Logger::Info("GateServer", "Uninit");
	}
}
