#pragma once

#include "config/ClusterConfig.h"

#include <string_view>

namespace de::server::engine
{
	class Logger
	{
	public:
		static bool IsInitialized();
		static void Init(std::string_view serverId, const config::LoggingConfig& loggingConfig);
		static void Debug(std::string_view tag, std::string_view message);
		static void Info(std::string_view tag, std::string_view message);
		static void Warn(std::string_view tag, std::string_view message);
		static void Error(std::string_view tag, std::string_view message);
	};
}
