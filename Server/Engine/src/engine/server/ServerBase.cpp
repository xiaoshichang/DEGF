#include "ServerBase.h"

#include <utility>

namespace de::server::engine
{
	ServerBase::ServerBase(std::string serverId)
		: serverId_(std::move(serverId))
	{
	}

	const std::string& ServerBase::GetServerId() const
	{
		return serverId_;
	}
}
