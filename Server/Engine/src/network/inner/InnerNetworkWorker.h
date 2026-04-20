#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

#include "network/inner/InnerNetworkSession.h"

namespace de::server::engine::network
{
	class InnerNetwork;

	class InnerNetworkWorker
	{
	public:
		using SessionId = InnerNetworkSession::SessionId;

		explicit InnerNetworkWorker(InnerNetwork& owner);
		~InnerNetworkWorker();
		InnerNetworkWorker(const InnerNetworkWorker&) = delete;
		InnerNetworkWorker& operator=(const InnerNetworkWorker&) = delete;
		InnerNetworkWorker(InnerNetworkWorker&&) = delete;
		InnerNetworkWorker& operator=(InnerNetworkWorker&&) = delete;

		bool Listen(const std::string& endpoint);
		InnerNetworkSession* ConnectTo(const std::string& endpoint);
		bool Send(const std::string& serverID, std::uint32_t messageID, const std::vector<std::byte>& data);
		bool ActiveDisconnect(SessionId sessionId);

	private:
		InnerNetworkSession* CreateConnectSession(const std::string& endpoint);
		InnerNetworkSession* CreateListenSession();
		void DestroyConnectSession(SessionId sessionId);
		void DestroyListenSession(SessionId sessionId);
		void DestroySessions(std::unordered_map<SessionId, InnerNetworkSession*>& sessions);
		InnerNetworkSession* FindConnectSession(SessionId sessionId);
		InnerNetworkSession* FindListenSession(SessionId sessionId);
		void RegisterSession(InnerNetworkSession* session, const std::string& serverID);
		std::string GetSessionServerID(const InnerNetworkSession* session) const;
		void RemoveSessionMapping(InnerNetworkSession* session);
		void StartWorker();
		void StopWorker();
		void RunWorker();
		void ProcessPendingTasks();
		void WakeWorker();
		void DrainWakeSocket();
		void PollListenSocket();
		void PollConnectSessions();
		void OnReceive(SessionId sessionId, std::uint32_t messageID, const std::vector<std::byte>& data);

		InnerNetwork& Owner_;
		void* ZMQContext_ = nullptr;
		void* ListenSocket_ = nullptr;
		void* WakeSendSocket_ = nullptr;
		void* WakeReceiveSocket_ = nullptr;
		std::thread WorkerThread_;
		std::thread::id WorkerThreadId_;
		std::mutex TaskMutex_;
		std::deque<std::function<void()>> PendingTasks_;
		std::atomic_bool StopRequested_{false};
		std::string WakeEndpoint_;
		bool Listening_ = false;
		std::unordered_map<SessionId, InnerNetworkSession*> SessionsFromListen;
		std::unordered_map<SessionId, InnerNetworkSession*> SessionsFromConnect;
		std::unordered_map<std::string, InnerNetworkSession*> ServerIDToSession;
		std::unordered_map<InnerNetworkSession*, std::string> SessionToServerID;
	};
}
