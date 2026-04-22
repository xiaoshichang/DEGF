#include "network/inner/InnerNetwork.h"

#include "core/Logger.h"
#include "network/protocal/Header.h"
#include "network/protocal/MessageID.h"

#include <algorithm>
#include <array>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

namespace de::server::engine::network
{
	namespace
	{
		struct HandShakePacket
		{
			std::uint16_t ServerIDLength = 0;
		};

		std::string ZMQErrorMessage(const std::string& prefix, const boost::system::error_code& error)
		{
			return prefix + ": " + error.message();
		}

		std::vector<std::byte> ToByteVector(const void* data, std::size_t size)
		{
			const auto* begin = static_cast<const std::byte*>(data);
			return std::vector<std::byte>(begin, begin + size);
		}

		std::vector<std::byte> StringToBytes(const std::string& value)
		{
			const auto* begin = reinterpret_cast<const std::byte*>(value.data());
			return std::vector<std::byte>(begin, begin + value.size());
		}

		std::string BytesToString(const std::vector<std::byte>& value)
		{
			const auto* begin = reinterpret_cast<const char*>(value.data());
			return std::string(begin, begin + value.size());
		}

		InnerNetworkSession::RoutingId ServerIDToRoutingId(const std::string& serverID)
		{
			return StringToBytes(serverID);
		}

		azmq::message CreateMessage(const void* data, std::size_t size)
		{
			azmq::message message(size);
			if (size > 0)
			{
				std::memcpy(boost::asio::buffer_cast<void*>(message.buffer()), data, size);
			}

			return message;
		}

		azmq::message CreateMessage(const std::vector<std::byte>& data)
		{
			return CreateMessage(data.data(), data.size());
		}

		std::vector<std::byte> SerializeHandShakePacket(const std::string& serverID)
		{
			if (serverID.size() > static_cast<std::size_t>(std::numeric_limits<std::uint16_t>::max()))
			{
				throw std::invalid_argument("ServerID is too long for handshake packet.");
			}

			HandShakePacket packet;
			packet.ServerIDLength = static_cast<std::uint16_t>(serverID.size());

			std::vector<std::byte> bytes(sizeof(HandShakePacket) + serverID.size());
			std::memcpy(bytes.data(), &packet, sizeof(HandShakePacket));
			if (!serverID.empty())
			{
				std::memcpy(bytes.data() + sizeof(HandShakePacket), serverID.data(), serverID.size());
			}

			return bytes;
		}

		bool TryDeserializeHandShakePacket(const std::vector<std::byte>& payload, std::string& serverID)
		{
			if (payload.size() < sizeof(HandShakePacket))
			{
				return false;
			}

			HandShakePacket packet{};
			std::memcpy(&packet, payload.data(), sizeof(HandShakePacket));
			const std::size_t expectedSize = sizeof(HandShakePacket) + static_cast<std::size_t>(packet.ServerIDLength);
			if (payload.size() != expectedSize)
			{
				return false;
			}

			serverID.assign(
				reinterpret_cast<const char*>(payload.data() + sizeof(HandShakePacket)),
				static_cast<std::size_t>(packet.ServerIDLength)
			);
			return !serverID.empty();
		}

		bool TryReceiveMultipart(azmq::socket& socket, azmq::message& firstPart, azmq::message_vector& parts)
		{
			parts.clear();
			parts.emplace_back(firstPart);
			if (!firstPart.more())
			{
				return true;
			}

			boost::system::error_code error;
			socket.receive_more(parts, 0, error);
			return !error;
		}

		bool TryDeserializeStructuredFrame(
			const azmq::message_vector& parts,
			std::vector<std::vector<std::byte>>& prefixFrames,
			Header& header,
			std::vector<std::byte>& payload
		)
		{
			prefixFrames.clear();
			payload.clear();

			if (parts.empty())
			{
				return false;
			}

			std::size_t headerIndex = parts.size();
			for (std::size_t index = 0; index < parts.size(); ++index)
			{
				const auto& part = parts[index];
				if (Header::TryDeserialize(part.data(), part.size(), header))
				{
					headerIndex = index;
					break;
				}

				prefixFrames.emplace_back(ToByteVector(part.data(), part.size()));
			}

			if (headerIndex == parts.size())
			{
				return false;
			}

			if (headerIndex + 1 < parts.size())
			{
				if (headerIndex + 2 != parts.size())
				{
					return false;
				}

				const auto& payloadPart = parts[headerIndex + 1];
				payload = ToByteVector(payloadPart.data(), payloadPart.size());
			}

			return true;
		}

		bool SendFrame(
			azmq::socket& socket,
			std::uint32_t messageID,
			const std::vector<std::byte>& payload,
			const InnerNetworkSession::RoutingId* routingId,
			boost::system::error_code& error
		)
		{
			const auto header = Header::CreateInner(messageID, static_cast<std::uint32_t>(payload.size()));
			const auto serializedHeader = header.Serialize();
			auto headerMessage = CreateMessage(serializedHeader.data(), serializedHeader.size());
			auto payloadMessage = CreateMessage(payload);

			if (routingId != nullptr)
			{
				auto routingMessage = CreateMessage(routingId->data(), routingId->size());
				std::array<azmq::message, 3> frames
				{
					std::move(routingMessage),
					std::move(headerMessage),
					std::move(payloadMessage)
				};
				socket.send(frames, 0, error);
			}
			else
			{
				std::array<azmq::message, 2> frames
				{
					std::move(headerMessage),
					std::move(payloadMessage)
				};
				socket.send(frames, 0, error);
			}

			return !error;
		}
	}

	InnerNetwork::InnerNetwork(const std::string& serverID, asio::io_context& ioContext, InnerNetworkCallbacks callbacks)
		: ServerID_(serverID)
		, IOContext_(ioContext)
		, Callbacks_(callbacks)
	{
	}

	InnerNetwork::~InnerNetwork()
	{
		ShuttingDown_ = true;
		DestroySessions();

		if (ListenSocket_ != nullptr)
		{
			boost::system::error_code error;
			ListenSocket_->cancel(error);
			ListenSocket_.reset();
		}
	}

	bool InnerNetwork::Listen(const std::string& endpoint)
	{
		if (Listening_)
		{
			Logger::Warn("InnerNetwork", "Listen called more than once.");
			return false;
		}

		try
		{
			ListenSocket_ = std::make_unique<azmq::socket>(IOContext_, ZMQ_ROUTER, true);
			ListenSocket_->set_option(azmq::socket::linger(0));
			ListenSocket_->set_option(azmq::socket::router_mandatory(true));

			boost::system::error_code error;
			ListenSocket_->bind(endpoint, error);
			if (error)
			{
				Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to bind ROUTER socket to " + endpoint, error));
				ListenSocket_.reset();
				return false;
			}

			Listening_ = true;
			StartListenReceive();
			Logger::Info("InnerNetwork", "Listening on " + endpoint);
			return true;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("InnerNetwork", std::string("Failed to listen on ") + endpoint + ": " + exception.what());
			ListenSocket_.reset();
			return false;
		}
	}

	InnerNetworkSession* InnerNetwork::ConnectTo(const std::string& endpoint)
	{
		auto* session = CreateConnectSession(endpoint);
		if (session == nullptr)
		{
			return nullptr;
		}

		auto* entry = FindConnectSession(session->GetSessionId());
		if (entry == nullptr || entry->Socket == nullptr)
		{
			DestroyConnectSession(session->GetSessionId());
			return nullptr;
		}

		try
		{
			entry->Socket->set_option(azmq::socket::identity(ServerID_));
			entry->Socket->set_option(azmq::socket::linger(0));
			entry->Socket->set_option(azmq::socket::immediate(true));

			boost::system::error_code error;
			entry->Socket->connect(endpoint, error);
			if (error)
			{
				Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to connect DEALER socket to " + endpoint, error));
				DestroyConnectSession(session->GetSessionId());
				return nullptr;
			}

			if (!SendFrame(
				*entry->Socket,
				static_cast<std::uint32_t>(MessageID::HandShakeReq),
				SerializeHandShakePacket(ServerID_),
				nullptr,
				error
			))
			{
				Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to send handshake request to " + endpoint, error));
				DestroyConnectSession(session->GetSessionId());
				return nullptr;
			}

			StartConnectReceive(session->GetSessionId());
			return session;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("InnerNetwork", std::string("Failed to connect to ") + endpoint + ": " + exception.what());
			DestroyConnectSession(session->GetSessionId());
			return nullptr;
		}
	}

	bool InnerNetwork::Send(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data)
	{
		const auto iterator = ServerIDToSession_.find(serverID);
		if (iterator == ServerIDToSession_.end())
		{
			Logger::Warn("InnerNetwork", "Send target not found: " + serverID);
			return false;
		}

		const SessionId sessionId = iterator->second;
		boost::system::error_code error;

		if (auto* entry = FindConnectSession(sessionId); entry != nullptr)
		{
			if (entry->Session->GetSessionState() != InnerNetworkSessionState::Registered)
			{
				Logger::Warn("InnerNetwork", "Send target is not registered yet: " + serverID);
				return false;
			}

			if (!SendFrame(*entry->Socket, messageID, data, nullptr, error))
			{
				Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to send payload to connected session " + serverID, error));
				return false;
			}

			return true;
		}

		auto* listenSession = FindListenSession(sessionId);
		if (listenSession == nullptr)
		{
			Logger::Warn("InnerNetwork", "Send session not found for server: " + serverID);
			return false;
		}

		if (listenSession->GetSessionState() != InnerNetworkSessionState::Registered)
		{
			Logger::Warn("InnerNetwork", "Send target is not registered yet: " + serverID);
			return false;
		}

		if (ListenSocket_ == nullptr)
		{
			Logger::Warn("InnerNetwork", "ROUTER socket is not available when sending to " + serverID);
			return false;
		}

		if (!SendFrame(*ListenSocket_, messageID, data, &listenSession->GetRoutingId(), error))
		{
			Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to send payload through ROUTER to " + serverID, error));
			return false;
		}

		return true;
	}

	bool InnerNetwork::HasRegisteredSession(const std::string& serverID) const
	{
		const auto iterator = ServerIDToSession_.find(serverID);
		if (iterator == ServerIDToSession_.end())
		{
			return false;
		}

		const SessionId sessionId = iterator->second;
		const auto connectIterator = SessionsFromConnect_.find(sessionId);
		if (connectIterator != SessionsFromConnect_.end())
		{
			return connectIterator->second.Session != nullptr
				&& connectIterator->second.Session->GetSessionState() == InnerNetworkSessionState::Registered;
		}

		const auto listenIterator = SessionsFromListen_.find(sessionId);
		if (listenIterator != SessionsFromListen_.end())
		{
			return listenIterator->second != nullptr
				&& listenIterator->second->GetSessionState() == InnerNetworkSessionState::Registered;
		}

		return false;
	}

	bool InnerNetwork::ActiveDisconnect(SessionId sessionId)
	{
		if (FindConnectSession(sessionId) != nullptr)
		{
			DestroyConnectSession(sessionId);
			return true;
		}

		if (FindListenSession(sessionId) != nullptr)
		{
			DestroyListenSession(sessionId);
			return true;
		}

		return false;
	}

	InnerNetworkSession* InnerNetwork::CreateConnectSession(const std::string& endpoint)
	{
		ActiveSessionEntry entry;
		entry.Session = std::make_unique<InnerNetworkSession>(endpoint);

		try
		{
			entry.Socket = std::make_unique<azmq::socket>(IOContext_, ZMQ_DEALER, true);
		}
		catch (const std::exception& exception)
		{
			Logger::Error("InnerNetwork", std::string("Failed to create active session for endpoint ") + endpoint + ": " + exception.what());
			return nullptr;
		}

		auto* session = entry.Session.get();
		SessionsFromConnect_.emplace(session->GetSessionId(), std::move(entry));
		Logger::Info("InnerNetwork", "Created active session for endpoint " + endpoint);
		return session;
	}

	InnerNetworkSession* InnerNetwork::CreateListenSession()
	{
		auto session = std::make_unique<InnerNetworkSession>();
		auto* sessionPtr = session.get();
		SessionsFromListen_.emplace(sessionPtr->GetSessionId(), std::move(session));
		Logger::Debug("InnerNetwork", "Created passive session.");
		return sessionPtr;
	}

	void InnerNetwork::DestroyConnectSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromConnect_.find(sessionId);
		if (iterator == SessionsFromConnect_.end())
		{
			return;
		}

		const auto remoteServerID = GetSessionServerID(sessionId);
		if (iterator->second.Socket != nullptr)
		{
			boost::system::error_code error;
			iterator->second.Socket->cancel(error);
			if (!iterator->second.Session->GetEndpoint().empty())
			{
				iterator->second.Socket->disconnect(iterator->second.Session->GetEndpoint(), error);
			}
		}

		RemoveSessionMapping(sessionId);
		SessionsFromConnect_.erase(iterator);

		if (!remoteServerID.empty() && !ShuttingDown_ && Callbacks_.OnDisconnect != nullptr)
		{
			Callbacks_.OnDisconnect(remoteServerID);
		}
	}

	void InnerNetwork::DestroyListenSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromListen_.find(sessionId);
		if (iterator == SessionsFromListen_.end())
		{
			return;
		}

		const auto remoteServerID = GetSessionServerID(sessionId);
		RemoveSessionMapping(sessionId);
		SessionsFromListen_.erase(iterator);

		if (!remoteServerID.empty() && !ShuttingDown_ && Callbacks_.OnDisconnect != nullptr)
		{
			Callbacks_.OnDisconnect(remoteServerID);
		}
	}

	void InnerNetwork::DestroySessions()
	{
		std::vector<SessionId> connectSessionIds;
		connectSessionIds.reserve(SessionsFromConnect_.size());
		for (const auto& [sessionId, entry] : SessionsFromConnect_)
		{
			(void)entry;
			connectSessionIds.push_back(sessionId);
		}

		for (const auto sessionId : connectSessionIds)
		{
			DestroyConnectSession(sessionId);
		}

		std::vector<SessionId> listenSessionIds;
		listenSessionIds.reserve(SessionsFromListen_.size());
		for (const auto& [sessionId, session] : SessionsFromListen_)
		{
			(void)session;
			listenSessionIds.push_back(sessionId);
		}

		for (const auto sessionId : listenSessionIds)
		{
			DestroyListenSession(sessionId);
		}
	}

	InnerNetwork::ActiveSessionEntry* InnerNetwork::FindConnectSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromConnect_.find(sessionId);
		return iterator == SessionsFromConnect_.end() ? nullptr : &iterator->second;
	}

	InnerNetworkSession* InnerNetwork::FindListenSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromListen_.find(sessionId);
		return iterator == SessionsFromListen_.end() ? nullptr : iterator->second.get();
	}

	InnerNetworkSession* InnerNetwork::ResolveListenSession(
		std::uint32_t messageID,
		const InnerNetworkSession::RoutingId& routingId,
		const std::vector<std::byte>& data
	)
	{
		if (messageID == static_cast<std::uint32_t>(MessageID::HandShakeReq))
		{
			std::string remoteServerID;
			if (!TryDeserializeHandShakePacket(data, remoteServerID))
			{
				Logger::Warn("InnerNetwork", "Received invalid handshake request on ROUTER socket.");
				return nullptr;
			}

			const auto expectedRoutingId = ServerIDToRoutingId(remoteServerID);
			if (routingId != expectedRoutingId)
			{
				Logger::Warn("InnerNetwork", "Handshake routing id does not match remote server id.");
				return nullptr;
			}

			const auto existingSessionIterator = ServerIDToSession_.find(remoteServerID);
			if (existingSessionIterator != ServerIDToSession_.end())
			{
				DestroyListenSession(existingSessionIterator->second);
			}

			auto* session = CreateListenSession();
			session->OnHandShakeReq(expectedRoutingId);
			RegisterSession(session, remoteServerID);
			return session;
		}

		const auto remoteServerID = BytesToString(routingId);
		const auto sessionIterator = ServerIDToSession_.find(remoteServerID);
		auto* session = sessionIterator == ServerIDToSession_.end() ? nullptr : FindListenSession(sessionIterator->second);
		if (session == nullptr || session->GetSessionState() != InnerNetworkSessionState::Registered)
		{
			Logger::Warn("InnerNetwork", "Received message from unregistered ROUTER routing id.");
			return nullptr;
		}

		return session;
	}

	void InnerNetwork::RegisterSession(InnerNetworkSession* session, const std::string& serverID)
	{
		if (session == nullptr || serverID.empty())
		{
			return;
		}

		const SessionId sessionId = session->GetSessionId();
		const auto oldServerIterator = SessionToServerID_.find(sessionId);
		if (oldServerIterator != SessionToServerID_.end())
		{
			if (oldServerIterator->second == serverID)
			{
				ServerIDToSession_[serverID] = sessionId;
				return;
			}

			ServerIDToSession_.erase(oldServerIterator->second);
		}

		const auto oldSessionIterator = ServerIDToSession_.find(serverID);
		if (oldSessionIterator != ServerIDToSession_.end() && oldSessionIterator->second != sessionId)
		{
			SessionToServerID_.erase(oldSessionIterator->second);
		}

		ServerIDToSession_[serverID] = sessionId;
		SessionToServerID_[sessionId] = serverID;
	}

	std::string InnerNetwork::GetSessionServerID(SessionId sessionId) const
	{
		const auto iterator = SessionToServerID_.find(sessionId);
		return iterator == SessionToServerID_.end() ? std::string{} : iterator->second;
	}

	void InnerNetwork::RemoveSessionMapping(SessionId sessionId)
	{
		const auto iterator = SessionToServerID_.find(sessionId);
		if (iterator == SessionToServerID_.end())
		{
			return;
		}

		const auto serverID = iterator->second;
		SessionToServerID_.erase(iterator);

		const auto serverIterator = ServerIDToSession_.find(serverID);
		if (serverIterator != ServerIDToSession_.end() && serverIterator->second == sessionId)
		{
			ServerIDToSession_.erase(serverIterator);
		}
	}

	void InnerNetwork::StartListenReceive()
	{
		if (ListenSocket_ == nullptr || ShuttingDown_)
		{
			return;
		}

		ListenSocket_->async_receive(
			[this](const boost::system::error_code& error, azmq::message& message, std::size_t bytesTransferred)
			{
				(void)bytesTransferred;
				HandleListenReceive(error, message);
			}
		);
	}

	void InnerNetwork::StartConnectReceive(SessionId sessionId)
	{
		auto* entry = FindConnectSession(sessionId);
		if (entry == nullptr || entry->Socket == nullptr || ShuttingDown_)
		{
			return;
		}

		entry->Socket->async_receive(
			[this, sessionId](const boost::system::error_code& error, azmq::message& message, std::size_t bytesTransferred)
			{
				(void)bytesTransferred;
				HandleConnectReceive(sessionId, error, message);
			}
		);
	}

	void InnerNetwork::HandleListenReceive(const boost::system::error_code& error, azmq::message& message)
	{
		if (error)
		{
			if (!ShuttingDown_ && error != asio::error::operation_aborted)
			{
				Logger::Warn("InnerNetwork", ZMQErrorMessage("Failed to receive from ROUTER socket", error));
			}

			return;
		}

		azmq::message_vector parts;
		if (!TryReceiveMultipart(*ListenSocket_, message, parts))
		{
			Logger::Warn("InnerNetwork", "Failed to read the remaining ROUTER message parts.");
			StartListenReceive();
			return;
		}

		std::vector<std::vector<std::byte>> prefixFrames;
		Header header;
		std::vector<std::byte> payload;
		if (!TryDeserializeStructuredFrame(parts, prefixFrames, header, payload))
		{
			Logger::Warn("InnerNetwork", "Received invalid ROUTER frame.");
			StartListenReceive();
			return;
		}

		if (prefixFrames.empty())
		{
			Logger::Warn("InnerNetwork", "Received ROUTER message without routing id.");
			StartListenReceive();
			return;
		}

		auto* session = ResolveListenSession(header.messageId, prefixFrames.front(), payload);
		if (session == nullptr)
		{
			StartListenReceive();
			return;
		}

		OnReceive(session->GetSessionId(), header.messageId, payload);
		StartListenReceive();
	}

	void InnerNetwork::HandleConnectReceive(SessionId sessionId, const boost::system::error_code& error, azmq::message& message)
	{
		auto* entry = FindConnectSession(sessionId);
		if (entry == nullptr)
		{
			return;
		}

		if (error)
		{
			if (!ShuttingDown_ && error != asio::error::operation_aborted)
			{
				Logger::Warn("InnerNetwork", ZMQErrorMessage("Failed to receive from DEALER socket", error));
			}

			DestroyConnectSession(sessionId);
			return;
		}

		azmq::message_vector parts;
		if (!TryReceiveMultipart(*entry->Socket, message, parts))
		{
			Logger::Warn("InnerNetwork", "Failed to read the remaining DEALER message parts.");
			DestroyConnectSession(sessionId);
			return;
		}

		std::vector<std::vector<std::byte>> prefixFrames;
		Header header;
		std::vector<std::byte> payload;
		if (!TryDeserializeStructuredFrame(parts, prefixFrames, header, payload))
		{
			Logger::Warn("InnerNetwork", "Received invalid DEALER frame.");
			DestroyConnectSession(sessionId);
			return;
		}

		OnReceive(sessionId, header.messageId, payload);
		StartConnectReceive(sessionId);
	}

	void InnerNetwork::OnReceive(SessionId sessionId, std::uint32_t messageID, const std::vector<std::byte>& data)
	{
		switch (static_cast<MessageID>(messageID))
		{
		case MessageID::HandShakeReq:
			HandleHandShakeReq(sessionId, data);
			return;

		case MessageID::HandShakeRsp:
			HandleHandShakeRsp(sessionId, data);
			return;

		default:
			HandlePayloadMessage(sessionId, messageID, data);
			return;
		}
	}

	void InnerNetwork::HandleHandShakeReq(SessionId sessionId, const std::vector<std::byte>& data)
	{
		auto* session = FindListenSession(sessionId);
		if (session == nullptr || session->GetSessionState() != InnerNetworkSessionState::Registered)
		{
			Logger::Warn("InnerNetwork", "Received handshake request for invalid passive session.");
			return;
		}

		std::string remoteServerID;
		if (!TryDeserializeHandShakePacket(data, remoteServerID))
		{
			Logger::Warn("InnerNetwork", "Received invalid handshake request payload.");
			return;
		}

		boost::system::error_code error;
		if (!SendFrame(
			*ListenSocket_,
			static_cast<std::uint32_t>(MessageID::HandShakeRsp),
			SerializeHandShakePacket(ServerID_),
			&session->GetRoutingId(),
			error
		))
		{
			Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to respond handshake to " + remoteServerID, error));
		}
	}

	void InnerNetwork::HandleHandShakeRsp(SessionId sessionId, const std::vector<std::byte>& data)
	{
		std::string remoteServerID;
		if (!TryDeserializeHandShakePacket(data, remoteServerID))
		{
			Logger::Warn("InnerNetwork", "Received invalid handshake response payload.");
			DestroyConnectSession(sessionId);
			return;
		}

		auto* entry = FindConnectSession(sessionId);
		if (entry == nullptr)
		{
			Logger::Warn("InnerNetwork", "Received handshake response for missing active session.");
			return;
		}

		entry->Session->OnHandShakeRsp();
		RegisterSession(entry->Session.get(), remoteServerID);
	}

	void InnerNetwork::HandlePayloadMessage(SessionId sessionId, std::uint32_t messageID, const std::vector<std::byte>& data)
	{
		const std::string serverID = GetSessionServerID(sessionId);
		if (serverID.empty())
		{
			Logger::Warn("InnerNetwork", "Received payload message from unknown session.");
			return;
		}

		if (Callbacks_.OnReceive)
		{
			Callbacks_.OnReceive(serverID, messageID, data);
		}
	}

}
