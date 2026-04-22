/*
InnerNetwork handles cluster-internal messaging.

- It listens for inbound node connections.
- It actively connects to peer nodes when needed.
- It owns InnerNetworkSession instances and routes session lifecycle events.
*/

#pragma once

#include "core/BoostAsio.h"

#include <azmq/socket.hpp>

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

#include "network/inner/InnerNetworkSession.h"

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
		struct ActiveSessionEntry
		{
			std::unique_ptr<InnerNetworkSession> Session;
			std::unique_ptr<azmq::socket> Socket;
		};

		InnerNetworkSession* CreateConnectSession(const std::string& endpoint);
		InnerNetworkSession* CreateListenSession();
		void DestroyConnectSession(SessionId sessionId);
		void DestroyListenSession(SessionId sessionId);
		void DestroySessions();
		ActiveSessionEntry* FindConnectSession(SessionId sessionId);
		InnerNetworkSession* FindListenSession(SessionId sessionId);
		InnerNetworkSession* ResolveListenSession(std::uint32_t messageID, const InnerNetworkSession::RoutingId& routingId, const std::vector<std::byte>& data);
		void RegisterSession(InnerNetworkSession* session, const std::string& serverID);
		std::string GetSessionServerID(SessionId sessionId) const;
		void RemoveSessionMapping(SessionId sessionId);
		void StartListenReceive();
		void StartConnectReceive(SessionId sessionId);
		void HandleListenReceive(const boost::system::error_code& error, azmq::message& message);
		void HandleConnectReceive(SessionId sessionId, const boost::system::error_code& error, azmq::message& message);
		void OnReceive(SessionId sessionId, std::uint32_t messageID, const std::vector<std::byte>& data);
		void HandleHandShakeReq(SessionId sessionId, const std::vector<std::byte>& data);
		void HandleHandShakeRsp(SessionId sessionId, const std::vector<std::byte>& data);

	private:
		std::string ServerID_;
		asio::io_context& IOContext_;
		InnerNetworkCallbacks Callbacks_;
		std::unique_ptr<azmq::socket> ListenSocket_;
		bool Listening_ = false;
		bool ShuttingDown_ = false;
		std::unordered_map<SessionId, ActiveSessionEntry> SessionsFromConnect_;
		std::unordered_map<SessionId, std::unique_ptr<InnerNetworkSession>> SessionsFromListen_;
		std::unordered_map<std::string, SessionId> ServerIDToSession_;
		std::unordered_map<SessionId, std::string> SessionToServerID_;
	};
}
