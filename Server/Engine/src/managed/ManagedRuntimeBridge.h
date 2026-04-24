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

	struct NativeApi
	{
		void* Context = nullptr;
		NativeLogFn Log = nullptr;
		NativeNotifyGameServerReadyFn NotifyGameServerReady = nullptr;
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
	using ManagedUninitializeFn = int (DE_MANAGED_CALLTYPE*)(const void* payload, std::int32_t sizeBytes);
}

