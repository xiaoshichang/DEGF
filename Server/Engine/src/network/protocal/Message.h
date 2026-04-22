#pragma once

#include "core/ProcessPerformance.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace de::server::engine::network
{
	struct HeartBeatWithDataNtfMessage
	{
		static constexpr std::uint16_t kCurrentVersion = 1;
		static constexpr std::size_t kWireSize = 12;

		std::uint16_t version = kCurrentVersion;
		std::uint16_t reserved = 0;
		ProcessPerformanceSnapshot performance;

		std::array<std::uint8_t, kWireSize> Serialize() const;
		static bool TryDeserialize(const void* data, std::size_t size, HeartBeatWithDataNtfMessage& message);
	};
}
