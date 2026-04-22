#include "core/Logger.h"

#include <boost/log/attributes/current_thread_id.hpp>
#include <boost/log/core.hpp>
#include <boost/log/expressions.hpp>
#include <boost/log/keywords/auto_flush.hpp>
#include <boost/log/keywords/file_name.hpp>
#include <boost/log/keywords/format.hpp>
#include <boost/log/keywords/max_files.hpp>
#include <boost/log/keywords/open_mode.hpp>
#include <boost/log/keywords/rotation_size.hpp>
#include <boost/log/keywords/target.hpp>
#include <boost/log/keywords/target_file_name.hpp>
#include <boost/log/keywords/time_based_rotation.hpp>
#include <boost/log/sinks/text_file_backend.hpp>
#include <boost/log/sources/severity_logger.hpp>
#include <boost/log/sources/record_ostream.hpp>
#include <boost/log/support/date_time.hpp>
#include <boost/log/trivial.hpp>
#include <boost/log/utility/setup/common_attributes.hpp>
#include <boost/log/utility/setup/console.hpp>
#include <boost/log/utility/setup/file.hpp>

#include <cstdio>
#include <filesystem>
#include <ios>
#include <iostream>
#include <memory>
#include <mutex>
#include <string>

namespace de::server::engine
{
	namespace
	{
		namespace logging = boost::log;
		namespace sinks = boost::log::sinks;
		namespace keywords = boost::log::keywords;
		namespace expr = boost::log::expressions;
		namespace attrs = boost::log::attributes;
		namespace sources = boost::log::sources;

		using SeverityLogger = sources::severity_logger_mt<logging::trivial::severity_level>;

		std::shared_ptr<SeverityLogger> g_logger;
		std::mutex g_loggerMutex;

		auto MakeLogFormatter(const std::string& loggerName)
		{
			return expr::stream
				<< "["
				<< expr::format_date_time<boost::posix_time::ptime>("TimeStamp", "%Y-%m-%d %H:%M:%S.%f")
				<< "] ["
				<< loggerName
				<< "] ["
				<< expr::attr<attrs::current_thread_id::value_type>("ThreadID")
				<< "] ["
				<< logging::trivial::severity
				<< "] "
				<< expr::smessage;
		}

		logging::trivial::severity_level ParseLogLevel(std::string_view minLevel)
		{
			if (minLevel == "Trace")
			{
				return logging::trivial::trace;
			}

			if (minLevel == "Debug")
			{
				return logging::trivial::debug;
			}

			if (minLevel == "Info")
			{
				return logging::trivial::info;
			}

			if (minLevel == "Warn" || minLevel == "Warning")
			{
				return logging::trivial::warning;
			}

			if (minLevel == "Error")
			{
				return logging::trivial::error;
			}

			if (minLevel == "Critical")
			{
				return logging::trivial::fatal;
			}

			return logging::trivial::info;
		}

		std::shared_ptr<SeverityLogger> TryGetLogger()
		{
			return std::atomic_load(&g_logger);
		}

		const char* ToLevelName(logging::trivial::severity_level level)
		{
			switch (level)
			{
			case logging::trivial::trace:
				return "trace";
			case logging::trivial::debug:
				return "debug";
			case logging::trivial::info:
				return "info";
			case logging::trivial::warning:
				return "warning";
			case logging::trivial::error:
				return "error";
			case logging::trivial::fatal:
				return "fatal";
			default:
				return "info";
			}
		}

		void Log(logging::trivial::severity_level level, std::string_view tag, std::string_view message)
		{
			auto logger = TryGetLogger();
			if (logger == nullptr)
			{
				std::string fallbackMessage;
				const auto* levelName = ToLevelName(level);
				fallbackMessage.reserve(std::char_traits<char>::length(levelName) + tag.size() + message.size() + 32);
				fallbackMessage.append("[UninitializedLogger] [");
				fallbackMessage.append(levelName);
				fallbackMessage.append("] [");
				fallbackMessage.append(tag.data(), tag.size());
				fallbackMessage.append("] ");
				fallbackMessage.append(message.data(), message.size());
				fallbackMessage.push_back('\n');
				std::fwrite(fallbackMessage.data(), sizeof(char), fallbackMessage.size(), stderr);
				std::fflush(stderr);
				return;
			}

			BOOST_LOG_SEV(*logger, level) << "[" << tag << "] " << message;
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
		const auto rotatedPattern = (logRoot / (loggerName + "_%Y%m%d_%H%M%S_%5N.log")).string();
		const auto formatter = MakeLogFormatter(loggerName);

		logging::core::get()->remove_all_sinks();
		logging::add_common_attributes();
		logging::core::get()->set_filter(logging::trivial::severity >= ParseLogLevel(loggingConfig.minLevel));

		if (loggingConfig.enableConsole)
		{
			logging::add_console_log(
				std::clog,
				keywords::format = formatter,
				keywords::auto_flush = true
			);
		}

		if (loggingConfig.rotateDaily)
		{
			logging::add_file_log(
				keywords::file_name = logFile,
				keywords::target_file_name = rotatedPattern,
				keywords::open_mode = std::ios_base::app,
				keywords::auto_flush = true,
				keywords::target = logRoot.string(),
				keywords::max_files = static_cast<std::size_t>(std::max(loggingConfig.maxRetainedFiles, 1)),
				keywords::time_based_rotation = sinks::file::rotation_at_time_point(0, 0, 0),
				keywords::format = formatter
			);
		}
		else
		{
			const auto maxFileSizeBytes = static_cast<std::size_t>(loggingConfig.maxFileSizeMB) * 1024ull * 1024ull;
			logging::add_file_log(
				keywords::file_name = logFile,
				keywords::target_file_name = rotatedPattern,
				keywords::open_mode = std::ios_base::app,
				keywords::auto_flush = true,
				keywords::target = logRoot.string(),
				keywords::max_files = static_cast<std::size_t>(std::max(loggingConfig.maxRetainedFiles, 1)),
				keywords::rotation_size = std::max<std::size_t>(maxFileSizeBytes, 1),
				keywords::format = formatter
			);
		}

		auto logger = std::make_shared<SeverityLogger>();
		std::atomic_store(&g_logger, logger);

		Log(logging::trivial::info, "Logger", "initialized");
	}

	void Logger::Debug(std::string_view tag, std::string_view message)
	{
		Log(logging::trivial::debug, tag, message);
	}

	void Logger::Info(std::string_view tag, std::string_view message)
	{
		Log(logging::trivial::info, tag, message);
	}

	void Logger::Warn(std::string_view tag, std::string_view message)
	{
		Log(logging::trivial::warning, tag, message);
	}

	void Logger::Error(std::string_view tag, std::string_view message)
	{
		Log(logging::trivial::error, tag, message);
	}
}
