#include "GateServer.h"

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
	}

	void GateServer::Run()
	{
	}

	void GateServer::Uninit()
	{
	}
}
