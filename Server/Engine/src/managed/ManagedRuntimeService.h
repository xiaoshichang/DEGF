#pragma once

#include "config/ClusterConfig.h"
#include "core/BoostAsio.h"
#include "managed/ManagedRuntimeBridge.h"
#include "network/protocal/Message.h"
#include "server/gate/GateAuthValidationResult.h"
#include "timer/TimerManager.h"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace de::server::engine
{
	class ManagedRuntimeService
	{
	public:
		ManagedRuntimeService(asio::io_context& ioContext, TimerManager& timerManager);
		~ManagedRuntimeService();

		void Start(std::string serverId, std::string configPath, const config::ManagedConfig& managedConfig);
		void Stop();
		bool IsRunning() const;
		bool TryBuildStubDistributePayload(const std::vector<std::string>& gameServerIds, std::vector<std::byte>& payload);
		bool HandleAllNodeReady(const std::vector<std::byte>& payload);
		bool HandleStubDistribute(const std::vector<std::byte>& payload);
		bool BeginValidateGateAuth(
			const std::string& account,
			const std::string& password,
			const std::vector<std::string>& gateServerIds,
			std::function<void(GateAuthValidationResult)> callback
		);
		bool HandleAvatarLoginReq(std::uint64_t clientSessionId, const std::string& account);
		bool HandleCreateAvatarReq(const std::string& sourceServerId, const network::GuidBytes& avatarId, std::uint64_t clientSessionId);
		bool HandleCreateAvatarRsp(
			const std::string& sourceServerId,
			const network::GuidBytes& avatarId,
			std::uint64_t clientSessionId,
			bool isSuccess,
			std::int32_t statusCode,
			const std::string& error,
			const std::vector<std::byte>& avatarData
		);
		bool HandleClientAvatarRpc(std::uint64_t clientSessionId, const std::vector<std::byte>& payload);
		bool HandleClientDisconnect(std::uint64_t clientSessionId);
		bool HandleServerAvatarRpc(const std::string& sourceServerId, const std::vector<std::byte>& payload);
		bool HandleServerRpc(const std::string& sourceServerId, const std::vector<std::byte>& payload);
		bool BeginGmTotalEntityCountCommand(std::uint64_t requestId, const std::vector<std::string>& gameServerIds);
		bool CancelGmCommand(std::uint64_t requestId);
		bool BuildGmTotalEntityCountRsp(std::uint64_t requestId, std::vector<std::byte>& payload);
		bool HandleGmTotalEntityCountRsp(const std::string& sourceServerId, const std::vector<std::byte>& payload, std::string& telnetResponse);
		bool ExecuteTelnetCSharp(const std::string& code, std::string& response);
		void SetGameServerReadyCallback(std::function<void()> callback);
		void SetCreateAvatarReqSender(std::function<bool(const std::string&, const network::GuidBytes&, std::uint64_t)> sender);
		void SetCreateAvatarRspSender(std::function<bool(const std::string&, const network::CreateAvatarRspMessage&)> sender);
		void SetAvatarLoginRspSender(std::function<bool(std::uint64_t, const network::LoginRspMessage&)> sender);
		void SetActiveDisconnectClientSender(std::function<bool(std::uint64_t)> sender);
		void SetAvatarRpcToServerSender(std::function<bool(const std::string&, const std::vector<std::byte>&)> sender);
		void SetAvatarRpcToClientSender(std::function<bool(std::uint64_t, const std::vector<std::byte>&)> sender);
		void SetServerRpcToServerSender(std::function<bool(const std::string&, const std::vector<std::byte>&)> sender);

	private:
		static void DE_MANAGED_CALLTYPE NativeNotifyGameServerReady(void* context);
		static std::int32_t DE_MANAGED_CALLTYPE NativeSendCreateAvatarReq(
			void* context,
			const char* targetServerId,
			const std::uint8_t* avatarId,
			std::uint64_t clientSessionId
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeSendCreateAvatarRsp(
			void* context,
			const char* targetServerId,
			const std::uint8_t* avatarId,
			std::uint64_t clientSessionId,
			std::int32_t isSuccess,
			std::int32_t statusCode,
			const char* error,
			const void* avatarData,
			std::int32_t avatarDataSizeBytes
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeSendAvatarLoginRsp(
			void* context,
			std::uint64_t clientSessionId,
			const std::uint8_t* avatarId,
			std::int32_t isSuccess,
			std::int32_t statusCode,
			const char* error,
			const void* avatarData,
			std::int32_t avatarDataSizeBytes
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeActiveDisconnectClient(
			void* context,
			std::uint64_t clientSessionId
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeSendAvatarRpcToServer(
			void* context,
			const char* targetServerId,
			const std::uint8_t* payload,
			std::int32_t payloadSizeBytes
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeSendAvatarRpcToClient(
			void* context,
			std::uint64_t clientSessionId,
			const std::uint8_t* payload,
			std::int32_t payloadSizeBytes
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeSendServerRpcToServer(
			void* context,
			const char* targetServerId,
			const std::uint8_t* payload,
			std::int32_t payloadSizeBytes
		);
		static std::uint64_t DE_MANAGED_CALLTYPE NativeAddTimer(
			void* context,
			std::int64_t delayMilliseconds,
			std::int32_t repeat,
			managed::NativeManagedTimerCallbackFn callback,
			void* state
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeCancelTimer(void* context, std::uint64_t timerId);
		static std::int32_t DE_MANAGED_CALLTYPE NativePostToIoContext(
			void* context,
			managed::NativePostManagedCallbackFn callback,
			void* state
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeCompleteGateAuthValidation(
			void* context,
			std::uint64_t requestId,
			const void* payload,
			std::int32_t payloadSizeBytes
		);
		std::uint64_t AddManagedTimer(
			std::chrono::milliseconds delay,
			bool repeat,
			managed::NativeManagedTimerCallbackFn callback,
			void* state
		);
		bool CancelManagedTimer(std::uint64_t timerId);
		void CancelAllManagedTimers();
		void LoadHostFxr();
		void InitializeHostContext();
		void BindManagedEntrypoints();
		void OnManagedGameServerReady();
		void Reset();

		std::string serverId_;
		std::string configPath_;
		std::string frameworkDllPath_;
		std::string gameplayDllPath_;
		std::string runtimeConfigPath_;

		asio::io_context* ioContext_ = nullptr;
		TimerManager* timerManager_ = nullptr;
		void* hostfxrLibraryHandle_ = nullptr;
		managed::ManagedInitializeFn initializeFn_ = nullptr;
		managed::ManagedBuildStubDistributePayloadFn buildStubDistributePayloadFn_ = nullptr;
		managed::ManagedHandleAllNodeReadyFn handleAllNodeReadyFn_ = nullptr;
		managed::ManagedHandleStubDistributeFn handleStubDistributeFn_ = nullptr;
		managed::ManagedBeginValidateGateAuthFn beginValidateGateAuthFn_ = nullptr;
		managed::ManagedHandleAvatarLoginReqFn handleAvatarLoginReqFn_ = nullptr;
		managed::ManagedHandleCreateAvatarReqFn handleCreateAvatarReqFn_ = nullptr;
		managed::ManagedHandleCreateAvatarRspFn handleCreateAvatarRspFn_ = nullptr;
		managed::ManagedHandleClientAvatarRpcFn handleClientAvatarRpcFn_ = nullptr;
		managed::ManagedHandleClientDisconnectFn handleClientDisconnectFn_ = nullptr;
		managed::ManagedHandleServerAvatarRpcFn handleServerAvatarRpcFn_ = nullptr;
		managed::ManagedHandleServerRpcFn handleServerRpcFn_ = nullptr;
		managed::ManagedBeginGmTotalEntityCountCommandFn beginGmTotalEntityCountCommandFn_ = nullptr;
		managed::ManagedCancelGmCommandFn cancelGmCommandFn_ = nullptr;
		managed::ManagedBuildGmTotalEntityCountRspFn buildGmTotalEntityCountRspFn_ = nullptr;
		managed::ManagedHandleGmTotalEntityCountRspFn handleGmTotalEntityCountRspFn_ = nullptr;
		managed::ManagedExecuteTelnetCSharpFn executeTelnetCSharpFn_ = nullptr;
		managed::ManagedUninitializeFn uninitializeFn_ = nullptr;
		std::function<void()> gameServerReadyCallback_;
		std::function<bool(const std::string&, const network::GuidBytes&, std::uint64_t)> createAvatarReqSender_;
		std::function<bool(const std::string&, const network::CreateAvatarRspMessage&)> createAvatarRspSender_;
		std::function<bool(std::uint64_t, const network::LoginRspMessage&)> avatarLoginRspSender_;
		std::function<bool(std::uint64_t)> activeDisconnectClientSender_;
		std::function<bool(const std::string&, const std::vector<std::byte>&)> avatarRpcToServerSender_;
		std::function<bool(std::uint64_t, const std::vector<std::byte>&)> avatarRpcToClientSender_;
		std::function<bool(const std::string&, const std::vector<std::byte>&)> serverRpcToServerSender_;
		std::uint64_t nextGateAuthRequestId_ = 1;
		std::unordered_map<std::uint64_t, std::function<void(GateAuthValidationResult)>> gateAuthValidationCallbacks_;
		std::unordered_set<TimerManager::TimerID> managedTimerIds_;
		bool running_ = false;
	};
}

