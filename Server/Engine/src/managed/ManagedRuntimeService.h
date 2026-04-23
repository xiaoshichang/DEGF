#pragma once

#include "config/ClusterConfig.h"
#include "managed/ManagedRuntimeBridge.h"

#include <string>

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

	private:
		void LoadHostFxr();
		void InitializeHostContext();
		void BindManagedEntrypoints();
		void Reset();

		std::string serverId_;
		std::string configPath_;
		std::string frameworkDllPath_;
		std::string gameplayDllPath_;
		std::string runtimeConfigPath_;

		void* hostfxrLibraryHandle_ = nullptr;
		managed::ManagedInitializeFn initializeFn_ = nullptr;
		managed::ManagedUninitializeFn uninitializeFn_ = nullptr;
		bool running_ = false;
	};
}

