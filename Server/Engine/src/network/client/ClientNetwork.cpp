#include "network/client/ClientNetwork.h"

#include "core/Logger.h"
#include "ikcp.h"
#include "network/protocal/Message.h"
#include "network/protocal/MessageID.h"
#include "network/protocal/Header.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

namespace de::server::engine::network
{
	namespace
	{
		int KcpOutput(const char* buffer, int length, ikcpcb* kcp, void* user)
		{
			(void)kcp;

			if (buffer == nullptr || length <= 0 || user == nullptr)
			{
				return -1;
			}

			auto* session = static_cast<ClientNetworkSession*>(user);
			auto* owner = session->GetOwner();
			if (owner == nullptr)
			{
				return -1;
			}

			return owner->SendRawPacket(*session, buffer, length);
		}

		std::vector<std::byte> ToByteVector(const void* data, std::size_t size)
		{
			const auto* begin = static_cast<const std::byte*>(data);
			return std::vector<std::byte>(begin, begin + size);
		}

		std::vector<std::byte> BuildClientFrame(std::uint32_t messageId, const std::vector<std::byte>& payload)
		{
			const auto header = Header::CreateClient(messageId, static_cast<std::uint32_t>(payload.size()));
			const auto serializedHeader = header.Serialize();

			std::vector<std::byte> frame(serializedHeader.size() + payload.size());
			std::memcpy(frame.data(), serializedHeader.data(), serializedHeader.size());
			if (!payload.empty())
			{
				std::memcpy(frame.data() + serializedHeader.size(), payload.data(), payload.size());
			}

			return frame;
		}
	}

	ClientNetwork::ClientNetwork(asio::io_context& ioContext, config::KcpConfig kcpConfig, ClientNetworkCallbacks callbacks)
		: ioContext_(ioContext)
		, kcpConfig_(std::move(kcpConfig))
		, callbacks_(std::move(callbacks))
		, socket_(ioContext)
		, updateTimer_(ioContext)
	{
	}

	ClientNetwork::~ClientNetwork()
	{
		shuttingDown_ = true;

		boost::system::error_code error;
		updateTimer_.cancel(error);
		socket_.cancel(error);
		socket_.close(error);

		DestroySessions();
	}

	bool ClientNetwork::Listen(const config::NetworkConfig& config)
	{
		if (listening_)
		{
			Logger::Warn("ClientNetwork", "Listen called more than once.");
			return false;
		}

		if (config.listenEndpoint.host.empty())
		{
			Logger::Warn("ClientNetwork", "Client network listen endpoint is invalid.");
			return false;
		}

		try
		{
			const auto address = asio::ip::make_address(config.listenEndpoint.host);
			const Endpoint endpoint(address, config.listenEndpoint.port);

			socket_.open(endpoint.protocol());
			socket_.set_option(asio::ip::udp::socket::reuse_address(true));
			socket_.bind(endpoint);

			listening_ = true;
			Logger::Info(
				"ClientNetwork",
				"Listening on " + config.listenEndpoint.host + ":" + std::to_string(socket_.local_endpoint().port())
			);

			StartReceive();
			StartUpdateTimer();
			return true;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ClientNetwork", std::string("Failed to listen client network: ") + exception.what());
			boost::system::error_code error;
			socket_.close(error);
			return false;
		}
	}

	bool ClientNetwork::IsListening() const
	{
		return listening_;
	}

	std::uint16_t ClientNetwork::GetListenPort() const
	{
		if (!socket_.is_open())
		{
			return 0;
		}

		boost::system::error_code error;
		const auto endpoint = socket_.local_endpoint(error);
		return error ? 0 : endpoint.port();
	}

	std::optional<AllocatedClientSession> ClientNetwork::AllocateSession()
	{
		const auto conv = AllocateConv();
		if (conv == 0)
		{
			return std::nullopt;
		}

		auto* session = CreateSession(conv);
		if (session == nullptr)
		{
			return std::nullopt;
		}

		return AllocatedClientSession{
			session->GetSessionId(),
			session->GetConv()
		};
	}

	bool ClientNetwork::Send(SessionId sessionId, std::uint32_t messageId, const std::vector<std::byte>& data)
	{
		auto* session = FindSession(sessionId);
		if (session == nullptr)
		{
			Logger::Warn("ClientNetwork", "Send target session not found.");
			return false;
		}

		if (session->GetSessionState() != ClientNetworkSessionState::Connected)
		{
			Logger::Warn("ClientNetwork", "Send target session is not connected yet.");
			return false;
		}

		return SendFrame(*session, messageId, data, true);
	}

	bool ClientNetwork::ActiveDisconnect(SessionId sessionId)
	{
		if (FindSession(sessionId) == nullptr)
		{
			return false;
		}

		DestroySession(sessionId);
		return true;
	}

	int ClientNetwork::SendRawPacket(const ClientNetworkSession& session, const char* data, int size)
	{
		if (!socket_.is_open() || data == nullptr || size <= 0)
		{
			return -1;
		}

		boost::system::error_code error;
		const auto sent = socket_.send_to(
			asio::buffer(data, static_cast<std::size_t>(size)),
			session.GetRemoteEndpoint(),
			0,
			error
		);
		if (error || sent != static_cast<std::size_t>(size))
		{
			Logger::Warn("ClientNetwork", "Failed to send UDP packet: " + error.message());
			return -1;
		}

		return static_cast<int>(sent);
	}

	ClientNetworkSession* ClientNetwork::CreateSession(std::uint32_t conv)
	{
		auto session = std::make_unique<ClientNetworkSession>(this, conv);
		auto* sessionPtr = session.get();
		ConfigureSession(*sessionPtr);
		sessions_.emplace(sessionPtr->GetSessionId(), std::move(session));
		convToSession_.emplace(conv, sessionPtr->GetSessionId());

		Logger::Info(
			"ClientNetwork",
			"Allocated KCP client session " + std::to_string(sessionPtr->GetSessionId()) + " conv=" + std::to_string(conv)
		);
		return sessionPtr;
	}

	ClientNetworkSession* ClientNetwork::FindSession(SessionId sessionId)
	{
		const auto iterator = sessions_.find(sessionId);
		return iterator == sessions_.end() ? nullptr : iterator->second.get();
	}

	ClientNetworkSession* ClientNetwork::FindSessionByConv(std::uint32_t conv)
	{
		const auto iterator = convToSession_.find(conv);
		if (iterator == convToSession_.end())
		{
			return nullptr;
		}

		return FindSession(iterator->second);
	}

	void ClientNetwork::DestroySession(SessionId sessionId)
	{
		const auto iterator = sessions_.find(sessionId);
		if (iterator == sessions_.end())
		{
			return;
		}

		convToSession_.erase(iterator->second->GetConv());
		sessions_.erase(iterator);

		if (!shuttingDown_ && callbacks_.OnDisconnect != nullptr)
		{
			callbacks_.OnDisconnect(sessionId);
		}
	}

	void ClientNetwork::DestroySessions()
	{
		std::vector<SessionId> sessionIds;
		sessionIds.reserve(sessions_.size());
		for (const auto& [sessionId, session] : sessions_)
		{
			(void)session;
			sessionIds.push_back(sessionId);
		}

		for (const auto sessionId : sessionIds)
		{
			DestroySession(sessionId);
		}
	}

	void ClientNetwork::StartReceive()
	{
		if (!listening_ || shuttingDown_ || !socket_.is_open())
		{
			return;
		}

		socket_.async_receive_from(
			asio::buffer(receiveBuffer_),
			receiveRemoteEndpoint_,
			[this](const boost::system::error_code& error, std::size_t bytesTransferred)
			{
				HandleReceive(error, bytesTransferred);
			}
		);
	}

	void ClientNetwork::HandleReceive(const boost::system::error_code& error, std::size_t bytesTransferred)
	{
		if (error)
		{
			if (!shuttingDown_ && error != asio::error::operation_aborted)
			{
				Logger::Warn("ClientNetwork", "UDP receive failed: " + error.message());
			}

			return;
		}

		if (bytesTransferred < sizeof(std::uint32_t))
		{
			Logger::Warn("ClientNetwork", "Received UDP packet that is too small for KCP.");
			StartReceive();
			return;
		}

		const std::uint32_t conv = ikcp_getconv(receiveBuffer_.data());
		if (conv == 0)
		{
			Logger::Warn("ClientNetwork", "Received UDP packet with invalid KCP conv.");
			StartReceive();
			return;
		}

		auto* session = FindSessionByConv(conv);
		if (session == nullptr)
		{
			Logger::Warn("ClientNetwork", "Received UDP packet for unknown conv.");
			StartReceive();
			return;
		}

		if (!session->BindRemoteEndpoint(receiveRemoteEndpoint_))
		{
			Logger::Warn("ClientNetwork", "Received UDP packet from unexpected remote endpoint.");
			StartReceive();
			return;
		}

		auto* kcp = session->GetKcp();
		if (kcp == nullptr)
		{
			Logger::Warn("ClientNetwork", "Client session has no KCP control block.");
			DestroySession(session->GetSessionId());
			StartReceive();
			return;
		}

		const int inputResult = ikcp_input(kcp, receiveBuffer_.data(), static_cast<long>(bytesTransferred));
		if (inputResult < 0)
		{
			Logger::Warn("ClientNetwork", "ikcp_input failed for session " + std::to_string(session->GetSessionId()) + ".");
			DestroySession(session->GetSessionId());
			StartReceive();
			return;
		}

		ikcp_update(kcp, GetCurrentMs());
		HandleKcpReceive(*session);
		StartReceive();
	}

	void ClientNetwork::HandleKcpReceive(ClientNetworkSession& session)
	{
		auto* kcp = session.GetKcp();
		if (kcp == nullptr)
		{
			return;
		}

		for (;;)
		{
			const int peekSize = ikcp_peeksize(kcp);
			if (peekSize < 0)
			{
				break;
			}

			std::vector<char> buffer(static_cast<std::size_t>(peekSize));
			const int receivedSize = ikcp_recv(kcp, buffer.data(), peekSize);
			if (receivedSize < 0)
			{
				Logger::Warn("ClientNetwork", "ikcp_recv failed for session " + std::to_string(session.GetSessionId()) + ".");
				return;
			}

			auto& sessionBuffer = session.GetReceiveBuffer();
			const auto originalSize = sessionBuffer.size();
			sessionBuffer.resize(originalSize + static_cast<std::size_t>(receivedSize));
			std::memcpy(sessionBuffer.data() + originalSize, buffer.data(), static_cast<std::size_t>(receivedSize));
			HandleDecodedFrames(session);
			if (FindSession(session.GetSessionId()) == nullptr)
			{
				return;
			}
		}
	}

	void ClientNetwork::HandleDecodedFrames(ClientNetworkSession& session)
	{
		auto& buffer = session.GetReceiveBuffer();
		while (buffer.size() >= Header::kWireSize)
		{
			const auto sessionId = session.GetSessionId();
			Header header;
			if (!Header::TryDeserialize(buffer.data(), buffer.size(), header))
			{
				Logger::Warn("ClientNetwork", "Received invalid client frame header.");
				DestroySession(session.GetSessionId());
				return;
			}

			const auto frameLength = static_cast<std::size_t>(header.GetFrameLength());
			if (buffer.size() < frameLength)
			{
				return;
			}

			std::vector<std::byte> payload;
			if (header.length > 0)
			{
				payload = ToByteVector(buffer.data() + header.headerLength, header.length);
			}

			buffer.erase(buffer.begin(), buffer.begin() + static_cast<std::ptrdiff_t>(frameLength));

			if (session.GetSessionState() != ClientNetworkSessionState::Connected)
			{
				if (header.messageId != static_cast<std::uint32_t>(MessageID::HandShakeReq))
				{
					Logger::Warn("ClientNetwork", "Received non-handshake payload before client session connected.");
					DestroySession(session.GetSessionId());
					return;
				}

				HandleHandShakeReq(session, payload);
				if (FindSession(sessionId) == nullptr)
				{
					return;
				}
			}
			else if (header.messageId == static_cast<std::uint32_t>(MessageID::HandShakeReq))
			{
				Logger::Warn("ClientNetwork", "Received duplicate client handshake request.");
			}
			else if (callbacks_.OnReceive != nullptr)
			{
				callbacks_.OnReceive(session.GetSessionId(), header.messageId, payload);
				if (FindSession(sessionId) == nullptr)
				{
					return;
				}
			}
		}
	}

	void ClientNetwork::HandleHandShakeReq(ClientNetworkSession& session, const std::vector<std::byte>& data)
	{
		network::ClientHandShakeMessage message;
		if (!network::ClientHandShakeMessage::TryDeserialize(data.data(), data.size(), message))
		{
			Logger::Warn("ClientNetwork", "Received invalid client handshake payload.");
			DestroySession(session.GetSessionId());
			return;
		}

		if (message.sessionId != session.GetSessionId())
		{
			Logger::Warn("ClientNetwork", "Received client handshake with mismatched session id.");
			DestroySession(session.GetSessionId());
			return;
		}

		if (!SendFrame(session, static_cast<std::uint32_t>(MessageID::HandShakeRsp), data, false))
		{
			Logger::Warn("ClientNetwork", "Failed to send client handshake response.");
			DestroySession(session.GetSessionId());
			return;
		}

		session.OnConnected();
		if (!shuttingDown_ && callbacks_.OnConnect != nullptr)
		{
			callbacks_.OnConnect(session.GetSessionId());
		}

		Logger::Info(
			"ClientNetwork",
			"Client session connected: sessionId=" + std::to_string(session.GetSessionId()) + ", conv=" + std::to_string(session.GetConv())
		);
	}

	bool ClientNetwork::SendFrame(ClientNetworkSession& session, std::uint32_t messageId, const std::vector<std::byte>& data, bool requireConnected)
	{
		if (requireConnected && session.GetSessionState() != ClientNetworkSessionState::Connected)
		{
			return false;
		}

		if (!session.HasRemoteEndpoint())
		{
			Logger::Warn("ClientNetwork", "Send target session has no bound remote endpoint.");
			return false;
		}

		auto frame = BuildClientFrame(messageId, data);
		auto* kcp = session.GetKcp();
		if (kcp == nullptr)
		{
			Logger::Warn("ClientNetwork", "Send target session has no KCP control block.");
			return false;
		}

		const int result = ikcp_send(kcp, reinterpret_cast<const char*>(frame.data()), static_cast<int>(frame.size()));
		if (result < 0)
		{
			Logger::Warn("ClientNetwork", "ikcp_send failed for session " + std::to_string(session.GetSessionId()) + ".");
			return false;
		}

		ikcp_update(kcp, GetCurrentMs());
		return true;
	}

	void ClientNetwork::StartUpdateTimer()
	{
		if (!listening_ || shuttingDown_)
		{
			return;
		}

		const auto intervalMs = std::max(1, kcpConfig_.intervalMs > 0 ? kcpConfig_.intervalMs : 10);
		updateTimer_.expires_after(std::chrono::milliseconds(intervalMs));
		updateTimer_.async_wait(
			[this](const boost::system::error_code& error)
			{
				HandleUpdateTimer(error);
			}
		);
	}

	void ClientNetwork::HandleUpdateTimer(const boost::system::error_code& error)
	{
		if (error)
		{
			return;
		}

		UpdateSessions();
		StartUpdateTimer();
	}

	void ClientNetwork::UpdateSessions()
	{
		const auto current = GetCurrentMs();
		for (const auto& [sessionId, session] : sessions_)
		{
			(void)sessionId;
			if (session != nullptr && session->GetKcp() != nullptr)
			{
				ikcp_update(session->GetKcp(), current);
			}
		}
	}

	void ClientNetwork::ConfigureSession(ClientNetworkSession& session)
	{
		auto* kcp = session.GetKcp();
		if (kcp == nullptr)
		{
			throw std::runtime_error("ConfigureSession requires a valid KCP control block.");
		}

		const int mtu = kcpConfig_.mtu > 0 ? kcpConfig_.mtu : 1400;
		const int sndwnd = kcpConfig_.sndwnd > 0 ? kcpConfig_.sndwnd : 32;
		const int rcvwnd = kcpConfig_.rcvwnd > 0 ? kcpConfig_.rcvwnd : 32;
		const int intervalMs = kcpConfig_.intervalMs > 0 ? kcpConfig_.intervalMs : 10;
		const int minRtoMs = std::max(0, kcpConfig_.minRtoMs);
		const int deadLinkCount = std::max(0, kcpConfig_.deadLinkCount);

		ikcp_setoutput(kcp, &KcpOutput);
		ikcp_setmtu(kcp, mtu);
		ikcp_wndsize(kcp, sndwnd, rcvwnd);
		ikcp_nodelay(
			kcp,
			kcpConfig_.nodelay ? 1 : 0,
			intervalMs,
			kcpConfig_.fastResend,
			kcpConfig_.noCongestionWindow ? 1 : 0
		);
		kcp->rx_minrto = static_cast<IUINT32>(minRtoMs);
		kcp->dead_link = static_cast<IUINT32>(deadLinkCount);
		kcp->stream = kcpConfig_.streamMode ? 1 : 0;
	}

	std::uint32_t ClientNetwork::AllocateConv()
	{
		for (std::uint32_t attempt = 0; attempt < std::numeric_limits<std::uint32_t>::max(); ++attempt)
		{
			const std::uint32_t conv = nextConv_++;
			if (conv == 0)
			{
				continue;
			}

			if (convToSession_.find(conv) == convToSession_.end())
			{
				return conv;
			}
		}

		return 0;
	}

	std::uint32_t ClientNetwork::GetCurrentMs()
	{
		using namespace std::chrono;
		const auto now = duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count();
		return static_cast<std::uint32_t>(std::min<std::uint64_t>(static_cast<std::uint64_t>(now), std::numeric_limits<std::uint32_t>::max()));
	}
}
