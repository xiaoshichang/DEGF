#pragma once

#include "core/BoostAsio.h"

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

extern "C"
{
	struct IKCPCB;
	typedef struct IKCPCB ikcpcb;
}

namespace de::server::engine::network
{
	class ClientNetwork;

	enum ClientNetworkSessionState
	{
		Allocated,
		Connected
	};

	class ClientNetworkSession
	{
	public:
		using SessionId = std::uint64_t;

		ClientNetworkSession(ClientNetwork* owner, std::uint32_t conv);
		~ClientNetworkSession();
		ClientNetworkSession(const ClientNetworkSession&) = delete;
		ClientNetworkSession& operator=(const ClientNetworkSession&) = delete;
		ClientNetworkSession(ClientNetworkSession&&) = delete;
		ClientNetworkSession& operator=(ClientNetworkSession&&) = delete;

		SessionId GetSessionId() const;
		ClientNetworkSessionState GetSessionState() const;
		const asio::ip::udp::endpoint& GetRemoteEndpoint() const;
		bool HasRemoteEndpoint() const;
		bool BindRemoteEndpoint(const asio::ip::udp::endpoint& remoteEndpoint);
		bool MatchesRemoteEndpoint(const asio::ip::udp::endpoint& remoteEndpoint) const;
		std::uint32_t GetConv() const;
		ikcpcb* GetKcp() const;
		std::vector<std::byte>& GetReceiveBuffer();
		const std::vector<std::byte>& GetReceiveBuffer() const;
		ClientNetwork* GetOwner() const;
		void OnConnected();

	private:
		SessionId SessionId_;
		ClientNetworkSessionState SessionState_;
		ClientNetwork* Owner_;
		asio::ip::udp::endpoint RemoteEndpoint_;
		std::uint32_t Conv_ = 0;
		ikcpcb* Kcp_ = nullptr;
		std::vector<std::byte> ReceiveBuffer_;
	};
}
