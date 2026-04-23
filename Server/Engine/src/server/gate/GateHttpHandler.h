#pragma once

#include "http/HttpService.h"
#include "network/client/ClientNetwork.h"

#include <functional>
#include <optional>
#include <string>

namespace de::server::engine
{
	class GateHttpHandler
	{
	public:
		using AllocateClientSessionFn = std::function<std::optional<network::AllocatedClientSession>()>;
		using IsGateOpenFn = std::function<bool()>;

		GateHttpHandler(
			std::string serverId,
			std::uint16_t clientPort,
			IsGateOpenFn isGateOpen,
			AllocateClientSessionFn allocateClientSession
		);

		HttpResponse HandleRequest(const HttpRequest& request) const;

	private:
		HttpResponse HandleAuthRequest(const HttpRequest& request) const;

		std::string serverId_;
		std::uint16_t clientPort_ = 0;
		IsGateOpenFn isGateOpen_;
		AllocateClientSessionFn allocateClientSession_;
	};
}
