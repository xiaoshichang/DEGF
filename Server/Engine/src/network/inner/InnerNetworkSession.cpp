#include "network/inner/InnerNetworkSession.h"

#include <stdexcept>
#include <utility>

namespace de::server::engine::network
{
	namespace
	{
		InnerNetworkSession::SessionId NextInnerNetworkSessionId()
		{
			static InnerNetworkSession::SessionId nextSessionId{1};
			return nextSessionId++;
		}
	}

	InnerNetworkSession::InnerNetworkSession(std::string endpoint)
		: SessionId_(NextInnerNetworkSessionId())
		, SessionState_(InnerNetworkSessionState::BeforeRegistered)
		, Endpoint_(std::move(endpoint))
	{
	}

	InnerNetworkSession::InnerNetworkSession()
		: SessionId_(NextInnerNetworkSessionId())
		, SessionState_(InnerNetworkSessionState::BeforeRegistered)
	{
	}

	InnerNetworkSession::~InnerNetworkSession() = default;

	InnerNetworkSession::SessionId InnerNetworkSession::GetSessionId() const
	{
		return SessionId_;
	}

	InnerNetworkSessionState InnerNetworkSession::GetSessionState() const
	{
		return SessionState_;
	}

	const std::string& InnerNetworkSession::GetEndpoint() const
	{
		return Endpoint_;
	}

	const InnerNetworkSession::RoutingId& InnerNetworkSession::GetRoutingId() const
	{
		return RoutingId_;
	}

	void InnerNetworkSession::OnHandShakeRsp()
	{
		if (SessionState_ == InnerNetworkSessionState::Registered)
		{
			return;
		}

		if (SessionState_ != InnerNetworkSessionState::BeforeRegistered)
		{
			throw std::logic_error("Only before-registered InnerNetworkSession can be registered.");
		}

		SessionState_ = InnerNetworkSessionState::Registered;
	}

	void InnerNetworkSession::OnHandShakeReq(RoutingId routingId)
	{
		if (routingId.empty())
		{
			throw std::invalid_argument("Passive InnerNetworkSession requires a valid routing id.");
		}

		if (SessionState_ == InnerNetworkSessionState::Registered)
		{
			if (RoutingId_ != routingId)
			{
				throw std::logic_error("Registered passive InnerNetworkSession cannot change routing id.");
			}

			return;
		}

		if (SessionState_ != InnerNetworkSessionState::BeforeRegistered)
		{
			throw std::logic_error("Only before-registered InnerNetworkSession can be registered.");
		}

		RoutingId_ = std::move(routingId);
		SessionState_ = InnerNetworkSessionState::Registered;
	}
}
