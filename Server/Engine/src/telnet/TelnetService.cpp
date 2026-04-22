#include "telnet/TelnetService.h"

#include "core/Logger.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <deque>
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>

#ifdef _WIN32
#include <windows.h>
#include <psapi.h>
#else
#include <unistd.h>
#endif

namespace de::server::engine
{
	namespace
	{
		constexpr char kPrompt[] = "> ";
		constexpr unsigned char kTelnetIac = 255;
		constexpr unsigned char kTelnetDont = 254;
		constexpr unsigned char kTelnetDo = 253;
		constexpr unsigned char kTelnetWont = 252;
		constexpr unsigned char kTelnetWill = 251;
		constexpr unsigned char kTelnetSb = 250;
		constexpr unsigned char kTelnetSe = 240;

		std::string Trim(std::string_view value)
		{
			std::size_t begin = 0;
			while (begin < value.size() && std::isspace(static_cast<unsigned char>(value[begin])) != 0)
			{
				++begin;
			}

			std::size_t end = value.size();
			while (end > begin && std::isspace(static_cast<unsigned char>(value[end - 1])) != 0)
			{
				--end;
			}

			return std::string(value.substr(begin, end - begin));
		}

		std::string ToLower(std::string_view value)
		{
			std::string result(value);
			std::transform(
				result.begin(),
				result.end(),
				result.begin(),
				[](unsigned char character)
				{
					return static_cast<char>(std::tolower(character));
				}
			);
			return result;
		}

		double BytesToMegabytes(std::uint64_t bytes)
		{
			return static_cast<double>(bytes) / 1024.0 / 1024.0;
		}

#ifdef _WIN32
		std::uint64_t FileTimeToMilliseconds(const FILETIME& fileTime)
		{
			ULARGE_INTEGER value;
			value.LowPart = fileTime.dwLowDateTime;
			value.HighPart = fileTime.dwHighDateTime;
			return value.QuadPart / 10000ull;
		}
#endif
	}

	class TelnetService::Session : public std::enable_shared_from_this<TelnetService::Session>
	{
	public:
		Session(TelnetService& service, std::uint64_t sessionId, asio::ip::tcp::socket socket)
			: service_(service)
			, sessionId_(sessionId)
			, socket_(std::move(socket))
		{
		}

		void Start()
		{
			std::ostringstream welcomeStream;
			welcomeStream
				<< "\r\n"
				<< "========================================\r\n"
				<< "      DE Server Telnet Console\r\n"
				<< "========================================\r\n"
				<< " serverId : " << service_.serverId_ << "\r\n"
				<< " endpoint : 127.0.0.1 (local only)\r\n"
				<< " command  : type 'help' to list commands\r\n"
				<< "========================================\r\n";
			EnqueueWrite(welcomeStream.str());
			EnqueueWrite(kPrompt);
			DoRead();
		}

		void Close()
		{
			if (closed_)
			{
				return;
			}

			closed_ = true;
			boost::system::error_code errorCode;
			socket_.shutdown(asio::ip::tcp::socket::shutdown_both, errorCode);
			socket_.close(errorCode);
			service_.OnSessionClosed(sessionId_);
		}

	private:
		void DoRead()
		{
			auto self = shared_from_this();
			socket_.async_read_some(
				asio::buffer(readBuffer_),
				[self](const boost::system::error_code& errorCode, std::size_t bytesTransferred)
				{
					if (errorCode)
					{
						if (errorCode != asio::error::eof && errorCode != asio::error::operation_aborted)
						{
							Logger::Warn("TelnetSession", "Read failed: " + errorCode.message());
						}
						self->Close();
						return;
					}

					self->ProcessBytes(bytesTransferred);
					if (!self->closed_)
					{
						self->DoRead();
					}
				}
			);
		}

		void ProcessBytes(std::size_t bytesTransferred)
		{
			for (std::size_t index = 0; index < bytesTransferred; ++index)
			{
				HandleByte(readBuffer_[index]);
			}
		}

		void HandleByte(unsigned char value)
		{
			if (inSubnegotiation_)
			{
				if (subnegotiationSawIac_)
				{
					subnegotiationSawIac_ = false;
					if (value == kTelnetSe)
					{
						inSubnegotiation_ = false;
					}
					else if (value != kTelnetIac)
					{
						inSubnegotiation_ = false;
					}
					return;
				}

				subnegotiationSawIac_ = value == kTelnetIac;
				return;
			}

			if (waitingForNegotiationOption_)
			{
				SendNegotiationReply(negotiationCommand_, value);
				waitingForNegotiationOption_ = false;
				negotiationCommand_ = 0;
				return;
			}

			if (sawIac_)
			{
				sawIac_ = false;

				if (value == kTelnetIac)
				{
					return;
				}

				if (value == kTelnetSb)
				{
					inSubnegotiation_ = true;
					subnegotiationSawIac_ = false;
					return;
				}

				if (value == kTelnetDo || value == kTelnetDont || value == kTelnetWill || value == kTelnetWont)
				{
					negotiationCommand_ = value;
					waitingForNegotiationOption_ = true;
				}

				return;
			}

			if (value == kTelnetIac)
			{
				sawIac_ = true;
				return;
			}

			if (value == '\r')
			{
				return;
			}

			if (value == '\n')
			{
				HandleLine();
				return;
			}

			if (value == '\b' || value == 127)
			{
				if (!lineBuffer_.empty())
				{
					lineBuffer_.pop_back();
				}
				return;
			}

			if (std::isprint(value) != 0 || value == '\t')
			{
				lineBuffer_.push_back(static_cast<char>(value));
			}
		}

		void SendNegotiationReply(unsigned char command, unsigned char option)
		{
			unsigned char replyCommand = 0;
			if (command == kTelnetDo || command == kTelnetDont)
			{
				replyCommand = kTelnetWont;
			}
			else if (command == kTelnetWill || command == kTelnetWont)
			{
				replyCommand = kTelnetDont;
			}
			else
			{
				return;
			}

			std::string reply;
			reply.push_back(static_cast<char>(kTelnetIac));
			reply.push_back(static_cast<char>(replyCommand));
			reply.push_back(static_cast<char>(option));
			EnqueueWrite(reply);
		}

		void HandleLine()
		{
			const auto result = service_.HandleCommand(lineBuffer_);
			lineBuffer_.clear();

			if (!result.response.empty())
			{
				std::string response = result.response;
				if (response.size() < 2 || response.substr(response.size() - 2) != "\r\n")
				{
					response.append("\r\n");
				}
				EnqueueWrite(response);
			}

			closeAfterWrite_ = result.closeSession;
			stopServerAfterWrite_ = result.stopServer;

			if (!closeAfterWrite_ && !stopServerAfterWrite_)
			{
				EnqueueWrite(kPrompt);
			}
		}

		void EnqueueWrite(std::string message)
		{
			if (closed_)
			{
				return;
			}

			const bool shouldStartWrite = writeQueue_.empty();
			writeQueue_.push_back(std::move(message));
			if (shouldStartWrite)
			{
				BeginWrite();
			}
		}

		void BeginWrite()
		{
			if (writeQueue_.empty() || closed_)
			{
				return;
			}

			auto self = shared_from_this();
			asio::async_write(
				socket_,
				asio::buffer(writeQueue_.front()),
				[self](const boost::system::error_code& errorCode, std::size_t)
				{
					if (errorCode)
					{
						if (errorCode != asio::error::operation_aborted)
						{
							Logger::Warn("TelnetSession", "Write failed: " + errorCode.message());
						}
						self->Close();
						return;
					}

					self->writeQueue_.pop_front();
					if (!self->writeQueue_.empty())
					{
						self->BeginWrite();
						return;
					}

					const bool shouldClose = self->closeAfterWrite_;
					const bool shouldStop = self->stopServerAfterWrite_;
					self->closeAfterWrite_ = false;
					self->stopServerAfterWrite_ = false;

					if (shouldClose)
					{
						self->Close();
					}

					if (shouldStop && self->service_.stopCallback_)
					{
						self->service_.stopCallback_();
					}
				}
			);
		}

		TelnetService& service_;
		std::uint64_t sessionId_ = 0;
		asio::ip::tcp::socket socket_;
		std::array<unsigned char, 1024> readBuffer_{};
		std::deque<std::string> writeQueue_;
		std::string lineBuffer_;
		bool closed_ = false;
		bool closeAfterWrite_ = false;
		bool stopServerAfterWrite_ = false;
		bool sawIac_ = false;
		bool waitingForNegotiationOption_ = false;
		bool inSubnegotiation_ = false;
		bool subnegotiationSawIac_ = false;
		unsigned char negotiationCommand_ = 0;
	};

	TelnetService::TelnetService(asio::io_context& ioContext, std::string serverId, StopCallback stopCallback)
		: ioContext_(ioContext)
		, acceptor_(ioContext)
		, serverId_(std::move(serverId))
		, stopCallback_(std::move(stopCallback))
		, startTime_(std::chrono::steady_clock::now())
	{
	}

	TelnetService::~TelnetService()
	{
		Stop();
	}

	void TelnetService::Start(const config::TelnetConfig& config)
	{
		if (config.port == 0 || running_)
		{
			return;
		}

		const asio::ip::tcp::endpoint endpoint(asio::ip::address_v4::loopback(), config.port);

		acceptor_.open(endpoint.protocol());
		acceptor_.set_option(asio::ip::tcp::acceptor::reuse_address(true));
		acceptor_.bind(endpoint);
		acceptor_.listen(asio::socket_base::max_listen_connections);

		running_ = true;
		listenPort_ = config.port;
		startTime_ = std::chrono::steady_clock::now();

		Logger::Info(
			"TelnetService",
			"Listening on 127.0.0.1:" + std::to_string(listenPort_) + " for server " + serverId_
		);

		StartAccept();
	}

	void TelnetService::Stop()
	{
		if (!running_ && !acceptor_.is_open() && sessions_.empty())
		{
			return;
		}

		running_ = false;
		listenPort_ = 0;

		boost::system::error_code errorCode;
		acceptor_.close(errorCode);

		auto sessions = std::move(sessions_);
		for (auto& [sessionId, session] : sessions)
		{
			(void)sessionId;
			if (session != nullptr)
			{
				session->Close();
			}
		}
	}

	bool TelnetService::IsRunning() const
	{
		return running_;
	}

	std::uint16_t TelnetService::GetListenPort() const
	{
		return listenPort_;
	}

	TelnetCommandResult TelnetService::HandleCommand(std::string_view commandLine)
	{
		const auto trimmedCommand = Trim(commandLine);
		if (trimmedCommand.empty())
		{
			return {};
		}

		++totalCommands_;

		const auto normalizedCommand = ToLower(trimmedCommand);
		if (normalizedCommand == "help" || normalizedCommand == "?")
		{
			return { BuildHelpResponse(), false, false };
		}

		if (normalizedCommand == "status" || normalizedCommand == "info")
		{
			return { BuildStatusResponse(), false, false };
		}

		if (normalizedCommand == "pid")
		{
			return { "pid: " + std::to_string(CollectProcessMetrics().processId), false, false };
		}

		if (normalizedCommand == "metrics" || normalizedCommand == "stats" || normalizedCommand == "perf")
		{
			return { BuildMetricsResponse(), false, false };
		}

		if (normalizedCommand == "ping")
		{
			return { "pong", false, false };
		}

		if (normalizedCommand == "quit" || normalizedCommand == "exit")
		{
			return { "bye", true, false };
		}

		if (normalizedCommand == "stop" || normalizedCommand == "shutdown")
		{
			return { "server stopping", true, true };
		}

		return {
			"unknown command: " + trimmedCommand + "\r\n"
			"try 'help' to list available commands",
			false,
			false
		};
	}

	void TelnetService::StartAccept()
	{
		if (!running_)
		{
			return;
		}

		acceptor_.async_accept(
			[this](const boost::system::error_code& errorCode, asio::ip::tcp::socket socket)
			{
				if (errorCode)
				{
					if (errorCode != asio::error::operation_aborted)
					{
						Logger::Warn("TelnetService", "Accept failed: " + errorCode.message());
					}
				}
				else
				{
					const auto sessionId = nextSessionId_++;
					auto session = std::make_shared<Session>(*this, sessionId, std::move(socket));
					sessions_.emplace(sessionId, session);
					++totalSessions_;
					session->Start();
				}

				if (running_)
				{
					StartAccept();
				}
			}
		);
	}

	void TelnetService::OnSessionClosed(std::uint64_t sessionId)
	{
		sessions_.erase(sessionId);
	}

	TelnetService::ProcessMetricsSnapshot TelnetService::CollectProcessMetrics() const
	{
		ProcessMetricsSnapshot snapshot;
#ifdef _WIN32
		snapshot.processId = static_cast<std::uint32_t>(::GetCurrentProcessId());

		PROCESS_MEMORY_COUNTERS_EX memoryCounters{};
		if (::GetProcessMemoryInfo(
			::GetCurrentProcess(),
			reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memoryCounters),
			sizeof(memoryCounters)
		) != FALSE)
		{
			snapshot.workingSetBytes = static_cast<std::uint64_t>(memoryCounters.WorkingSetSize);
			snapshot.privateBytes = static_cast<std::uint64_t>(memoryCounters.PrivateUsage);
		}

		DWORD handleCount = 0;
		if (::GetProcessHandleCount(::GetCurrentProcess(), &handleCount) != FALSE)
		{
			snapshot.handleCount = static_cast<std::uint32_t>(handleCount);
		}

		FILETIME creationTime{};
		FILETIME exitTime{};
		FILETIME kernelTime{};
		FILETIME userTime{};
		if (::GetProcessTimes(::GetCurrentProcess(), &creationTime, &exitTime, &kernelTime, &userTime) != FALSE)
		{
			snapshot.kernelTimeMs = FileTimeToMilliseconds(kernelTime);
			snapshot.userTimeMs = FileTimeToMilliseconds(userTime);
		}
#else
		snapshot.processId = static_cast<std::uint32_t>(::getpid());
#endif

		snapshot.uptimeMs = static_cast<std::uint64_t>(
			std::chrono::duration_cast<std::chrono::milliseconds>(
				std::chrono::steady_clock::now() - startTime_
			).count()
		);
		snapshot.activeSessions = sessions_.size();
		snapshot.totalSessions = totalSessions_;
		snapshot.totalCommands = totalCommands_;
		return snapshot;
	}

	std::string TelnetService::BuildHelpResponse() const
	{
		return
			"available commands:\r\n"
			"  help      show this help text\r\n"
			"  status    show basic server status\r\n"
			"  pid       show the current process id\r\n"
			"  metrics   show process performance counters\r\n"
			"  ping      health check\r\n"
			"  stop      stop this server process\r\n"
			"  quit      close this telnet session";
	}

	std::string TelnetService::BuildStatusResponse() const
	{
		const auto snapshot = CollectProcessMetrics();

		std::ostringstream stream;
		stream
			<< "serverId: " << serverId_ << "\r\n"
			<< "telnetListen: 127.0.0.1:" << listenPort_ << "\r\n"
			<< "pid: " << snapshot.processId << "\r\n"
			<< "uptimeMs: " << snapshot.uptimeMs << "\r\n"
			<< "activeSessions: " << snapshot.activeSessions << "\r\n"
			<< "totalSessions: " << snapshot.totalSessions << "\r\n"
			<< "totalCommands: " << snapshot.totalCommands;
		return stream.str();
	}

	std::string TelnetService::BuildMetricsResponse() const
	{
		const auto snapshot = CollectProcessMetrics();

		std::ostringstream stream;
		stream << std::fixed << std::setprecision(2);
		stream
			<< "serverId: " << serverId_ << "\r\n"
			<< "pid: " << snapshot.processId << "\r\n"
			<< "uptimeMs: " << snapshot.uptimeMs << "\r\n"
			<< "workingSetMB: " << BytesToMegabytes(snapshot.workingSetBytes) << "\r\n"
			<< "privateBytesMB: " << BytesToMegabytes(snapshot.privateBytes) << "\r\n"
			<< "handleCount: " << snapshot.handleCount << "\r\n"
			<< "kernelCpuMs: " << snapshot.kernelTimeMs << "\r\n"
			<< "userCpuMs: " << snapshot.userTimeMs << "\r\n"
			<< "activeSessions: " << snapshot.activeSessions << "\r\n"
			<< "totalSessions: " << snapshot.totalSessions << "\r\n"
			<< "totalCommands: " << snapshot.totalCommands;
		return stream.str();
	}
}
