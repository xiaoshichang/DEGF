#pragma once

#include "config/ClusterConfig.h"
#include "managed/ManagedRuntimeBridge.h"

#include <cstddef>
#include <functional>
#include <string>
#include <vector>

namespace de::server::engine
{
	class ManagedRuntimeService
	{
	public:
		ManagedRuntimeService();
		~ManagedRuntimeService();

		void Start(std::string serverId, std::string configPath, const config::ManagedConfig& managedConfig);
		void Stop();
		bool IsRunning() const;
		bool TryBuildStubDistributePayload(const std::vector<std::string>& gameServerIds, std::vector<std::byte>& payload);
		bool HandleAllNodeReady(const std::vector<std::byte>& payload);
		void SetGameServerReadyCallback(std::function<void()> callback);

	private:
		static void DE_MANAGED_CALLTYPE NativeNotifyGameServerReady(void* context);
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

		void* hostfxrLibraryHandle_ = nullptr;
		managed::ManagedInitializeFn initializeFn_ = nullptr;
		managed::ManagedBuildStubDistributePayloadFn buildStubDistributePayloadFn_ = nullptr;
		managed::ManagedHandleAllNodeReadyFn handleAllNodeReadyFn_ = nullptr;
		managed::ManagedUninitializeFn uninitializeFn_ = nullptr;
		std::function<void()> gameServerReadyCallback_;
		bool running_ = false;
	};
}

