#pragma once

#include "core/ProcessPerformance.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

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

	struct ClientHandShakeMessage
	{
		static constexpr std::uint16_t kCurrentVersion = 1;
		static constexpr std::size_t kWireSize = 12;

		std::uint16_t version = kCurrentVersion;
		std::uint16_t reserved = 0;
		std::uint64_t sessionId = 0;

		std::array<std::uint8_t, kWireSize> Serialize() const;
		static bool TryDeserialize(const void* data, std::size_t size, ClientHandShakeMessage& message);
	};

	struct GuidBytes
	{
		static constexpr std::size_t kWireSize = 16;

		std::array<std::uint8_t, kWireSize> bytes{};

		bool IsEmpty() const;
	};

	struct LoginReqMessage
	{
		static constexpr std::uint16_t kCurrentVersion = 1;
		static constexpr std::size_t kFixedWireSize = 6;

		std::uint16_t version = kCurrentVersion;
		std::uint16_t reserved = 0;
		std::string account;

		static bool TryDeserialize(const void* data, std::size_t size, LoginReqMessage& message);
	};

	struct LoginRspMessage
	{
		static constexpr std::uint16_t kCurrentVersion = 1;
		static constexpr std::size_t kFixedWireSize = 26;

		std::uint16_t version = kCurrentVersion;
		bool isSuccess = false;
		std::uint8_t reserved = 0;
		std::int32_t statusCode = 0;
		GuidBytes avatarId{};
		std::vector<std::byte> avatarData;
		std::string error;

		std::vector<std::byte> Serialize() const;
	};

	struct CreateAvatarReqMessage
	{
		static constexpr std::uint16_t kCurrentVersion = 1;
		static constexpr std::size_t kWireSize = 20;

		std::uint16_t version = kCurrentVersion;
		std::uint16_t reserved = 0;
		GuidBytes avatarId{};

		std::vector<std::byte> Serialize() const;
		static bool TryDeserialize(const void* data, std::size_t size, CreateAvatarReqMessage& message);
	};

	struct CreateAvatarRspMessage
	{
		static constexpr std::uint16_t kCurrentVersion = 1;
		static constexpr std::size_t kFixedWireSize = 26;

		std::uint16_t version = kCurrentVersion;
		bool isSuccess = false;
		std::uint8_t reserved = 0;
		std::int32_t statusCode = 0;
		GuidBytes avatarId{};
		std::vector<std::byte> avatarData;
		std::string error;

		std::vector<std::byte> Serialize() const;
		static bool TryDeserialize(const void* data, std::size_t size, CreateAvatarRspMessage& message);
	};
}
