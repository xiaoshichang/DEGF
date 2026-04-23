#pragma once

#include "config/ClusterConfig.h"
#include "core/BoostAsio.h"
#include "network/client/ClientNetworkSession.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace de::server::engine::network
{
	using OnClientNetworkConnectCallback = std::function<void(ClientNetworkSession::SessionId)>;
	using OnClientNetworkReceiveCallback = std::function<void(ClientNetworkSession::SessionId, std::uint32_t messageId, const std::vector<std::byte>& data)>;
	using OnClientNetworkDisconnectCallback = std::function<void(ClientNetworkSession::SessionId)>;

	struct ClientNetworkCallbacks
	{
		OnClientNetworkConnectCallback OnConnect;
		OnClientNetworkReceiveCallback OnReceive;
		OnClientNetworkDisconnectCallback OnDisconnect;
	};

	class ClientNetwork
	{
	public:
		using SessionId = ClientNetworkSession::SessionId;

		ClientNetwork(asio::io_context& ioContext, config::KcpConfig kcpConfig, ClientNetworkCallbacks callbacks);
		~ClientNetwork();
		ClientNetwork(const ClientNetwork&) = delete;
		ClientNetwork& operator=(const ClientNetwork&) = delete;

		bool Listen(const config::NetworkConfig& config);
		bool IsListening() const;
		std::uint16_t GetListenPort() const;
		bool Send(SessionId sessionId, std::uint32_t messageId, const std::vector<std::byte>& data);
		bool ActiveDisconnect(SessionId sessionId);

		int SendRawPacket(const ClientNetworkSession& session, const char* data, int size);

	private:
		using Endpoint = asio::ip::udp::endpoint;

		ClientNetworkSession* CreateSession(const Endpoint& remoteEndpoint, std::uint32_t conv);
		ClientNetworkSession* FindSession(SessionId sessionId);
		ClientNetworkSession* FindSession(const Endpoint& remoteEndpoint, std::uint32_t conv);
		void DestroySession(SessionId sessionId);
		void DestroySessions();
		void StartReceive();
		void HandleReceive(const boost::system::error_code& error, std::size_t bytesTransferred);
		void HandleKcpReceive(ClientNetworkSession& session);
		void HandleDecodedFrames(ClientNetworkSession& session);
		void StartUpdateTimer();
		void HandleUpdateTimer(const boost::system::error_code& error);
		void UpdateSessions();
		void ConfigureSession(ClientNetworkSession& session);
		static std::string BuildEndpointConvKey(const Endpoint& remoteEndpoint, std::uint32_t conv);
		static std::uint32_t GetCurrentMs();

	private:
		asio::io_context& ioContext_;
		config::KcpConfig kcpConfig_;
		ClientNetworkCallbacks callbacks_;
		asio::ip::udp::socket socket_;
		asio::steady_timer updateTimer_;
		Endpoint receiveRemoteEndpoint_;
		std::array<char, 65536> receiveBuffer_{};
		bool listening_ = false;
		bool shuttingDown_ = false;
		std::unordered_map<SessionId, std::unique_ptr<ClientNetworkSession>> sessions_;
		std::unordered_map<std::string, SessionId> endpointConvToSession_;
	};
}
