#include "network/inner/InnerNetworkWorker.h"

#include "core/Logger.h"
#include "network/inner/InnerNetwork.h"
#include "network/protocal/Header.h"
#include "network/protocal/MessageID.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstring>
#include <future>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <utility>
#include <zmq.h>

namespace de::server::engine::network
{
	namespace
	{
		constexpr auto kWorkerPollInterval = std::chrono::milliseconds(50);

		struct HandShakePacket
		{
			std::uint16_t ServerIDLength = 0;
		};

		std::string ZMQErrorMessage(const std::string& prefix)
		{
			const char* error = zmq_strerror(zmq_errno());
			return prefix + ": " + (error == nullptr ? "unknown error" : error);
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

		bool SendMessagePart(const void* socket, const void* data, std::size_t size, int flags)
		{
			const auto sentBytes = zmq_send(const_cast<void*>(socket), data, size, flags);
			return sentBytes == static_cast<int>(size);
		}

		bool SendFrame(
			void* socket,
			std::uint32_t messageId,
			const std::vector<std::byte>& payload,
			const std::vector<std::vector<std::byte>>& prefixFrames = {}
		)
		{
			if (socket == nullptr)
			{
				return false;
			}

			const auto header = Header::CreateInner(messageId, static_cast<std::uint32_t>(payload.size()));
			const auto serializedHeader = header.Serialize();

			for (std::size_t index = 0; index < prefixFrames.size(); ++index)
			{
				if (!SendMessagePart(socket, prefixFrames[index].data(), prefixFrames[index].size(), ZMQ_SNDMORE))
				{
					return false;
				}
			}

			if (!SendMessagePart(socket, serializedHeader.data(), serializedHeader.size(), ZMQ_SNDMORE))
			{
				return false;
			}

			return SendMessagePart(socket, payload.data(), payload.size(), 0);
		}

		bool ReceiveFramePart(void* socket, std::vector<std::byte>& output)
		{
			zmq_msg_t message;
			if (zmq_msg_init(&message) != 0)
			{
				return false;
			}

			const int receivedBytes = zmq_msg_recv(&message, socket, ZMQ_DONTWAIT);
			if (receivedBytes < 0)
			{
				zmq_msg_close(&message);
				return false;
			}

			output = ToByteVector(zmq_msg_data(&message), static_cast<std::size_t>(receivedBytes));
			zmq_msg_close(&message);
			return true;
		}

		bool ReceiveFramePart(void* socket, std::vector<std::byte>& output, bool& hasMore)
		{
			zmq_msg_t message;
			if (zmq_msg_init(&message) != 0)
			{
				return false;
			}

			const int receivedBytes = zmq_msg_recv(&message, socket, ZMQ_DONTWAIT);
			if (receivedBytes < 0)
			{
				zmq_msg_close(&message);
				return false;
			}

			output = ToByteVector(zmq_msg_data(&message), static_cast<std::size_t>(receivedBytes));
			hasMore = zmq_msg_more(&message) != 0;
			zmq_msg_close(&message);
			return true;
		}

		bool ReceiveStructuredFrame(
			void* socket,
			std::vector<std::vector<std::byte>>& prefixFrames,
			Header& header,
			std::vector<std::byte>& payload
		)
		{
			prefixFrames.clear();
			payload.clear();

			std::vector<std::byte> part;
			bool hasMore = false;
			if (!ReceiveFramePart(socket, part, hasMore))
			{
				return false;
			}

			while (hasMore)
			{
				Header maybeHeader;
				if (Header::TryDeserialize(part.data(), part.size(), maybeHeader))
				{
					header = maybeHeader;
					break;
				}

				prefixFrames.emplace_back(std::move(part));
				if (!ReceiveFramePart(socket, part, hasMore))
				{
					return false;
				}
			}

			if (!Header::TryDeserialize(part.data(), part.size(), header))
			{
				return false;
			}

			if (!hasMore)
			{
				payload.clear();
				return true;
			}

			if (!ReceiveFramePart(socket, payload, hasMore))
			{
				return false;
			}

			return !hasMore;
		}

		template<typename TResult>
		TResult ExecuteOnWorker(
			std::mutex& taskMutex,
			std::deque<std::function<void()>>& pendingTasks,
			const std::function<void()>& wakeWorker,
			std::thread::id workerThreadId,
			std::function<TResult()> task
		)
		{
			if (std::this_thread::get_id() == workerThreadId)
			{
				return task();
			}

			auto promise = std::make_shared<std::promise<TResult>>();
			auto future = promise->get_future();

			{
				std::lock_guard lock(taskMutex);
				pendingTasks.emplace_back(
					[task = std::move(task), promise]() mutable
					{
						try
						{
							promise->set_value(task());
						}
						catch (...)
						{
							promise->set_exception(std::current_exception());
						}
					}
				);
			}

			wakeWorker();
			return future.get();
		}

		template<>
		void ExecuteOnWorker<void>(
			std::mutex& taskMutex,
			std::deque<std::function<void()>>& pendingTasks,
			const std::function<void()>& wakeWorker,
			std::thread::id workerThreadId,
			std::function<void()> task
		)
		{
			if (std::this_thread::get_id() == workerThreadId)
			{
				task();
				return;
			}

			auto promise = std::make_shared<std::promise<void>>();
			auto future = promise->get_future();

			{
				std::lock_guard lock(taskMutex);
				pendingTasks.emplace_back(
					[task = std::move(task), promise]() mutable
					{
						try
						{
							task();
							promise->set_value();
						}
						catch (...)
						{
							promise->set_exception(std::current_exception());
						}
					}
				);
			}

			wakeWorker();
			future.get();
		}
	}

	InnerNetworkWorker::InnerNetworkWorker(InnerNetwork& owner)
		: Owner_(owner)
	{
		ZMQContext_ = zmq_ctx_new();
		if (ZMQContext_ == nullptr)
		{
			throw std::runtime_error("Failed to create zmq context for InnerNetworkWorker.");
		}

		const auto wakeIdentity = reinterpret_cast<std::uintptr_t>(this);
		WakeEndpoint_ = "inproc://inner-network-wake-" + std::to_string(wakeIdentity);
		StartWorker();
	}

	InnerNetworkWorker::~InnerNetworkWorker()
	{
		StopWorker();
		DestroySessions(SessionsFromListen);
		DestroySessions(SessionsFromConnect);

		if (ListenSocket_ != nullptr)
		{
			zmq_close(ListenSocket_);
			ListenSocket_ = nullptr;
		}

		if (ZMQContext_ != nullptr)
		{
			zmq_ctx_term(ZMQContext_);
			ZMQContext_ = nullptr;
		}
	}

	bool InnerNetworkWorker::Listen(const std::string& endpoint)
	{
		return ExecuteOnWorker<bool>(
			TaskMutex_,
			PendingTasks_,
			[this]() { WakeWorker(); },
			WorkerThreadId_,
			[this, endpoint]()
			{
				if (Listening_)
				{
					Logger::Warn("InnerNetwork", "Listen called more than once.");
					return false;
				}

				ListenSocket_ = zmq_socket(ZMQContext_, ZMQ_ROUTER);
				if (ListenSocket_ == nullptr)
				{
					Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to create ROUTER socket"));
					return false;
				}

				const int linger = 0;
				zmq_setsockopt(ListenSocket_, ZMQ_LINGER, &linger, sizeof(linger));
				if (zmq_bind(ListenSocket_, endpoint.c_str()) != 0)
				{
					Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to bind ROUTER socket to " + endpoint));
					zmq_close(ListenSocket_);
					ListenSocket_ = nullptr;
					return false;
				}

				Listening_ = true;
				Logger::Info("InnerNetwork", "Listening on " + endpoint);
				return true;
			}
		);
	}

	InnerNetworkSession* InnerNetworkWorker::ConnectTo(const std::string& endpoint)
	{
		return ExecuteOnWorker<InnerNetworkSession*>(
			TaskMutex_,
			PendingTasks_,
			[this]() { WakeWorker(); },
			WorkerThreadId_,
			[this, endpoint]()
			{
				auto* session = CreateConnectSession(endpoint);
				if (session == nullptr)
				{
					return static_cast<InnerNetworkSession*>(nullptr);
				}

				const SessionId sessionId = session->GetSessionId();
				const int linger = 0;
				zmq_setsockopt(session->GetZMQSocket(), ZMQ_LINGER, &linger, sizeof(linger));
				if (zmq_connect(session->GetZMQSocket(), endpoint.c_str()) != 0)
				{
					Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to connect DEALER socket to " + endpoint));
					DestroyConnectSession(sessionId);
					return static_cast<InnerNetworkSession*>(nullptr);
				}

				if (!SendFrame(session->GetZMQSocket(), static_cast<std::uint32_t>(MessageID::HandShakeReq), SerializeHandShakePacket(Owner_.ServerID_)))
				{
					Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to send handshake request to " + endpoint));
					DestroyConnectSession(sessionId);
					return static_cast<InnerNetworkSession*>(nullptr);
				}

				return session;
			}
		);
	}

	bool InnerNetworkWorker::Send(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data)
	{
		return ExecuteOnWorker<bool>(
			TaskMutex_,
			PendingTasks_,
			[this]() { WakeWorker(); },
			WorkerThreadId_,
			[this, serverID, messageID, data]()
			{
				const auto iterator = ServerIDToSession.find(serverID);
				if (iterator == ServerIDToSession.end())
				{
					Logger::Warn("InnerNetwork", "Send target not found: " + serverID);
					return false;
				}

				if (auto* session = iterator->second; session != nullptr && FindConnectSession(session->GetSessionId()) == session)
				{
					if (session->GetSessionState() != InnerNetworkSessionState::Registered)
					{
						Logger::Warn("InnerNetwork", "Send target is not registered yet: " + serverID);
						return false;
					}

					if (!SendFrame(session->GetZMQSocket(), messageID, data))
					{
						Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to send payload to connected session " + serverID));
						return false;
					}

					return true;
				}

				auto* listenSession = iterator->second;
				if (listenSession != nullptr && FindListenSession(listenSession->GetSessionId()) != listenSession)
				{
					listenSession = nullptr;
				}
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

				std::vector<std::vector<std::byte>> prefixFrames;
				prefixFrames.emplace_back(listenSession->GetRoutingId());
				if (!SendFrame(ListenSocket_, messageID, data, prefixFrames))
				{
					Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to send payload through ROUTER to " + serverID));
					return false;
				}

				return true;
			}
		);
	}

	bool InnerNetworkWorker::ActiveDisconnect(SessionId sessionId)
	{
		return ExecuteOnWorker<bool>(
			TaskMutex_,
			PendingTasks_,
			[this]() { WakeWorker(); },
			WorkerThreadId_,
			[this, sessionId]()
			{
				if (auto* session = FindConnectSession(sessionId))
				{
					(void)session;
					DestroyConnectSession(sessionId);
					return true;
				}

				auto* listenSession = FindListenSession(sessionId);
				if (listenSession == nullptr)
				{
					return false;
				}

				DestroyListenSession(sessionId);
				return true;
			}
		);
	}

	InnerNetworkSession* InnerNetworkWorker::CreateConnectSession(const std::string& endpoint)
	{
		const auto routingId = ServerIDToRoutingId(Owner_.ServerID_);
		InnerNetworkSession* session = nullptr;
		try
		{
			session = new InnerNetworkSession(ZMQContext_, routingId);
		}
		catch (const std::exception& exception)
		{
			Logger::Error("InnerNetwork", std::string("Failed to create active session for endpoint ") + endpoint + ": " + exception.what());
			return nullptr;
		}

		SessionsFromConnect.emplace(session->GetSessionId(), session);
		Logger::Info("InnerNetwork", "Created active session for endpoint " + endpoint);
		return session;
	}

	InnerNetworkSession* InnerNetworkWorker::CreateListenSession()
	{
		auto* session = new InnerNetworkSession();
		SessionsFromListen.emplace(session->GetSessionId(), session);
		Logger::Debug("InnerNetwork", "Created passive session.");
		return session;
	}

	void InnerNetworkWorker::DestroyConnectSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromConnect.find(sessionId);
		if (iterator == SessionsFromConnect.end())
		{
			return;
		}

		auto* session = iterator->second;
		const auto remoteServerID = GetSessionServerID(session);

		RemoveSessionMapping(session);
		delete session;
		SessionsFromConnect.erase(iterator);

		if (!remoteServerID.empty() && Owner_.Callbacks_.OnDisconnect != nullptr)
		{
			Owner_.PostDisconnectCallback(remoteServerID);
		}
	}

	void InnerNetworkWorker::DestroyListenSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromListen.find(sessionId);
		if (iterator == SessionsFromListen.end())
		{
			return;
		}

		auto* session = iterator->second;
		const auto remoteServerID = GetSessionServerID(session);

		RemoveSessionMapping(session);
		delete session;
		SessionsFromListen.erase(iterator);

		if (!remoteServerID.empty() && Owner_.Callbacks_.OnDisconnect != nullptr)
		{
			Owner_.PostDisconnectCallback(remoteServerID);
		}
	}

	void InnerNetworkWorker::DestroySessions(std::unordered_map<SessionId, InnerNetworkSession*>& sessions)
	{
		for (auto& [sessionId, session] : sessions)
		{
			(void)sessionId;
			RemoveSessionMapping(session);
			delete session;
		}

		sessions.clear();
	}

	InnerNetworkSession* InnerNetworkWorker::FindConnectSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromConnect.find(sessionId);
		return iterator == SessionsFromConnect.end() ? nullptr : iterator->second;
	}

	InnerNetworkSession* InnerNetworkWorker::FindListenSession(SessionId sessionId)
	{
		const auto iterator = SessionsFromListen.find(sessionId);
		return iterator == SessionsFromListen.end() ? nullptr : iterator->second;
	}

	void InnerNetworkWorker::RegisterSession(InnerNetworkSession* session, const std::string& serverID)
	{
		if (session == nullptr || serverID.empty())
		{
			return;
		}

		const auto oldServerIterator = SessionToServerID.find(session);
		if (oldServerIterator != SessionToServerID.end())
		{
			if (oldServerIterator->second == serverID)
			{
				ServerIDToSession[serverID] = session;
				return;
			}

			ServerIDToSession.erase(oldServerIterator->second);
		}

		const auto oldSessionIterator = ServerIDToSession.find(serverID);
		if (oldSessionIterator != ServerIDToSession.end() && oldSessionIterator->second != session)
		{
			SessionToServerID.erase(oldSessionIterator->second);
		}

		ServerIDToSession[serverID] = session;
		SessionToServerID[session] = serverID;
	}

	std::string InnerNetworkWorker::GetSessionServerID(const InnerNetworkSession* session) const
	{
		if (session == nullptr)
		{
			return {};
		}

		const auto iterator = SessionToServerID.find(const_cast<InnerNetworkSession*>(session));
		return iterator == SessionToServerID.end() ? std::string{} : iterator->second;
	}

	void InnerNetworkWorker::RemoveSessionMapping(InnerNetworkSession* session)
	{
		if (session == nullptr)
		{
			return;
		}

		const auto iterator = SessionToServerID.find(session);
		if (iterator == SessionToServerID.end())
		{
			return;
		}

		const auto serverID = iterator->second;
		SessionToServerID.erase(iterator);

		const auto serverIterator = ServerIDToSession.find(serverID);
		if (serverIterator != ServerIDToSession.end() && serverIterator->second == session)
		{
			ServerIDToSession.erase(serverIterator);
		}
	}

	void InnerNetworkWorker::PollListenSocket()
	{
		if (ListenSocket_ == nullptr)
		{
			return;
		}

		for (;;)
		{
			std::vector<std::vector<std::byte>> prefixFrames;
			Header header;
			std::vector<std::byte> payload;
			if (!ReceiveStructuredFrame(ListenSocket_, prefixFrames, header, payload))
			{
				if (zmq_errno() != EAGAIN)
				{
					Logger::Warn("InnerNetwork", ZMQErrorMessage("Failed to receive from ROUTER socket"));
				}

				break;
			}

			if (prefixFrames.empty())
			{
				Logger::Warn("InnerNetwork", "Received ROUTER message without routing id.");
				continue;
			}

			if (header.messageId == static_cast<std::uint32_t>(MessageID::HandShakeReq))
			{
				std::string remoteServerID;
				if (!TryDeserializeHandShakePacket(payload, remoteServerID))
				{
					Logger::Warn("InnerNetwork", "Received invalid handshake request on ROUTER socket.");
					continue;
				}

				const auto expectedRoutingId = ServerIDToRoutingId(remoteServerID);
				if (prefixFrames.front() != expectedRoutingId)
				{
					Logger::Warn("InnerNetwork", "Handshake routing id does not match remote server id.");
					continue;
				}

				const auto existingSessionIterator = ServerIDToSession.find(remoteServerID);
				if (existingSessionIterator != ServerIDToSession.end())
				{
					auto* existingSession = existingSessionIterator->second;
					if (existingSession != nullptr)
					{
						DestroyListenSession(existingSession->GetSessionId());
					}
				}

				auto* session = CreateListenSession();
				if (session == nullptr)
				{
					continue;
				}

				session->OnHandShakeReq(expectedRoutingId);
				RegisterSession(session, remoteServerID);
				std::vector<std::vector<std::byte>> responsePrefixFrames;
				responsePrefixFrames.emplace_back(session->GetRoutingId());
				if (!SendFrame(ListenSocket_, static_cast<std::uint32_t>(MessageID::HandShakeRsp), SerializeHandShakePacket(Owner_.ServerID_), responsePrefixFrames))
				{
					Logger::Error("InnerNetwork", ZMQErrorMessage("Failed to respond handshake to " + remoteServerID));
				}

				continue;
			}

			const auto remoteServerID = BytesToString(prefixFrames.front());
			const auto sessionIterator = ServerIDToSession.find(remoteServerID);
			auto* session = sessionIterator == ServerIDToSession.end() ? nullptr : sessionIterator->second;
			if (session == nullptr || session->GetSessionState() != InnerNetworkSessionState::Registered)
			{
				Logger::Warn("InnerNetwork", "Received message from unregistered ROUTER routing id.");
				continue;
			}

			OnReceive(session->GetSessionId(), header.messageId, payload);
		}
	}

	void InnerNetworkWorker::PollConnectSessions()
	{
		std::vector<SessionId> sessionsToDisconnect;

		for (const auto& [sessionId, session] : SessionsFromConnect)
		{
			for (;;)
			{
				std::vector<std::vector<std::byte>> prefixFrames;
				Header header;
				std::vector<std::byte> payload;
				if (!ReceiveStructuredFrame(session->GetZMQSocket(), prefixFrames, header, payload))
				{
					if (zmq_errno() != EAGAIN)
					{
						Logger::Warn("InnerNetwork", ZMQErrorMessage("Failed to receive from DEALER socket"));
						sessionsToDisconnect.push_back(sessionId);
					}

					break;
				}

				if (header.messageId == static_cast<std::uint32_t>(MessageID::HandShakeRsp))
				{
					std::string remoteServerID;
					if (!TryDeserializeHandShakePacket(payload, remoteServerID))
					{
						Logger::Warn("InnerNetwork", "Received invalid handshake response on DEALER socket.");
						break;
					}

					session->OnHandShakeRsp();
					RegisterSession(session, remoteServerID);
					break;
				}

				OnReceive(sessionId, header.messageId, payload);
			}
		}

		std::sort(sessionsToDisconnect.begin(), sessionsToDisconnect.end());
		sessionsToDisconnect.erase(std::unique(sessionsToDisconnect.begin(), sessionsToDisconnect.end()), sessionsToDisconnect.end());
		for (const auto sessionId : sessionsToDisconnect)
		{
			DestroyConnectSession(sessionId);
		}
	}

	void InnerNetworkWorker::StartWorker()
	{
		WakeReceiveSocket_ = zmq_socket(ZMQContext_, ZMQ_PAIR);
		WakeSendSocket_ = zmq_socket(ZMQContext_, ZMQ_PAIR);
		if (WakeReceiveSocket_ == nullptr || WakeSendSocket_ == nullptr)
		{
			throw std::runtime_error("Failed to create wake sockets for InnerNetwork worker.");
		}

		if (zmq_bind(WakeReceiveSocket_, WakeEndpoint_.c_str()) != 0)
		{
			throw std::runtime_error(ZMQErrorMessage("Failed to bind wake receive socket"));
		}

		if (zmq_connect(WakeSendSocket_, WakeEndpoint_.c_str()) != 0)
		{
			throw std::runtime_error(ZMQErrorMessage("Failed to connect wake send socket"));
		}

		WorkerThread_ = std::thread([this]() { RunWorker(); });
	}

	void InnerNetworkWorker::StopWorker()
	{
		if (!WorkerThread_.joinable())
		{
			return;
		}

		StopRequested_.store(true);
		WakeWorker();
		WorkerThread_.join();
		WorkerThreadId_ = std::thread::id{};

		if (WakeSendSocket_ != nullptr)
		{
			zmq_close(WakeSendSocket_);
			WakeSendSocket_ = nullptr;
		}

		if (WakeReceiveSocket_ != nullptr)
		{
			zmq_close(WakeReceiveSocket_);
			WakeReceiveSocket_ = nullptr;
		}
	}

	void InnerNetworkWorker::RunWorker()
	{
		WorkerThreadId_ = std::this_thread::get_id();

		while (true)
		{
			std::vector<zmq_pollitem_t> pollItems;
			pollItems.reserve(1 + (ListenSocket_ != nullptr ? 1u : 0u) + SessionsFromConnect.size());

			pollItems.push_back(zmq_pollitem_t{WakeReceiveSocket_, 0, ZMQ_POLLIN, 0});
			if (ListenSocket_ != nullptr)
			{
				pollItems.push_back(zmq_pollitem_t{ListenSocket_, 0, ZMQ_POLLIN, 0});
			}

			for (const auto& [sessionId, session] : SessionsFromConnect)
			{
				(void)sessionId;
				pollItems.push_back(zmq_pollitem_t{session->GetZMQSocket(), 0, ZMQ_POLLIN, 0});
			}

			const auto timeout = static_cast<long>(kWorkerPollInterval.count());
			const int rc = zmq_poll(pollItems.data(), static_cast<int>(pollItems.size()), timeout);
			if (rc < 0)
			{
				if (zmq_errno() == ETERM)
				{
					break;
				}

				Logger::Warn("InnerNetwork", ZMQErrorMessage("Worker poll failed"));
			}

			if (pollItems[0].revents & ZMQ_POLLIN)
			{
				DrainWakeSocket();
			}

			ProcessPendingTasks();

			if (StopRequested_.load())
			{
				break;
			}

			std::size_t pollIndex = 1;
			if (ListenSocket_ != nullptr)
			{
				if (pollItems[pollIndex].revents & ZMQ_POLLIN)
				{
					PollListenSocket();
				}

				++pollIndex;
			}

			bool shouldPollConnectSessions = false;
			for (; pollIndex < pollItems.size(); ++pollIndex)
			{
				if (pollItems[pollIndex].revents & ZMQ_POLLIN)
				{
					shouldPollConnectSessions = true;
					break;
				}
			}

			if (shouldPollConnectSessions)
			{
				PollConnectSessions();
			}
		}
	}

	void InnerNetworkWorker::ProcessPendingTasks()
	{
		std::deque<std::function<void()>> tasks;
		{
			std::lock_guard lock(TaskMutex_);
			tasks.swap(PendingTasks_);
		}

		for (auto& task : tasks)
		{
			task();
		}
	}

	void InnerNetworkWorker::WakeWorker()
	{
		if (WakeSendSocket_ == nullptr)
		{
			return;
		}

		const char wakeByte = 0;
		zmq_send(WakeSendSocket_, &wakeByte, 1, ZMQ_DONTWAIT);
	}

	void InnerNetworkWorker::DrainWakeSocket()
	{
		if (WakeReceiveSocket_ == nullptr)
		{
			return;
		}

		char buffer[32];
		while (zmq_recv(WakeReceiveSocket_, buffer, sizeof(buffer), ZMQ_DONTWAIT) >= 0)
		{
		}
	}

	void InnerNetworkWorker::OnReceive(SessionId sessionId, std::uint32_t messageID, const std::vector<std::byte>& data)
	{
		InnerNetworkSession* session = FindConnectSession(sessionId);
		if (session == nullptr)
		{
			session = FindListenSession(sessionId);
		}

		if (session == nullptr || session->GetSessionState() != InnerNetworkSessionState::Registered)
		{
			return;
		}

		const auto serverID = GetSessionServerID(session);
		if (serverID.empty())
		{
			return;
		}

		if (Owner_.Callbacks_.OnReceive != nullptr)
		{
			Owner_.PostReceiveCallback(serverID, messageID, data);
		}
	}
}
