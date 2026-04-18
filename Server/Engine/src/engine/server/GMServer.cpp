#include "GMServer.h"

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
	}

	void GMServer::Run()
	{
	}

	void GMServer::Uninit()
	{
	}
}
