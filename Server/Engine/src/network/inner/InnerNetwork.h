/*
InnerNetwork handles cluster-internal messaging.

- It listens for inbound node connections.
- It actively connects to peer nodes when needed.
- It owns InnerNetworkSession instances and routes session lifecycle events.
*/

#pragma once

#include "core/BoostAsio.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "network/inner/InnerNetworkSession.h"

namespace de::server::engine::network
{
	class InnerNetworkWorker;
}

namespace de::server::engine::network
{
	typedef void (*OnInnerNetworkReceiveCallback)(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data);
	typedef void (*OnInnerNetworkDisconnectCallback)(const std::string& serverID);

	struct InnerNetworkCallbacks
	{
		OnInnerNetworkReceiveCallback OnReceive;
		OnInnerNetworkDisconnectCallback OnDisconnect;
	};

	class InnerNetwork
	{
	public:
		using SessionId = InnerNetworkSession::SessionId;

		InnerNetwork(const std::string& serverID, asio::io_context& ioContext, InnerNetworkCallbacks callbacks);
		~InnerNetwork();
		InnerNetwork(const InnerNetwork&) = delete;
		InnerNetwork& operator=(const InnerNetwork&) = delete;
		bool Listen(const std::string& endpoint);
		InnerNetworkSession* ConnectTo(const std::string& endpoint);
		bool Send(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data);
		bool ActiveDisconnect(SessionId sessionId);

	private:
		friend class InnerNetworkWorker;

		void PostReceiveCallback(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data) const;
		void PostDisconnectCallback(const std::string& serverID) const;

	private:
		std::string ServerID_;
		asio::io_context& IOContext_;
		InnerNetworkCallbacks Callbacks_;
		std::unique_ptr<InnerNetworkWorker> Worker_;
	};
}
