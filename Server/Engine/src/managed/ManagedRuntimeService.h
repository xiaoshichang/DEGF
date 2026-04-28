#pragma once

#include "config/ClusterConfig.h"
#include "managed/ManagedRuntimeBridge.h"
#include "server/gate/GateAuthValidationResult.h"
#include "timer/TimerManager.h"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <string>
#include <unordered_set>
#include <vector>

namespace de::server::engine
{
	class ManagedRuntimeService
	{
	public:
		explicit ManagedRuntimeService(TimerManager& timerManager);
		~ManagedRuntimeService();

		void Start(std::string serverId, std::string configPath, const config::ManagedConfig& managedConfig);
		void Stop();
		bool IsRunning() const;
		bool TryBuildStubDistributePayload(const std::vector<std::string>& gameServerIds, std::vector<std::byte>& payload);
		bool HandleAllNodeReady(const std::vector<std::byte>& payload);
		bool TryValidateGateAuth(
			const std::string& account,
			const std::string& password,
			const std::vector<std::string>& gateServerIds,
			GateAuthValidationResult& result
		);
		void SetGameServerReadyCallback(std::function<void()> callback);

	private:
		static void DE_MANAGED_CALLTYPE NativeNotifyGameServerReady(void* context);
		static std::uint64_t DE_MANAGED_CALLTYPE NativeAddTimer(
			void* context,
			std::int64_t delayMilliseconds,
			std::int32_t repeat,
			managed::NativeManagedTimerCallbackFn callback,
			void* state
		);
		static std::int32_t DE_MANAGED_CALLTYPE NativeCancelTimer(void* context, std::uint64_t timerId);
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

		TimerManager* timerManager_ = nullptr;
		void* hostfxrLibraryHandle_ = nullptr;
		managed::ManagedInitializeFn initializeFn_ = nullptr;
		managed::ManagedBuildStubDistributePayloadFn buildStubDistributePayloadFn_ = nullptr;
		managed::ManagedHandleAllNodeReadyFn handleAllNodeReadyFn_ = nullptr;
		managed::ManagedValidateGateAuthFn validateGateAuthFn_ = nullptr;
		managed::ManagedUninitializeFn uninitializeFn_ = nullptr;
		std::function<void()> gameServerReadyCallback_;
		std::unordered_set<TimerManager::TimerID> managedTimerIds_;
		bool running_ = false;
	};
}

