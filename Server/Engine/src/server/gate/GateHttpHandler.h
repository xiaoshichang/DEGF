#pragma once

#include "http/HttpService.h"
#include "network/client/ClientNetwork.h"
#include "server/gate/GateAuthValidationResult.h"

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
		using AuthValidationCallback = std::function<void(GateAuthValidationResult)>;
		using ValidateAuthFn = std::function<void(const std::string& account, const std::string& password, AuthValidationCallback callback)>;

		GateHttpHandler(
			std::string serverId,
			std::uint16_t clientPort,
			IsGateOpenFn isGateOpen,
			ValidateAuthFn validateAuth,
			AllocateClientSessionFn allocateClientSession
		);

		void HandleRequest(const HttpRequest& request, HttpService::ResponseCallback responseCallback) const;

	private:
		void HandleAuthRequest(const HttpRequest& request, HttpService::ResponseCallback responseCallback) const;
		HttpResponse BuildAuthSuccessResponse() const;

		std::string serverId_;
		std::uint16_t clientPort_ = 0;
		IsGateOpenFn isGateOpen_;
		ValidateAuthFn validateAuth_;
		AllocateClientSessionFn allocateClientSession_;
	};
}
