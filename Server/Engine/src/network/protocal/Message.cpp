#include "network/protocal/Message.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

namespace de::server::engine::network
{
	namespace
	{
		void WriteUInt16BigEndian(std::uint8_t* buffer, std::uint16_t value)
		{
			buffer[0] = static_cast<std::uint8_t>((value >> 8u) & 0xffu);
			buffer[1] = static_cast<std::uint8_t>(value & 0xffu);
		}

		void WriteUInt32BigEndian(std::uint8_t* buffer, std::uint32_t value)
		{
			buffer[0] = static_cast<std::uint8_t>((value >> 24u) & 0xffu);
			buffer[1] = static_cast<std::uint8_t>((value >> 16u) & 0xffu);
			buffer[2] = static_cast<std::uint8_t>((value >> 8u) & 0xffu);
			buffer[3] = static_cast<std::uint8_t>(value & 0xffu);
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

		std::uint32_t ReadUInt32BigEndian(const std::uint8_t* buffer)
		{
			return (static_cast<std::uint32_t>(buffer[0]) << 24u)
				| (static_cast<std::uint32_t>(buffer[1]) << 16u)
				| (static_cast<std::uint32_t>(buffer[2]) << 8u)
				| static_cast<std::uint32_t>(buffer[3]);
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

		std::vector<std::byte> ToByteVector(const std::uint8_t* data, std::size_t size)
		{
			std::vector<std::byte> bytes(size);
			if (size > 0)
			{
				std::memcpy(bytes.data(), data, size);
			}

			return bytes;
		}

		void AppendStringWithUInt16Length(std::vector<std::uint8_t>& bytes, std::size_t lengthOffset, std::size_t dataOffset, const std::string& value)
		{
			if (value.size() > static_cast<std::size_t>(std::numeric_limits<std::uint16_t>::max()))
			{
				throw std::invalid_argument("Message string field is too long.");
			}

			WriteUInt16BigEndian(bytes.data() + lengthOffset, static_cast<std::uint16_t>(value.size()));
			if (!value.empty())
			{
				std::memcpy(bytes.data() + dataOffset, value.data(), value.size());
			}
		}

		void AppendBytes(std::vector<std::uint8_t>& bytes, std::size_t lengthOffset, std::size_t dataOffset, const std::vector<std::byte>& value)
		{
			if (value.size() > static_cast<std::size_t>(std::numeric_limits<std::uint32_t>::max()))
			{
				throw std::invalid_argument("Message binary field is too long.");
			}

			WriteUInt32BigEndian(bytes.data() + lengthOffset, static_cast<std::uint32_t>(value.size()));
			if (!value.empty())
			{
				std::memcpy(bytes.data() + dataOffset, value.data(), value.size());
			}
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

	std::array<std::uint8_t, ClientHandShakeMessage::kWireSize> ClientHandShakeMessage::Serialize() const
	{
		std::array<std::uint8_t, kWireSize> bytes{};
		WriteUInt16BigEndian(bytes.data(), version);
		WriteUInt16BigEndian(bytes.data() + 2, reserved);
		WriteUInt64BigEndian(bytes.data() + 4, sessionId);
		return bytes;
	}

	bool ClientHandShakeMessage::TryDeserialize(const void* data, std::size_t size, ClientHandShakeMessage& message)
	{
		if (data == nullptr || size != kWireSize)
		{
			return false;
		}

		const auto* bytes = static_cast<const std::uint8_t*>(data);
		ClientHandShakeMessage parsed;
		parsed.version = ReadUInt16BigEndian(bytes);
		parsed.reserved = ReadUInt16BigEndian(bytes + 2);
		parsed.sessionId = ReadUInt64BigEndian(bytes + 4);
		if (parsed.version != kCurrentVersion || parsed.sessionId == 0)
		{
			return false;
		}

		message = parsed;
		return true;
	}

	bool GuidBytes::IsEmpty() const
	{
		return std::all_of(
			bytes.begin(),
			bytes.end(),
			[](std::uint8_t value)
			{
				return value == 0;
			}
		);
	}

	bool LoginReqMessage::TryDeserialize(const void* data, std::size_t size, LoginReqMessage& message)
	{
		if (data == nullptr || size < kFixedWireSize)
		{
			return false;
		}

		const auto* bytes = static_cast<const std::uint8_t*>(data);
		LoginReqMessage parsed;
		parsed.version = ReadUInt16BigEndian(bytes);
		parsed.reserved = ReadUInt16BigEndian(bytes + 2);
		const auto accountLength = static_cast<std::size_t>(ReadUInt16BigEndian(bytes + 4));
		if (parsed.version != kCurrentVersion || size != kFixedWireSize + accountLength)
		{
			return false;
		}

		parsed.account.assign(reinterpret_cast<const char*>(bytes + kFixedWireSize), accountLength);
		message = std::move(parsed);
		return true;
	}

	std::vector<std::byte> LoginRspMessage::Serialize() const
	{
		std::vector<std::uint8_t> bytes(kFixedWireSize + error.size() + avatarData.size());
		WriteUInt16BigEndian(bytes.data(), version);
		bytes[2] = isSuccess ? 1 : 0;
		bytes[3] = reserved;
		WriteUInt32BigEndian(bytes.data() + 4, static_cast<std::uint32_t>(statusCode));
		std::memcpy(bytes.data() + 8, avatarId.bytes.data(), avatarId.bytes.size());
		AppendStringWithUInt16Length(bytes, 24, kFixedWireSize, error);
		AppendBytes(bytes, 26, kFixedWireSize + error.size(), avatarData);
		return ToByteVector(bytes.data(), bytes.size());
	}

	std::vector<std::byte> CreateAvatarReqMessage::Serialize() const
	{
		std::array<std::uint8_t, kWireSize> bytes{};
		WriteUInt16BigEndian(bytes.data(), version);
		WriteUInt16BigEndian(bytes.data() + 2, reserved);
		std::memcpy(bytes.data() + 4, avatarId.bytes.data(), avatarId.bytes.size());
		return ToByteVector(bytes.data(), bytes.size());
	}

	bool CreateAvatarReqMessage::TryDeserialize(const void* data, std::size_t size, CreateAvatarReqMessage& message)
	{
		if (data == nullptr || size != kWireSize)
		{
			return false;
		}

		const auto* bytes = static_cast<const std::uint8_t*>(data);
		CreateAvatarReqMessage parsed;
		parsed.version = ReadUInt16BigEndian(bytes);
		parsed.reserved = ReadUInt16BigEndian(bytes + 2);
		std::memcpy(parsed.avatarId.bytes.data(), bytes + 4, parsed.avatarId.bytes.size());
		if (parsed.version != kCurrentVersion || parsed.avatarId.IsEmpty())
		{
			return false;
		}

		message = parsed;
		return true;
	}

	std::vector<std::byte> CreateAvatarRspMessage::Serialize() const
	{
		std::vector<std::uint8_t> bytes(kFixedWireSize + error.size() + avatarData.size());
		WriteUInt16BigEndian(bytes.data(), version);
		bytes[2] = isSuccess ? 1 : 0;
		bytes[3] = reserved;
		WriteUInt32BigEndian(bytes.data() + 4, static_cast<std::uint32_t>(statusCode));
		std::memcpy(bytes.data() + 8, avatarId.bytes.data(), avatarId.bytes.size());
		AppendStringWithUInt16Length(bytes, 24, kFixedWireSize, error);
		AppendBytes(bytes, 26, kFixedWireSize + error.size(), avatarData);
		return ToByteVector(bytes.data(), bytes.size());
	}

	bool CreateAvatarRspMessage::TryDeserialize(const void* data, std::size_t size, CreateAvatarRspMessage& message)
	{
		if (data == nullptr || size < kFixedWireSize)
		{
			return false;
		}

		const auto* bytes = static_cast<const std::uint8_t*>(data);
		CreateAvatarRspMessage parsed;
		parsed.version = ReadUInt16BigEndian(bytes);
		parsed.isSuccess = bytes[2] != 0;
		parsed.reserved = bytes[3];
		parsed.statusCode = static_cast<std::int32_t>(ReadUInt32BigEndian(bytes + 4));
		std::memcpy(parsed.avatarId.bytes.data(), bytes + 8, parsed.avatarId.bytes.size());
		const auto errorLength = static_cast<std::size_t>(ReadUInt16BigEndian(bytes + 24));
		const auto avatarDataLength = static_cast<std::size_t>(ReadUInt32BigEndian(bytes + 26));
		if (parsed.version != kCurrentVersion || size != kFixedWireSize + errorLength + avatarDataLength)
		{
			return false;
		}

		parsed.error.assign(reinterpret_cast<const char*>(bytes + kFixedWireSize), errorLength);
		parsed.avatarData.resize(avatarDataLength);
		if (avatarDataLength > 0)
		{
			std::memcpy(parsed.avatarData.data(), bytes + kFixedWireSize + errorLength, avatarDataLength);
		}

		message = std::move(parsed);
		return true;
	}
}
