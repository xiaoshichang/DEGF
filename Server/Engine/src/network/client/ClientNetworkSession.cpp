#include "network/client/ClientNetworkSession.h"

#include "ikcp.h"

#include <stdexcept>
#include <utility>

namespace de::server::engine::network
{
	namespace
	{
		ClientNetworkSession::SessionId NextClientNetworkSessionId()
		{
			static ClientNetworkSession::SessionId nextSessionId{1};
			return nextSessionId++;
		}
	}

	ClientNetworkSession::ClientNetworkSession(ClientNetwork* owner, asio::ip::udp::endpoint remoteEndpoint, std::uint32_t conv)
		: SessionId_(NextClientNetworkSessionId())
		, SessionState_(ClientNetworkSessionState::BeforeConnected)
		, Owner_(owner)
		, RemoteEndpoint_(std::move(remoteEndpoint))
		, Conv_(conv)
		, Kcp_(ikcp_create(conv, this))
	{
		if (Owner_ == nullptr)
		{
			throw std::invalid_argument("ClientNetworkSession requires a valid owner.");
		}

		if (Conv_ == 0)
		{
			throw std::invalid_argument("ClientNetworkSession requires a non-zero conv.");
		}

		if (Kcp_ == nullptr)
		{
			throw std::runtime_error("Failed to create KCP control block.");
		}
	}

	ClientNetworkSession::~ClientNetworkSession()
	{
		if (Kcp_ != nullptr)
		{
			ikcp_release(Kcp_);
			Kcp_ = nullptr;
		}
	}

	ClientNetworkSession::SessionId ClientNetworkSession::GetSessionId() const
	{
		return SessionId_;
	}

	ClientNetworkSessionState ClientNetworkSession::GetSessionState() const
	{
		return SessionState_;
	}

	const asio::ip::udp::endpoint& ClientNetworkSession::GetRemoteEndpoint() const
	{
		return RemoteEndpoint_;
	}

	std::uint32_t ClientNetworkSession::GetConv() const
	{
		return Conv_;
	}

	ikcpcb* ClientNetworkSession::GetKcp() const
	{
		return Kcp_;
	}

	std::vector<std::byte>& ClientNetworkSession::GetReceiveBuffer()
	{
		return ReceiveBuffer_;
	}

	const std::vector<std::byte>& ClientNetworkSession::GetReceiveBuffer() const
	{
		return ReceiveBuffer_;
	}

	ClientNetwork* ClientNetworkSession::GetOwner() const
	{
		return Owner_;
	}

	void ClientNetworkSession::OnConnected()
	{
		if (SessionState_ == ClientNetworkSessionState::Connected)
		{
			return;
		}

		if (SessionState_ != ClientNetworkSessionState::BeforeConnected)
		{
			throw std::logic_error("Only before-connected ClientNetworkSession can become connected.");
		}

		SessionState_ = ClientNetworkSessionState::Connected;
	}
}
