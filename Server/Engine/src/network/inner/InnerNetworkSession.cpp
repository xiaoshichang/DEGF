#include "network/inner/InnerNetworkSession.h"

#include <stdexcept>
#include <utility>
#include <zmq.h>

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

	InnerNetworkSession::InnerNetworkSession(void* ctx, RoutingId routingId)
		: SessionId_(NextInnerNetworkSessionId())
		, SessionState_(InnerNetworkSessionState::BeforeRegistered)
		, RoutingId_(std::move(routingId))
	{
		if (ctx == nullptr)
		{
			throw std::invalid_argument("InnerNetworkSession requires a valid zmq context.");
		}

		if (RoutingId_.empty())
		{
			throw std::invalid_argument("Active InnerNetworkSession requires a valid routing id.");
		}

		ZMQSocket_ = zmq_socket(ctx, ZMQ_DEALER);
		if (ZMQSocket_ == nullptr)
		{
			throw std::runtime_error("Failed to create zmq socket for InnerNetworkSession.");
		}

		if (zmq_setsockopt(ZMQSocket_, ZMQ_ROUTING_ID, RoutingId_.data(), RoutingId_.size()) != 0)
		{
			CloseSocket();
			throw std::runtime_error("Failed to set zmq routing id for InnerNetworkSession.");
		}
	}

	InnerNetworkSession::InnerNetworkSession()
		: SessionId_(NextInnerNetworkSessionId())
		, SessionState_(InnerNetworkSessionState::BeforeRegistered)
	{
	}

	InnerNetworkSession::~InnerNetworkSession()
	{
		CloseSocket();
	}

	InnerNetworkSession::SessionId InnerNetworkSession::GetSessionId() const
	{
		return SessionId_;
	}

	InnerNetworkSessionState InnerNetworkSession::GetSessionState() const
	{
		return SessionState_;
	}

	void* InnerNetworkSession::GetZMQSocket() const
	{
		return ZMQSocket_;
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

	void InnerNetworkSession::CloseSocket()
	{
		if (ZMQSocket_ == nullptr)
		{
			return;
		}

		zmq_close(ZMQSocket_);
		ZMQSocket_ = nullptr;
	}
}
