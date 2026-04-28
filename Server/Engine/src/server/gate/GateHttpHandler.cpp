#include "server/gate/GateHttpHandler.h"

#include <boost/json.hpp>

#include <algorithm>
#include <charconv>
#include <cstdint>
#include <string_view>
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

		constexpr std::uint32_t kFnvOffsetBasis = 2166136261u;
		constexpr std::uint32_t kFnvPrime = 16777619u;

		bool TryParseGateIndex(std::string_view serverId, std::uint32_t& index)
		{
			static constexpr std::string_view kGatePrefix = "Gate";
			if (serverId.size() <= kGatePrefix.size() || serverId.substr(0, kGatePrefix.size()) != kGatePrefix)
			{
				return false;
			}

			const auto suffix = serverId.substr(kGatePrefix.size());
			std::uint32_t parsedIndex = 0;
			const auto result = std::from_chars(suffix.data(), suffix.data() + suffix.size(), parsedIndex);
			if (result.ec != std::errc{} || result.ptr != suffix.data() + suffix.size())
			{
				return false;
			}

			index = parsedIndex;
			return true;
		}

		bool GateServerIdLess(std::string_view left, std::string_view right)
		{
			std::uint32_t leftIndex = 0;
			std::uint32_t rightIndex = 0;
			const bool hasLeftIndex = TryParseGateIndex(left, leftIndex);
			const bool hasRightIndex = TryParseGateIndex(right, rightIndex);
			if (hasLeftIndex && hasRightIndex && leftIndex != rightIndex)
			{
				return leftIndex < rightIndex;
			}

			return left < right;
		}

		std::uint32_t ComputeAccountGateHash(std::string_view account)
		{
			std::uint32_t hash = kFnvOffsetBasis;
			for (const unsigned char byte : account)
			{
				hash ^= static_cast<std::uint32_t>(byte);
				hash *= kFnvPrime;
			}

			return hash;
		}
	}

	GateHttpHandler::GateHttpHandler(
		std::string serverId,
		std::uint16_t clientPort,
		std::vector<std::string> gateServerIds,
		IsGateOpenFn isGateOpen,
		AllocateClientSessionFn allocateClientSession
	)
		: serverId_(std::move(serverId))
		, clientPort_(clientPort)
		, gateServerIds_(std::move(gateServerIds))
		, isGateOpen_(std::move(isGateOpen))
		, allocateClientSession_(std::move(allocateClientSession))
	{
		std::sort(
			gateServerIds_.begin(),
			gateServerIds_.end(),
			[](const std::string& left, const std::string& right)
			{
				return GateServerIdLess(left, right);
			}
		);
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

		if (gateServerIds_.empty())
		{
			return BuildJsonResponse(
				503,
				"Service Unavailable",
				boost::json::object{
					{ "error", "gate routing unavailable" }
				}
			);
		}

		const auto selectedGateIndex = static_cast<std::size_t>(ComputeAccountGateHash(account) % gateServerIds_.size());
		const auto& selectedGateServerId = gateServerIds_[selectedGateIndex];
		if (selectedGateServerId != serverId_)
		{
			return BuildJsonResponse(
				403,
				"Forbidden",
				boost::json::object{
					{ "error", "account routed to another gate" },
					{ "expectedServerId", selectedGateServerId },
					{ "serverId", serverId_ }
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
