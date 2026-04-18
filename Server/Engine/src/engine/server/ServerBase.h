#pragma once

#include <string>

namespace de::server::engine
{
	class ServerBase
	{
	public:
		explicit ServerBase(std::string serverId);
		virtual ~ServerBase() = default;
		const std::string& GetServerId() const;
		virtual void Init() = 0;
		virtual void Run() = 0;
		virtual void Uninit() = 0;

	private:
		std::string serverId_;
	};
}
