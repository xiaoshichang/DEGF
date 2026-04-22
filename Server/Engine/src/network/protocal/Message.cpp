#include "network/protocal/Message.h"

namespace de::server::engine::network
{
	namespace
	{
		void WriteUInt16BigEndian(std::uint8_t* buffer, std::uint16_t value)
		{
			buffer[0] = static_cast<std::uint8_t>((value >> 8u) & 0xffu);
			buffer[1] = static_cast<std::uint8_t>(value & 0xffu);
		}

		void WriteUInt64BigEndian(std::uint8_t* buffer, std::uint64_t value)
		{
			buffer[0] = static_cast<std::uint8_t>((value >> 56u) & 0xffu);
			buffer[1] = static_cast<std::uint8_t>((value >> 48u) & 0xffu);
			buffer[2] = static_cast<std::uint8_t>((value >> 40u) & 0xffu);
			buffer[3] = static_cast<std::uint8_t>((value >> 32u) & 0xffu);
			buffer[4] = static_cast<std::uint8_t>((value >> 24u) & 0xffu);
			buffer[5] = static_cast<std::uint8_t>((value >> 16u) & 0xffu);
			buffer[6] = static_cast<std::uint8_t>((value >> 8u) & 0xffu);
			buffer[7] = static_cast<std::uint8_t>(value & 0xffu);
		}

		std::uint16_t ReadUInt16BigEndian(const std::uint8_t* buffer)
		{
			return static_cast<std::uint16_t>(
				(static_cast<std::uint16_t>(buffer[0]) << 8u)
				| static_cast<std::uint16_t>(buffer[1])
			);
		}

		std::uint64_t ReadUInt64BigEndian(const std::uint8_t* buffer)
		{
			return (static_cast<std::uint64_t>(buffer[0]) << 56u)
				| (static_cast<std::uint64_t>(buffer[1]) << 48u)
				| (static_cast<std::uint64_t>(buffer[2]) << 40u)
				| (static_cast<std::uint64_t>(buffer[3]) << 32u)
				| (static_cast<std::uint64_t>(buffer[4]) << 24u)
				| (static_cast<std::uint64_t>(buffer[5]) << 16u)
				| (static_cast<std::uint64_t>(buffer[6]) << 8u)
				| static_cast<std::uint64_t>(buffer[7]);
		}
	}

	std::array<std::uint8_t, HeartBeatWithDataNtfMessage::kWireSize> HeartBeatWithDataNtfMessage::Serialize() const
	{
		std::array<std::uint8_t, kWireSize> bytes{};
		WriteUInt16BigEndian(bytes.data(), version);
		WriteUInt16BigEndian(bytes.data() + 2, reserved);
		WriteUInt64BigEndian(bytes.data() + 4, performance.workingSetBytes);
		return bytes;
	}

	bool HeartBeatWithDataNtfMessage::TryDeserialize(const void* data, std::size_t size, HeartBeatWithDataNtfMessage& message)
	{
		if (data == nullptr || size != kWireSize)
		{
			return false;
		}

		const auto* bytes = static_cast<const std::uint8_t*>(data);
		HeartBeatWithDataNtfMessage parsed;
		parsed.version = ReadUInt16BigEndian(bytes);
		parsed.reserved = ReadUInt16BigEndian(bytes + 2);
		parsed.performance.workingSetBytes = ReadUInt64BigEndian(bytes + 4);
		if (parsed.version != kCurrentVersion)
		{
			return false;
		}

		message = parsed;
		return true;
	}
}
