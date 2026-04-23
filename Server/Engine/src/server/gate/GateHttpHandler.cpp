#include "server/gate/GateHttpHandler.h"

#include <boost/json.hpp>

#include <utility>

namespace de::server::engine
{
	namespace
	{
		HttpResponse BuildJsonResponse(int statusCode, std::string statusText, const boost::json::object& payload)
		{
			return HttpResponse{
				statusCode,
				std::move(statusText),
				"application/json; charset=utf-8",
				boost::json::serialize(payload)
			};
		}
	}

	GateHttpHandler::GateHttpHandler(
		std::string serverId,
		std::uint16_t clientPort,
		IsGateOpenFn isGateOpen,
		AllocateClientSessionFn allocateClientSession
	)
		: serverId_(std::move(serverId))
		, clientPort_(clientPort)
		, isGateOpen_(std::move(isGateOpen))
		, allocateClientSession_(std::move(allocateClientSession))
	{
	}

	HttpResponse GateHttpHandler::HandleRequest(const HttpRequest& request) const
	{
		if (request.target == "/auth" || request.target == "/api/auth")
		{
			return HandleAuthRequest(request);
		}

		return HttpResponse{
			404,
			"Not Found",
			"application/json; charset=utf-8",
			R"({"error":"not found"})"
		};
	}

	HttpResponse GateHttpHandler::HandleAuthRequest(const HttpRequest& request) const
	{
		if (request.method != "POST")
		{
			return HttpResponse{
				405,
				"Method Not Allowed",
				"application/json; charset=utf-8",
				R"({"error":"method not allowed"})"
			};
		}

		if (!isGateOpen_ || !isGateOpen_())
		{
			return BuildJsonResponse(
				503,
				"Service Unavailable",
				boost::json::object{
					{ "error", "gate not open" }
				}
			);
		}

		boost::json::value requestJson;
		try
		{
			requestJson = boost::json::parse(request.body);
		}
		catch (const std::exception&)
		{
			return BuildJsonResponse(
				400,
				"Bad Request",
				boost::json::object{
					{ "error", "invalid json body" }
				}
			);
		}

		if (!requestJson.is_object())
		{
			return BuildJsonResponse(
				400,
				"Bad Request",
				boost::json::object{
					{ "error", "json body must be an object" }
				}
			);
		}

		const auto& object = requestJson.as_object();
		const auto* accountValue = object.if_contains("account");
		const auto* passwordValue = object.if_contains("password");
		if (accountValue == nullptr || passwordValue == nullptr || !accountValue->is_string() || !passwordValue->is_string())
		{
			return BuildJsonResponse(
				400,
				"Bad Request",
				boost::json::object{
					{ "error", "account and password are required" }
				}
			);
		}

		const auto account = boost::json::value_to<std::string>(*accountValue);
		const auto password = boost::json::value_to<std::string>(*passwordValue);
		if (account.empty() || password.empty())
		{
			return BuildJsonResponse(
				401,
				"Unauthorized",
				boost::json::object{
					{ "error", "invalid account or password" }
				}
			);
		}

		if (!allocateClientSession_)
		{
			return BuildJsonResponse(
				503,
				"Service Unavailable",
				boost::json::object{
					{ "error", "client network unavailable" }
				}
			);
		}

		const auto allocatedSession = allocateClientSession_();
		if (!allocatedSession.has_value())
		{
			return BuildJsonResponse(
				503,
				"Service Unavailable",
				boost::json::object{
					{ "error", "failed to allocate client session" }
				}
			);
		}

		return BuildJsonResponse(
			200,
			"OK",
			boost::json::object{
				{ "serverId", serverId_ },
				{ "sessionId", allocatedSession->sessionId },
				{ "conv", allocatedSession->conv },
				{ "clientPort", clientPort_ }
			}
		);
	}
}
