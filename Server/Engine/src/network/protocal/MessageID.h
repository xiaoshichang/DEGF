#pragma once

#include <cstdint>

namespace de::server::engine::network
{
	enum class MessageID : std::uint32_t
	{
		HandShakeReq = 1,
		HandShakeRsp = 2,
		HeartBeatWithDataNtf = 3,
		HeartBeatNtf = 4,

		AllNodeReadyNtf = 5,
		GameReadyNtf = 6,
		OpenGateNtf = 7,
	};
}
