#include "network/inner/InnerNetwork.h"
#include "core/Logger.h"
#include <zmq.h>
#include <algorithm>
#include <cerrno>
#include <cstring>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <utility>

namespace de::server::engine::network
{
	InnerNetworkSession::InnerNetworkSession(SessionId sessionId, InnerNetworkSessionState initState, std::string remoteServerID)
		: SessionId_(sessionId)
		, SessionState_(initState)
		, RemoteServerID(std::move(remoteServerID))
	{
	}


	InnerNetwork::InnerNetwork(asio::io_context& ioContext, InnerNetworkCallbacks callbacks)
		: IOContext_(ioContext)
		, Callbacks_(callbacks)
		, Listening_(false)
	{
	}
}
