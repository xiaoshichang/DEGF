#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace de::server::engine::network
{
	enum InnerNetworkSessionState
	{
		BeforeRegistered,
		Registered
	};

	class InnerNetworkSession
	{
	public:
		using SessionId = std::uint64_t;
		using RoutingId = std::vector<std::byte>;

		InnerNetworkSession(void* ctx, RoutingId routingId);
		InnerNetworkSession();
		~InnerNetworkSession();
		InnerNetworkSession(const InnerNetworkSession&) = delete;
		InnerNetworkSession& operator=(const InnerNetworkSession&) = delete;
		InnerNetworkSession(InnerNetworkSession&&) = delete;
		InnerNetworkSession& operator=(InnerNetworkSession&&) = delete;
		SessionId GetSessionId() const;
		InnerNetworkSessionState GetSessionState() const;
		void* GetZMQSocket() const;
		const RoutingId& GetRoutingId() const;
		void OnHandShakeRsp();
		void OnHandShakeReq(RoutingId routingId);

	private:
		void CloseSocket();

		SessionId SessionId_;
		InnerNetworkSessionState SessionState_;
		void* ZMQSocket_ = nullptr;
		RoutingId RoutingId_;
	};
}
