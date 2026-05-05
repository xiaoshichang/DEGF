#pragma once

#include <cstdint>

namespace de::server::engine::managed
{
#ifdef _WIN32
	#define DE_MANAGED_CALLTYPE __cdecl
#else
	#define DE_MANAGED_CALLTYPE
#endif

	enum class NativeLogLevel : std::int32_t
	{
		Debug = 0,
		Info = 1,
		Warn = 2,
		Error = 3
	};

	using NativeLogFn = void (DE_MANAGED_CALLTYPE*)(void* context, std::int32_t level, const char* tag, const char* message);
	using NativeNotifyGameServerReadyFn = void (DE_MANAGED_CALLTYPE*)(void* context);
	using NativeSendCreateAvatarReqFn = std::int32_t (DE_MANAGED_CALLTYPE*)(
		void* context,
		const char* targetServerId,
		const std::uint8_t* avatarId
	);
	using NativeSendCreateAvatarRspFn = std::int32_t (DE_MANAGED_CALLTYPE*)(
		void* context,
		const char* targetServerId,
		const std::uint8_t* avatarId,
		std::int32_t isSuccess,
		std::int32_t statusCode,
		const char* error,
		const void* avatarData,
		std::int32_t avatarDataSizeBytes
	);
	using NativeSendAvatarLoginRspFn = std::int32_t (DE_MANAGED_CALLTYPE*)(
		void* context,
		std::uint64_t clientSessionId,
		const std::uint8_t* avatarId,
		std::int32_t isSuccess,
		std::int32_t statusCode,
		const char* error,
		const void* avatarData,
		std::int32_t avatarDataSizeBytes
	);
	using NativeSendAvatarRpcToServerFn = std::int32_t (DE_MANAGED_CALLTYPE*)(
		void* context,
		const char* targetServerId,
		const std::uint8_t* payload,
		std::int32_t payloadSizeBytes
	);
	using NativeSendAvatarRpcToClientFn = std::int32_t (DE_MANAGED_CALLTYPE*)(
		void* context,
		std::uint64_t clientSessionId,
		const std::uint8_t* payload,
		std::int32_t payloadSizeBytes
	);
	using NativeSendServerRpcToServerFn = std::int32_t (DE_MANAGED_CALLTYPE*)(
		void* context,
		const char* targetServerId,
		const std::uint8_t* payload,
		std::int32_t payloadSizeBytes
	);
	using NativeManagedTimerCallbackFn = void (DE_MANAGED_CALLTYPE*)(void* context, std::uint64_t timerId, void* state);
	using NativeAddTimerFn = std::uint64_t (DE_MANAGED_CALLTYPE*)(
		void* context,
		std::int64_t delayMilliseconds,
		std::int32_t repeat,
		NativeManagedTimerCallbackFn callback,
		void* state
	);
	using NativeCancelTimerFn = std::int32_t (DE_MANAGED_CALLTYPE*)(void* context, std::uint64_t timerId);

	struct NativeApi
	{
		void* Context = nullptr;
		NativeLogFn Log = nullptr;
		NativeNotifyGameServerReadyFn NotifyGameServerReady = nullptr;
		NativeSendCreateAvatarReqFn SendCreateAvatarReq = nullptr;
		NativeSendCreateAvatarRspFn SendCreateAvatarRsp = nullptr;
		NativeSendAvatarLoginRspFn SendAvatarLoginRsp = nullptr;
		NativeSendAvatarRpcToServerFn SendAvatarRpcToServer = nullptr;
		NativeSendAvatarRpcToClientFn SendAvatarRpcToClient = nullptr;
		NativeSendServerRpcToServerFn SendServerRpcToServer = nullptr;
		NativeAddTimerFn AddTimer = nullptr;
		NativeCancelTimerFn CancelTimer = nullptr;
	};

	struct ManagedRuntimeInitInfo
	{
		const char* serverId = nullptr;
		const char* configPath = nullptr;
		const char* frameworkDllPath = nullptr;
		const char* gameplayDllPath = nullptr;
		NativeApi nativeApi{};
	};

	using ManagedInitializeFn = int (DE_MANAGED_CALLTYPE*)(const ManagedRuntimeInitInfo* initInfo, std::int32_t sizeBytes);
	using ManagedBuildStubDistributePayloadFn = int (DE_MANAGED_CALLTYPE*)(
		const void* inputPayload,
		std::int32_t inputSizeBytes,
		void* outputBuffer,
		std::int32_t outputBufferSizeBytes
	);
	using ManagedHandleAllNodeReadyFn = int (DE_MANAGED_CALLTYPE*)(const void* payload, std::int32_t sizeBytes);
	using ManagedHandleStubDistributeFn = int (DE_MANAGED_CALLTYPE*)(const void* payload, std::int32_t sizeBytes);
	using ManagedValidateGateAuthFn = int (DE_MANAGED_CALLTYPE*)(
		const void* inputPayload,
		std::int32_t inputSizeBytes,
		void* outputBuffer,
		std::int32_t outputBufferSizeBytes
	);
	using ManagedHandleAvatarLoginReqFn = int (DE_MANAGED_CALLTYPE*)(
		std::uint64_t clientSessionId,
		const char* account
	);
	using ManagedHandleCreateAvatarReqFn = int (DE_MANAGED_CALLTYPE*)(
		const char* sourceServerId,
		const std::uint8_t* avatarId
	);
	using ManagedHandleCreateAvatarRspFn = int (DE_MANAGED_CALLTYPE*)(
		const char* sourceServerId,
		const std::uint8_t* avatarId,
		std::int32_t isSuccess,
		std::int32_t statusCode,
		const char* error,
		const void* avatarData,
		std::int32_t avatarDataSizeBytes
	);
	using ManagedHandleClientAvatarRpcFn = int (DE_MANAGED_CALLTYPE*)(
		std::uint64_t clientSessionId,
		const void* payload,
		std::int32_t payloadSizeBytes
	);
	using ManagedHandleServerAvatarRpcFn = int (DE_MANAGED_CALLTYPE*)(
		const char* sourceServerId,
		const void* payload,
		std::int32_t payloadSizeBytes
	);
	using ManagedHandleServerRpcFn = int (DE_MANAGED_CALLTYPE*)(
		const char* sourceServerId,
		const void* payload,
		std::int32_t payloadSizeBytes
	);
	using ManagedBeginGmTotalEntityCountCommandFn = int (DE_MANAGED_CALLTYPE*)(
		std::uint64_t requestId,
		const void* gameServerIdsPayload,
		std::int32_t gameServerIdsPayloadSizeBytes
	);
	using ManagedCancelGmCommandFn = int (DE_MANAGED_CALLTYPE*)(std::uint64_t requestId);
	using ManagedBuildGmTotalEntityCountRspFn = int (DE_MANAGED_CALLTYPE*)(
		std::uint64_t requestId,
		void* outputBuffer,
		std::int32_t outputBufferSizeBytes
	);
	using ManagedHandleGmTotalEntityCountRspFn = int (DE_MANAGED_CALLTYPE*)(
		const char* sourceServerId,
		const void* payload,
		std::int32_t payloadSizeBytes,
		void* outputBuffer,
		std::int32_t outputBufferSizeBytes
	);
	using ManagedExecuteTelnetCSharpFn = int (DE_MANAGED_CALLTYPE*)(
		const char* code,
		void* outputBuffer,
		std::int32_t outputBufferSizeBytes
	);
	using ManagedUninitializeFn = int (DE_MANAGED_CALLTYPE*)(const void* payload, std::int32_t sizeBytes);
}

