#pragma once

#include <cstdint>

namespace de::server::engine::network
{
	namespace MessageID
	{
		inline constexpr std::uint32_t kCategoryMask = 0xffff0000u;
		inline constexpr std::uint32_t kCSCategory = 0x00010000u;
		inline constexpr std::uint32_t kSSCategory = 0x00020000u;

		enum class CS : std::uint32_t
		{
			HandShakeReq = 0x00010001u,
			HandShakeRsp = 0x00010002u,
			HeartBeatNtf = 0x00010003u,
		};

		enum class SS : std::uint32_t
		{
			HandShakeReq = 0x00020001u,
			HandShakeRsp = 0x00020002u,
			HeartBeatWithDataNtf = 0x00020003u,
			AllNodeReadyNtf = 0x00020004u,
			GameReadyNtf = 0x00020005u,
			OpenGateNtf = 0x00020006u,
		};

		constexpr bool IsCS(std::uint32_t messageId)
		{
			return (messageId & kCategoryMask) == kCSCategory;
		}

		constexpr bool IsSS(std::uint32_t messageId)
		{
			return (messageId & kCategoryMask) == kSSCategory;
		}
	}
}
