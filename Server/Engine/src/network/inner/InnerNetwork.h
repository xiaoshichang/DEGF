/**

InnerNetwork 主要处理集群内部通信，底层采用zeromq第三方库，使用tcp作为传输层协议。InnerNetwork 提供两大功能：

1. 作为被动监听方，监听集群内其他节点的连接请求，并处理这些请求。只能进行一次监听。
2. 作为主动连接方，向集群内其他节点发起连接请求，并处理这些请求。可以进行多次主动连接。

InnerNetwork需要管理所有InnerNetworkSession。无论主动连接还是被动连接，都需要返回一个sessionID，用于后续的通信。
InnerNetworkSession负责维护一个连接的状态，包括连接的socket、连接的对端信息、以及连接的生命周期管理等。


*/

#pragma once

#include <asio/io_context.hpp>
#include <asio/steady_timer.hpp>
#include <cstddef>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>
#include "network/inner/InnerNetworkSession.h"
#include "network/protocal/Header.h"


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
		InnerNetworkSession* CreateConnectSession(const std::string& endpoint);
		InnerNetworkSession* CreateListenSession();
		void DestroyConnectSession(SessionId sessionId);
		void DestroyListenSession(SessionId sessionId);
		void DestroySessions(std::unordered_map<SessionId, InnerNetworkSession*>& sessions);
		InnerNetworkSession* FindConnectSession(SessionId sessionId);
		InnerNetworkSession* FindListenSession(SessionId sessionId);
		void RegisterSession(InnerNetworkSession* session, const std::string& serverID);
		std::string GetSessionServerID(const InnerNetworkSession* session) const;
		void RemoveSessionMapping(InnerNetworkSession* session);
		void SchedulePoll();
		void PollOnce();
		void PollListenSocket();
		void PollConnectSessions();
		void OnReceive(SessionId sessionId, std::uint32_t messageID, const std::vector<std::byte>& data);


	private:
		std::string ServerID_;
		asio::io_context& IOContext_;
		asio::steady_timer PollTimer_;
		void* ZMQContext_ = nullptr;
		void* ListenSocket_ = nullptr;
		InnerNetworkCallbacks Callbacks_;

		/*
		* Indicates whether the network is currently listening for incoming connections.
		*/
		bool Listening_;

		/*
		* sessions from passive listen
		*/
		std::unordered_map<SessionId, InnerNetworkSession*> SessionsFromListen;

		/*
		* sessions from active connect
		*/
		std::unordered_map<SessionId, InnerNetworkSession*> SessionsFromConnect;

		/*
		* Maps server IDs to their corresponding sessions.
		*/
		std::unordered_map<std::string, InnerNetworkSession*> ServerIDToSession;

		/*
		* Maps sessions to their corresponding server IDs.
		*/
		std::unordered_map<InnerNetworkSession*, std::string> SessionToServerID;

	};
}
