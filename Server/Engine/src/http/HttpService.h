#pragma once

#include "config/ClusterConfig.h"
#include "core/BoostAsio.h"

#include <functional>
#include <memory>
#include <string>
#include <unordered_map>

namespace de::server::engine
{
	struct HttpRequest
	{
		std::string method;
		std::string target;
		std::string version;
	};

	struct HttpResponse
	{
		int statusCode = 200;
		std::string statusText = "OK";
		std::string contentType = "application/json; charset=utf-8";
		std::string body;
	};

	class HttpService
	{
	public:
		using RequestHandler = std::function<HttpResponse(const HttpRequest&)>;

		HttpService(asio::io_context& ioContext, std::string serverId, RequestHandler requestHandler);
		~HttpService();

		void Start(const config::HttpConfig& config);
		void Stop();

		bool IsRunning() const;
		HttpResponse HandleRequest(const HttpRequest& request) const;

	private:
		class Session;

		void StartAccept();
		void OnSessionClosed(std::uint64_t sessionId);
		std::string BuildHttpResponseText(const HttpResponse& response) const;

		asio::io_context& ioContext_;
		asio::ip::tcp::acceptor acceptor_;
		std::string serverId_;
		RequestHandler requestHandler_;
		bool running_ = false;
		std::uint64_t nextSessionId_ = 1;
		std::unordered_map<std::uint64_t, std::shared_ptr<Session>> sessions_;
	};
}
