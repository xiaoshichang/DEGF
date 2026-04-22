#pragma once

#include <cstdint>

namespace de::server::engine::network
{
	enum class MessageID : std::uint32_t
	{
		HandShakeReq = 1,
		HandShakeRsp = 2,
		HeartBeat = 3
	};
}
