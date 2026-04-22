#include "http/HttpService.h"

#include "core/Logger.h"

#include <cctype>
#include <deque>
#include <sstream>
#include <string>
#include <utility>

namespace de::server::engine
{
	namespace
	{
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
	}

	class HttpService::Session : public std::enable_shared_from_this<HttpService::Session>
	{
	public:
		Session(HttpService& service, std::uint64_t sessionId, asio::ip::tcp::socket socket)
			: service_(service)
			, sessionId_(sessionId)
			, socket_(std::move(socket))
		{
		}

		void Start()
		{
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
			asio::async_read_until(
				socket_,
				readBuffer_,
				"\r\n\r\n",
				[self](const boost::system::error_code& errorCode, std::size_t)
				{
					if (errorCode)
					{
						if (errorCode != asio::error::eof && errorCode != asio::error::operation_aborted)
						{
							Logger::Warn("HttpSession", "Read failed: " + errorCode.message());
						}

						self->Close();
						return;
					}

					self->HandleRequest();
				}
			);
		}

		void HandleRequest()
		{
			std::istream input(&readBuffer_);
			std::string requestLine;
			std::getline(input, requestLine);
			if (!requestLine.empty() && requestLine.back() == '\r')
			{
				requestLine.pop_back();
			}

			std::istringstream lineStream(requestLine);
			std::string method;
			std::string target;
			std::string version;
			lineStream >> method >> target >> version;

			HttpResponse response;
			if (method.empty() || target.empty())
			{
				response.statusCode = 400;
				response.statusText = "Bad Request";
				response.body = R"({"error":"invalid request line"})";
			}
			else
			{
				response = service_.HandleRequest(Trim(method), Trim(target));
			}

			EnqueueWrite(service_.BuildHttpResponseText(response));
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
							Logger::Warn("HttpSession", "Write failed: " + errorCode.message());
						}

						self->Close();
						return;
					}

					self->writeQueue_.pop_front();
					self->Close();
				}
			);
		}

		HttpService& service_;
		std::uint64_t sessionId_ = 0;
		asio::ip::tcp::socket socket_;
		asio::streambuf readBuffer_;
		std::deque<std::string> writeQueue_;
		bool closed_ = false;
	};

	HttpService::HttpService(asio::io_context& ioContext, std::string serverId, PerformanceProvider performanceProvider)
		: ioContext_(ioContext)
		, acceptor_(ioContext)
		, serverId_(std::move(serverId))
		, performanceProvider_(std::move(performanceProvider))
	{
	}

	HttpService::~HttpService()
	{
		Stop();
	}

	void HttpService::Start(const config::HttpConfig& config)
	{
		if (running_ || config.listenEndpoint.host.empty() || config.listenEndpoint.port == 0)
		{
			return;
		}

		const auto address = asio::ip::make_address(config.listenEndpoint.host);
		const asio::ip::tcp::endpoint endpoint(address, config.listenEndpoint.port);

		acceptor_.open(endpoint.protocol());
		acceptor_.set_option(asio::ip::tcp::acceptor::reuse_address(true));
		acceptor_.bind(endpoint);
		acceptor_.listen(asio::socket_base::max_listen_connections);

		running_ = true;
		Logger::Info(
			"HttpService",
			"Listening on " + config.listenEndpoint.host + ":" + std::to_string(config.listenEndpoint.port) + " for server " + serverId_
		);

		StartAccept();
	}

	void HttpService::Stop()
	{
		if (!running_ && !acceptor_.is_open() && sessions_.empty())
		{
			return;
		}

		running_ = false;

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

	bool HttpService::IsRunning() const
	{
		return running_;
	}

	HttpResponse HttpService::HandleRequest(std::string_view method, std::string_view target) const
	{
		if (method != "GET")
		{
			return HttpResponse{
				405,
				"Method Not Allowed",
				"application/json; charset=utf-8",
				R"({"error":"method not allowed"})"
			};
		}

		if (target == "/performance" || target == "/api/performance")
		{
			return HttpResponse{
				200,
				"OK",
				"application/json; charset=utf-8",
				performanceProvider_ ? performanceProvider_() : R"({"nodes":{}})"
			};
		}

		return HttpResponse{
			404,
			"Not Found",
			"application/json; charset=utf-8",
			R"({"error":"not found"})"
		};
	}

	void HttpService::StartAccept()
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
						Logger::Warn("HttpService", "Accept failed: " + errorCode.message());
					}
				}
				else
				{
					const auto sessionId = nextSessionId_++;
					auto session = std::make_shared<Session>(*this, sessionId, std::move(socket));
					sessions_.emplace(sessionId, session);
					session->Start();
				}

				if (running_)
				{
					StartAccept();
				}
			}
		);
	}

	void HttpService::OnSessionClosed(std::uint64_t sessionId)
	{
		sessions_.erase(sessionId);
	}

	std::string HttpService::BuildHttpResponseText(const HttpResponse& response) const
	{
		std::ostringstream stream;
		stream
			<< "HTTP/1.1 " << response.statusCode << ' ' << response.statusText << "\r\n"
			<< "Content-Type: " << response.contentType << "\r\n"
			<< "Content-Length: " << response.body.size() << "\r\n"
			<< "Connection: close\r\n"
			<< "\r\n"
			<< response.body;
		return stream.str();
	}
}
