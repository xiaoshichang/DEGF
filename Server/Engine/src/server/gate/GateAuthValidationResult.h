#pragma once

#include <string>

namespace de::server::engine
{
	struct GateAuthValidationResult
	{
		bool IsSuccess = false;
		int StatusCode = 503;
		std::string Error;
		std::string ExpectedServerId;
	};
}
