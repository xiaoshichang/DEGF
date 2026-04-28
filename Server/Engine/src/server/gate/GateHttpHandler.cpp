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

		std::string BuildHttpStatusText(int statusCode)
		{
			switch (statusCode)
			{
			case 400:
				return "Bad Request";
			case 401:
				return "Unauthorized";
			case 403:
				return "Forbidden";
			case 405:
				return "Method Not Allowed";
			case 503:
				return "Service Unavailable";
			default:
				return "Internal Server Error";
			}
		}

		HttpResponse BuildAuthValidationFailureResponse(
			const std::string& serverId,
			const GateAuthValidationResult& validationResult
		)
		{
			boost::json::object payload{
				{
					"error",
					validationResult.Error.empty() ? "auth validation failed" : validationResult.Error
				},
				{ "serverId", serverId }
			};
			if (!validationResult.ExpectedServerId.empty())
			{
				payload["expectedServerId"] = validationResult.ExpectedServerId;
			}

			const int statusCode = validationResult.StatusCode > 0 ? validationResult.StatusCode : 503;
			return BuildJsonResponse(statusCode, BuildHttpStatusText(statusCode), payload);
		}
	}

	GateHttpHandler::GateHttpHandler(
		std::string serverId,
		std::uint16_t clientPort,
		IsGateOpenFn isGateOpen,
		ValidateAuthFn validateAuth,
		AllocateClientSessionFn allocateClientSession
	)
		: serverId_(std::move(serverId))
		, clientPort_(clientPort)
		, isGateOpen_(std::move(isGateOpen))
		, validateAuth_(std::move(validateAuth))
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
		if (!validateAuth_)
		{
			return BuildJsonResponse(
				503,
				"Service Unavailable",
				boost::json::object{
					{ "error", "auth validator unavailable" }
				}
			);
		}

		const auto validationResult = validateAuth_(account, password);
		if (!validationResult.IsSuccess)
		{
			return BuildAuthValidationFailureResponse(serverId_, validationResult);
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
