#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace de::server::engine::network::protocal
{
	constexpr std::uint32_t MakeMagic(char a, char b, char c, char d)
	{
		return (static_cast<std::uint32_t>(static_cast<unsigned char>(a)) << 24u)
			| (static_cast<std::uint32_t>(static_cast<unsigned char>(b)) << 16u)
			| (static_cast<std::uint32_t>(static_cast<unsigned char>(c)) << 8u)
			| static_cast<std::uint32_t>(static_cast<unsigned char>(d));
	}

	struct Header
	{
		static constexpr std::uint16_t kCurrentVersion = 1;
		static constexpr std::uint16_t kWireSize = 24;
		static constexpr std::uint32_t kDefaultFlags = 0;
		static constexpr std::uint32_t kMagic = MakeMagic('D', 'E', 'N', 'G');

		std::uint32_t magic = 0;
		std::uint16_t version = kCurrentVersion;
		std::uint16_t headerLength = kWireSize;
		// Payload length in bytes. Full frame length = headerLength + length.
		std::uint32_t length = 0;
		std::uint32_t messageId = 0;
		std::uint32_t flags = kDefaultFlags;
		std::uint32_t reserved = 0;

		static Header CreateInner(
			std::uint32_t messageId,
			std::uint32_t payloadLength,
			std::uint32_t flags = kDefaultFlags
		);

		static Header CreateClient(
			std::uint32_t messageId,
			std::uint32_t payloadLength,
			std::uint32_t flags = kDefaultFlags
		);

		static bool TryDeserialize(const void* data, std::size_t dataSize, Header& header);

		bool HasValidMagic() const;
		bool HasValidLayout() const;
		bool IsValid() const;
		std::uint32_t GetFrameLength() const;
		// Serialized in network byte order (big-endian).
		std::array<std::uint8_t, kWireSize> Serialize() const;
	};
}
