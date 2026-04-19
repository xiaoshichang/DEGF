/**

InnerNetwork 主要处理集群内部通信，底层采用zeromq第三方库，使用tcp作为传输层协议。InnerNetwork 提供两大功能：

1. 作为被动监听方，监听集群内其他节点的连接请求，并处理这些请求。只能进行一次监听。
2. 作为主动连接方，向集群内其他节点发起连接请求，并处理这些请求。可以进行多次主动连接。

InnerNetwork需要管理所有InnerNetworkSession。无论主动连接还是被动连接，都需要返回一个sessionID，用于后续的通信。
InnerNetworkSession负责维护一个连接的状态，包括连接的socket、连接的对端信息、以及连接的生命周期管理等。


*/

#pragma once

#include <asio/io_context.hpp>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

#include "network/protocal/Header.h"


namespace de::server::engine::network
{

	enum InnerNetworkSessionState
	{
		Connecting,
		Connected,
		Registered,
		Disconnected
	};

	class InnerNetworkSession
	{
	public:
		using SessionId = std::uint64_t;
		InnerNetworkSession(SessionId sessionId, InnerNetworkSessionState initState, std::string remoteServerID);
		SessionId GetSessionId() const;
		InnerNetworkSessionState GetSessionState() const;
		const std::string& GetRemoteServerID() const;


	private:
		SessionId SessionId_;
		std::string RemoteServerID;
		InnerNetworkSessionState SessionState_;
	};


	typedef void (*OnInnerNetworkReceiveCallback)(const std::string& serverID, const std::vector<std::byte>& data);
	typedef void (*OnInnerNetworkAcceptCallback)(const std::string& serverID);
	typedef void (*OnInnerNetworkDisconnectCallback)(const std::string& serverID);

	struct InnerNetworkCallbacks
	{
		OnInnerNetworkReceiveCallback OnReceive;
		OnInnerNetworkAcceptCallback OnAccept;
		OnInnerNetworkDisconnectCallback OnDisconnect;
	};

	class InnerNetwork
	{
	public:
		using SessionId = InnerNetworkSession::SessionId;

		InnerNetwork(asio::io_context& ioContext, InnerNetworkCallbacks callbacks);
		~InnerNetwork();
		InnerNetwork(const InnerNetwork&) = delete;
		InnerNetwork& operator=(const InnerNetwork&) = delete;
		bool Listen(const std::string& endpoint);
		SessionId ConnectTo(const std::string& endpoint);
		bool Send(const std::string& serverID, const std::vector<std::byte>& data);
		bool ActiveDisconnect(SessionId sessionId);

	private:
		SessionId CreateSession(const std::string& endpoint, InnerNetworkSessionState initState);
		void OnReceive(SessionId sessionId, const std::vector<std::byte>& data);
		void OnAccept(SessionId sessionId);
		void OnPassiveDisconnect(SessionId sessionId);


	private:
		asio::io_context& IOContext_;
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
		* Maps server IDs to their corresponding session IDs.
		*/
		std::unordered_map<std::string, SessionId> ServerIDToSessionId;

	};
}
