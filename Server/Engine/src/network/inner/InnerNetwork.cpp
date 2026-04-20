#include "network/inner/InnerNetwork.h"

#include "network/inner/InnerNetworkWorker.h"

namespace de::server::engine::network
{
	InnerNetwork::InnerNetwork(const std::string& serverID, asio::io_context& ioContext, InnerNetworkCallbacks callbacks)
		: ServerID_(serverID)
		, IOContext_(ioContext)
		, Callbacks_(callbacks)
		, Worker_(std::make_unique<InnerNetworkWorker>(*this))
	{
	}

	InnerNetwork::~InnerNetwork() = default;

	bool InnerNetwork::Listen(const std::string& endpoint)
	{
		return Worker_->Listen(endpoint);
	}

	InnerNetworkSession* InnerNetwork::ConnectTo(const std::string& endpoint)
	{
		return Worker_->ConnectTo(endpoint);
	}

	bool InnerNetwork::Send(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data)
	{
		return Worker_->Send(serverID, messageID, data);
	}

	bool InnerNetwork::ActiveDisconnect(SessionId sessionId)
	{
		return Worker_->ActiveDisconnect(sessionId);
	}

	void InnerNetwork::PostReceiveCallback(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data) const
	{
		if (Callbacks_.OnReceive == nullptr)
		{
			return;
		}

		asio::post(
			IOContext_,
			[callbacks = Callbacks_, serverID, messageID, data]()
			{
				callbacks.OnReceive(serverID, messageID, data);
			}
		);
	}

	void InnerNetwork::PostDisconnectCallback(const std::string& serverID) const
	{
		if (Callbacks_.OnDisconnect == nullptr)
		{
			return;
		}

		asio::post(
			IOContext_,
			[callbacks = Callbacks_, serverID]()
			{
				callbacks.OnDisconnect(serverID);
			}
		);
	}
}
