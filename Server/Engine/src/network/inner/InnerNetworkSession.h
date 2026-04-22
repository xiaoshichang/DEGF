#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
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

		explicit InnerNetworkSession(std::string endpoint);
		InnerNetworkSession();
		~InnerNetworkSession();
		InnerNetworkSession(const InnerNetworkSession&) = delete;
		InnerNetworkSession& operator=(const InnerNetworkSession&) = delete;
		InnerNetworkSession(InnerNetworkSession&&) = delete;
		InnerNetworkSession& operator=(InnerNetworkSession&&) = delete;
		SessionId GetSessionId() const;
		InnerNetworkSessionState GetSessionState() const;
		const std::string& GetEndpoint() const;
		const RoutingId& GetRoutingId() const;
		void OnHandShakeRsp();
		void OnHandShakeReq(RoutingId routingId);

	private:
		SessionId SessionId_;
		InnerNetworkSessionState SessionState_;
		std::string Endpoint_;
		RoutingId RoutingId_;
	};
}
