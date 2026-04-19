#include "network/protocal/Header.h"

namespace de::server::engine::network
{
	namespace
	{
		Header CreateHeader(std::uint32_t magic, std::uint32_t messageId, std::uint32_t payloadLength, std::uint32_t flags)
		{
			Header header;
			header.magic = magic;
			header.length = payloadLength;
			header.messageId = messageId;
			header.flags = flags;
			return header;
		}

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
	}

	Header Header::CreateInner(std::uint32_t messageId, std::uint32_t payloadLength, std::uint32_t flags)
	{
		return CreateHeader(kMagic, messageId, payloadLength, flags);
	}

	Header Header::CreateClient(std::uint32_t messageId, std::uint32_t payloadLength, std::uint32_t flags)
	{
		return CreateHeader(kMagic, messageId, payloadLength, flags);
	}

	bool Header::TryDeserialize(const void* data, std::size_t dataSize, Header& header)
	{
		if (data == nullptr || dataSize < kWireSize)
		{
			return false;
		}

		const auto* bytes = static_cast<const std::uint8_t*>(data);

		Header parsedHeader;
		parsedHeader.magic = ReadUInt32BigEndian(bytes);
		parsedHeader.version = ReadUInt16BigEndian(bytes + 4);
		parsedHeader.headerLength = ReadUInt16BigEndian(bytes + 6);
		parsedHeader.length = ReadUInt32BigEndian(bytes + 8);
		parsedHeader.messageId = ReadUInt32BigEndian(bytes + 12);
		parsedHeader.flags = ReadUInt32BigEndian(bytes + 16);
		parsedHeader.reserved = ReadUInt32BigEndian(bytes + 20);

		if (!parsedHeader.IsValid())
		{
			return false;
		}

		header = parsedHeader;
		return true;
	}

	bool Header::HasValidMagic() const
	{
		return magic == kMagic;
	}

	bool Header::HasValidLayout() const
	{
		return version == kCurrentVersion && headerLength == kWireSize;
	}

	bool Header::IsValid() const
	{
		return HasValidMagic() && HasValidLayout();
	}

	std::uint32_t Header::GetFrameLength() const
	{
		return static_cast<std::uint32_t>(headerLength) + length;
	}

	std::array<std::uint8_t, Header::kWireSize> Header::Serialize() const
	{
		std::array<std::uint8_t, Header::kWireSize> bytes{};

		WriteUInt32BigEndian(bytes.data(), magic);
		WriteUInt16BigEndian(bytes.data() + 4, version);
		WriteUInt16BigEndian(bytes.data() + 6, headerLength);
		WriteUInt32BigEndian(bytes.data() + 8, length);
		WriteUInt32BigEndian(bytes.data() + 12, messageId);
		WriteUInt32BigEndian(bytes.data() + 16, flags);
		WriteUInt32BigEndian(bytes.data() + 20, reserved);

		return bytes;
	}
}
