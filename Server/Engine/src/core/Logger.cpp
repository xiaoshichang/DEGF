#include "core/Logger.h"

#include <chrono>
#include <cstdio>
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#include <spdlog/common.h>
#include <spdlog/logger.h>
#include <spdlog/sinks/daily_file_sink.h>
#include <spdlog/sinks/rotating_file_sink.h>
#include <spdlog/sinks/stdout_color_sinks.h>
#include <spdlog/spdlog.h>

namespace de::server::engine
{
	namespace
	{
		std::shared_ptr<spdlog::logger> g_logger;
		std::mutex g_loggerMutex;

		spdlog::level::level_enum ParseLogLevel(std::string_view minLevel)
		{
			if (minLevel == "Trace")
			{
				return spdlog::level::trace;
			}

			if (minLevel == "Debug")
			{
				return spdlog::level::debug;
			}

			if (minLevel == "Info")
			{
				return spdlog::level::info;
			}

			if (minLevel == "Warn" || minLevel == "Warning")
			{
				return spdlog::level::warn;
			}

			if (minLevel == "Error")
			{
				return spdlog::level::err;
			}

			if (minLevel == "Critical")
			{
				return spdlog::level::critical;
			}

			return spdlog::level::info;
		}

		std::shared_ptr<spdlog::logger> TryGetLogger()
		{
			return std::atomic_load(&g_logger);
		}

		void Log(spdlog::level::level_enum level, std::string_view tag, std::string_view message)
		{
			auto logger = TryGetLogger();
			if (logger == nullptr)
			{
				const auto levelName = spdlog::level::to_string_view(level);
				std::string fallbackMessage;
				fallbackMessage.reserve(levelName.size() + tag.size() + message.size() + 32);
				fallbackMessage.append("[UninitializedLogger] [");
				fallbackMessage.append(levelName.data(), levelName.size());
				fallbackMessage.append("] [");
				fallbackMessage.append(tag.data(), tag.size());
				fallbackMessage.append("] ");
				fallbackMessage.append(message.data(), message.size());
				fallbackMessage.push_back('\n');
				std::fwrite(fallbackMessage.data(), sizeof(char), fallbackMessage.size(), stderr);
				std::fflush(stderr);
				return;
			}

			logger->log(level, "[{}] {}", tag, message);
		}
	}

	bool Logger::IsInitialized()
	{
		return TryGetLogger() != nullptr;
	}

	void Logger::Init(std::string_view serverId, const config::LoggingConfig& loggingConfig)
	{
		std::scoped_lock lock(g_loggerMutex);
		if (g_logger != nullptr)
		{
			return;
		}

		const std::filesystem::path logRoot(loggingConfig.rootDir);
		std::filesystem::create_directories(logRoot);

		const auto loggerName = std::string(serverId);
		const auto logFile = (logRoot / (loggerName + ".log")).string();

		std::vector<spdlog::sink_ptr> sinks;
		if (loggingConfig.enableConsole)
		{
			sinks.emplace_back(std::make_shared<spdlog::sinks::stdout_color_sink_mt>());
		}

		if (loggingConfig.rotateDaily)
		{
			sinks.emplace_back(std::make_shared<spdlog::sinks::daily_file_sink_mt>(
				logFile,
				0,
				0,
				false,
				static_cast<std::uint16_t>(loggingConfig.maxRetainedFiles)
			));
		}
		else
		{
			const auto maxFileSizeBytes = static_cast<std::size_t>(loggingConfig.maxFileSizeMB) * 1024ull * 1024ull;
			sinks.emplace_back(std::make_shared<spdlog::sinks::rotating_file_sink_mt>(
				logFile,
				maxFileSizeBytes,
				static_cast<std::size_t>(loggingConfig.maxRetainedFiles)
			));
		}

		auto logger = std::make_shared<spdlog::logger>(loggerName, sinks.begin(), sinks.end());
		logger->set_level(ParseLogLevel(loggingConfig.minLevel));
		logger->flush_on(spdlog::level::warn);

		for (const auto& sink : sinks)
		{
			sink->set_pattern("[%Y-%m-%d %H:%M:%S.%e] [%n] [%t] [%^%l%$] %v");
		}

		std::atomic_store(&g_logger, logger);
		spdlog::set_default_logger(logger);
		spdlog::flush_every(std::chrono::milliseconds(loggingConfig.flushIntervalMs));

		logger->info("[Logger] initialized");
	}

	void Logger::Debug(std::string_view tag, std::string_view message)
	{
		Log(spdlog::level::debug, tag, message);
	}

	void Logger::Info(std::string_view tag, std::string_view message)
	{
		Log(spdlog::level::info, tag, message);
	}

	void Logger::Warn(std::string_view tag, std::string_view message)
	{
		Log(spdlog::level::warn, tag, message);
	}

	void Logger::Error(std::string_view tag, std::string_view message)
	{
		Log(spdlog::level::err, tag, message);
	}
}
