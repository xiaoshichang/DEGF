#include "managed/ManagedRuntimeService.h"

#include "core/Logger.h"

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

#include <filesystem>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#ifdef _WIN32
#include <Windows.h>
#else
#include <dlfcn.h>
#endif

namespace de::server::engine
{
	namespace
	{
		using HostfxrInitializeForRuntimeConfigFn = hostfxr_initialize_for_runtime_config_fn;
		using HostfxrGetRuntimeDelegateFn = hostfxr_get_runtime_delegate_fn;
		using HostfxrCloseFn = hostfxr_close_fn;
		using LoadAssemblyAndGetFunctionPointerFn = load_assembly_and_get_function_pointer_fn;

#ifdef _WIN32
		#define DE_HOST_CHAR_LITERAL(value) L##value
#else
		#define DE_HOST_CHAR_LITERAL(value) value
#endif

		constexpr const char_t* kManagedApiTypeName = DE_HOST_CHAR_LITERAL("DE.Server.NativeBridge.ManagedAPI, DE.Server");
		constexpr const char_t* kInitializeMethodName = DE_HOST_CHAR_LITERAL("InitializeNative");
		constexpr const char_t* kBuildStubDistributePayloadMethodName = DE_HOST_CHAR_LITERAL("BuildStubDistributePayloadNative");
		constexpr const char_t* kHandleAllNodeReadyMethodName = DE_HOST_CHAR_LITERAL("HandleAllNodeReadyNative");
		constexpr const char_t* kUninitializeMethodName = DE_HOST_CHAR_LITERAL("UninitializeNative");

		std::filesystem::path ResolvePathFromConfig(const std::string& configPath, const std::string& value)
		{
			const std::filesystem::path path(value);
			if (path.is_absolute())
			{
				return path.lexically_normal();
			}

			return (std::filesystem::path(configPath).parent_path() / path).lexically_normal();
		}

		std::filesystem::path BuildRuntimeConfigPath(const std::filesystem::path& frameworkDllPath)
		{
			auto runtimeConfigPath = frameworkDllPath;
			runtimeConfigPath.replace_extension(".runtimeconfig.json");
			return runtimeConfigPath;
		}

		std::basic_string<char_t> ToHostString(const std::filesystem::path& path)
		{
#ifdef _WIN32
			return path.native();
#else
			return path.string();
#endif
		}

		std::basic_string<char_t> ToHostString(const std::string& value)
		{
#ifdef _WIN32
			return std::filesystem::path(value).native();
#else
			return value;
#endif
		}

		void* LoadDynamicLibrary(const std::filesystem::path& path)
		{
#ifdef _WIN32
			return reinterpret_cast<void*>(::LoadLibraryW(path.c_str()));
#else
			return ::dlopen(path.c_str(), RTLD_LAZY | RTLD_LOCAL);
#endif
		}

		void* GetExport(void* libraryHandle, const char* symbolName)
		{
#ifdef _WIN32
			return reinterpret_cast<void*>(::GetProcAddress(reinterpret_cast<HMODULE>(libraryHandle), symbolName));
#else
			return ::dlsym(libraryHandle, symbolName);
#endif
		}

		void CloseDynamicLibrary(void* libraryHandle)
		{
			if (libraryHandle == nullptr)
			{
				return;
			}

#ifdef _WIN32
			::FreeLibrary(reinterpret_cast<HMODULE>(libraryHandle));
#else
			::dlclose(libraryHandle);
#endif
		}

		std::string SerializeGameServerIds(const std::vector<std::string>& gameServerIds)
		{
			std::string payload;
			for (const auto& gameServerId : gameServerIds)
			{
				if (gameServerId.empty())
				{
					continue;
				}

				if (!payload.empty())
				{
					payload.push_back('\n');
				}

				payload.append(gameServerId);
			}

			return payload;
		}

		void DE_MANAGED_CALLTYPE NativeLog(void* context, std::int32_t level, const char* tag, const char* message)
		{
			(void)context;
			const std::string_view safeTag = tag == nullptr ? std::string_view("ManagedRuntime") : std::string_view(tag);
			const std::string_view safeMessage = message == nullptr ? std::string_view{} : std::string_view(message);

			switch (static_cast<managed::NativeLogLevel>(level))
			{
			case managed::NativeLogLevel::Debug:
				Logger::Debug(safeTag, safeMessage);
				return;

			case managed::NativeLogLevel::Info:
				Logger::Info(safeTag, safeMessage);
				return;

			case managed::NativeLogLevel::Warn:
				Logger::Warn(safeTag, safeMessage);
				return;

			case managed::NativeLogLevel::Error:
				Logger::Error(safeTag, safeMessage);
				return;

			default:
				Logger::Warn("ManagedRuntime", "Received native log with unknown level.");
				Logger::Info(safeTag, safeMessage);
				return;
			}
		}
	}

	ManagedRuntimeService::ManagedRuntimeService() = default;

	ManagedRuntimeService::~ManagedRuntimeService()
	{
		Stop();
	}

	void DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeNotifyGameServerReady(void* context)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr)
		{
			return;
		}

		service->OnManagedGameServerReady();
	}

	void ManagedRuntimeService::Start(std::string serverId, std::string configPath, const config::ManagedConfig& managedConfig)
	{
		if (running_)
		{
			return;
		}

		serverId_ = std::move(serverId);
		configPath_ = std::move(configPath);
		frameworkDllPath_ = ResolvePathFromConfig(configPath_, managedConfig.frameworkDll).string();
		gameplayDllPath_ = ResolvePathFromConfig(configPath_, managedConfig.gameplayDll).string();
		runtimeConfigPath_ = BuildRuntimeConfigPath(frameworkDllPath_).string();

		if (frameworkDllPath_.empty())
		{
			throw std::runtime_error("ManagedRuntimeService requires managed.frameworkDll.");
		}

		if (!std::filesystem::exists(frameworkDllPath_))
		{
			throw std::runtime_error("Managed framework dll not found: " + frameworkDllPath_);
		}

		if (!gameplayDllPath_.empty() && !std::filesystem::exists(gameplayDllPath_))
		{
			throw std::runtime_error("Managed gameplay dll not found: " + gameplayDllPath_);
		}

		if (!std::filesystem::exists(runtimeConfigPath_))
		{
			throw std::runtime_error("Managed runtimeconfig json not found: " + runtimeConfigPath_);
		}

		LoadHostFxr();
		InitializeHostContext();
		BindManagedEntrypoints();

		const managed::ManagedRuntimeInitInfo initInfo{
			serverId_.c_str(),
			configPath_.c_str(),
			frameworkDllPath_.c_str(),
			gameplayDllPath_.c_str(),
			managed::NativeApi{
				this,
				&NativeLog,
				&ManagedRuntimeService::NativeNotifyGameServerReady
			}
		};

		const int result = initializeFn_(&initInfo, static_cast<std::int32_t>(sizeof(initInfo)));
		if (result != 0)
		{
			Reset();
			throw std::runtime_error("Managed runtime initialize failed with code: " + std::to_string(result));
		}

		running_ = true;
		Logger::Info("ManagedRuntimeService", "Managed runtime initialized for " + serverId_);
	}

	void ManagedRuntimeService::Stop()
	{
		if (!running_ && hostfxrLibraryHandle_ == nullptr)
		{
			return;
		}

		if (running_ && uninitializeFn_ != nullptr)
		{
			const int result = uninitializeFn_(nullptr, 0);
			if (result != 0)
			{
				Logger::Warn("ManagedRuntimeService", "Managed runtime uninitialize returned code: " + std::to_string(result));
			}
		}

		Reset();
	}

	bool ManagedRuntimeService::IsRunning() const
	{
		return running_;
	}

	bool ManagedRuntimeService::TryBuildStubDistributePayload(const std::vector<std::string>& gameServerIds, std::vector<std::byte>& payload)
	{
		payload.clear();
		if (!running_ || buildStubDistributePayloadFn_ == nullptr)
		{
			return false;
		}

		const auto serializedGameServerIds = SerializeGameServerIds(gameServerIds);
		const auto* inputPayload = serializedGameServerIds.empty() ? nullptr : serializedGameServerIds.data();
		const auto inputPayloadSize = static_cast<std::int32_t>(serializedGameServerIds.size());

		const int requiredSize = buildStubDistributePayloadFn_(inputPayload, inputPayloadSize, nullptr, 0);
		if (requiredSize < 0)
		{
			Logger::Warn(
				"ManagedRuntimeService",
				"BuildStubDistributePayloadNative failed with code: " + std::to_string(requiredSize)
			);
			return false;
		}

		if (requiredSize == 0)
		{
			return true;
		}

		payload.resize(static_cast<std::size_t>(requiredSize));
		const int writtenSize = buildStubDistributePayloadFn_(
			inputPayload,
			inputPayloadSize,
			payload.data(),
			requiredSize
		);
		if (writtenSize != requiredSize)
		{
			Logger::Warn(
				"ManagedRuntimeService",
				"BuildStubDistributePayloadNative wrote unexpected size: " + std::to_string(writtenSize)
			);
			payload.clear();
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleAllNodeReady(const std::vector<std::byte>& payload)
	{
		if (!running_ || handleAllNodeReadyFn_ == nullptr)
		{
			return false;
		}

		const auto* payloadData = payload.empty() ? nullptr : payload.data();
		const int result = handleAllNodeReadyFn_(payloadData, static_cast<std::int32_t>(payload.size()));
		if (result != 0)
		{
			Logger::Warn(
				"ManagedRuntimeService",
				"HandleAllNodeReadyNative failed with code: " + std::to_string(result)
			);
			return false;
		}

		return true;
	}

	void ManagedRuntimeService::SetGameServerReadyCallback(std::function<void()> callback)
	{
		gameServerReadyCallback_ = std::move(callback);
	}

	void ManagedRuntimeService::LoadHostFxr()
	{
		if (hostfxrLibraryHandle_ != nullptr)
		{
			return;
		}

		const auto frameworkDllPath = std::filesystem::path(frameworkDllPath_);
		const auto frameworkDllPathWide = ToHostString(frameworkDllPath);
		get_hostfxr_parameters parameters{};
		parameters.size = sizeof(parameters);
		parameters.assembly_path = frameworkDllPathWide.c_str();
		parameters.dotnet_root = nullptr;

		size_t bufferSize = 0;
		int result = get_hostfxr_path(nullptr, &bufferSize, &parameters);
		if (result != 0 && bufferSize == 0)
		{
			throw std::runtime_error("Failed to query hostfxr path buffer size: " + std::to_string(result));
		}

		std::vector<char_t> buffer(bufferSize);
		result = get_hostfxr_path(buffer.data(), &bufferSize, &parameters);
		if (result != 0)
		{
			throw std::runtime_error("Failed to get hostfxr path: " + std::to_string(result));
		}

		hostfxrLibraryHandle_ = LoadDynamicLibrary(std::filesystem::path(buffer.data()));
		if (hostfxrLibraryHandle_ == nullptr)
		{
			throw std::runtime_error("Failed to load hostfxr library.");
		}
	}

	void ManagedRuntimeService::InitializeHostContext()
	{
		const auto initializeForRuntimeConfig = reinterpret_cast<HostfxrInitializeForRuntimeConfigFn>(
			GetExport(hostfxrLibraryHandle_, "hostfxr_initialize_for_runtime_config")
		);
		const auto getRuntimeDelegate = reinterpret_cast<HostfxrGetRuntimeDelegateFn>(
			GetExport(hostfxrLibraryHandle_, "hostfxr_get_runtime_delegate")
		);
		const auto closeHostContext = reinterpret_cast<HostfxrCloseFn>(
			GetExport(hostfxrLibraryHandle_, "hostfxr_close")
		);
		if (initializeForRuntimeConfig == nullptr || getRuntimeDelegate == nullptr || closeHostContext == nullptr)
		{
			throw std::runtime_error("Failed to resolve required hostfxr exports.");
		}

		hostfxr_handle hostContext = nullptr;
		const auto runtimeConfigPathWide = ToHostString(runtimeConfigPath_);
		const int result = initializeForRuntimeConfig(runtimeConfigPathWide.c_str(), nullptr, &hostContext);
		if (result != 0 || hostContext == nullptr)
		{
			throw std::runtime_error("hostfxr_initialize_for_runtime_config failed with code: " + std::to_string(result));
		}

		void* delegate = nullptr;
		const int delegateResult = getRuntimeDelegate(
			hostContext,
			hdt_load_assembly_and_get_function_pointer,
			&delegate
		);
		closeHostContext(hostContext);
		if (delegateResult != 0 || delegate == nullptr)
		{
			throw std::runtime_error("hostfxr_get_runtime_delegate failed with code: " + std::to_string(delegateResult));
		}

		const auto loadAssemblyAndGetFunctionPointer = reinterpret_cast<LoadAssemblyAndGetFunctionPointerFn>(delegate);
		if (loadAssemblyAndGetFunctionPointer == nullptr)
		{
			throw std::runtime_error("Failed to acquire load_assembly_and_get_function_pointer delegate.");
		}

		auto bindMethod = [&](const char_t* methodName, void** functionPointer)
		{
			const auto frameworkDllPathWide = ToHostString(frameworkDllPath_);
			const int bindResult = loadAssemblyAndGetFunctionPointer(
				frameworkDllPathWide.c_str(),
				kManagedApiTypeName,
				methodName,
				UNMANAGEDCALLERSONLY_METHOD,
				nullptr,
				functionPointer
			);
			if (bindResult != 0 || *functionPointer == nullptr)
			{
				throw std::runtime_error("Failed to bind managed runtime entrypoint.");
			}
		};

		void* initializePointer = nullptr;
		bindMethod(kInitializeMethodName, &initializePointer);
		initializeFn_ = reinterpret_cast<managed::ManagedInitializeFn>(initializePointer);

		void* buildStubDistributePayloadPointer = nullptr;
		bindMethod(kBuildStubDistributePayloadMethodName, &buildStubDistributePayloadPointer);
		buildStubDistributePayloadFn_ = reinterpret_cast<managed::ManagedBuildStubDistributePayloadFn>(
			buildStubDistributePayloadPointer
		);

		void* handleAllNodeReadyPointer = nullptr;
		bindMethod(kHandleAllNodeReadyMethodName, &handleAllNodeReadyPointer);
		handleAllNodeReadyFn_ = reinterpret_cast<managed::ManagedHandleAllNodeReadyFn>(handleAllNodeReadyPointer);

		void* uninitializePointer = nullptr;
		bindMethod(kUninitializeMethodName, &uninitializePointer);
		uninitializeFn_ = reinterpret_cast<managed::ManagedUninitializeFn>(uninitializePointer);
	}

	void ManagedRuntimeService::BindManagedEntrypoints()
	{
		if (initializeFn_ == nullptr
			|| buildStubDistributePayloadFn_ == nullptr
			|| handleAllNodeReadyFn_ == nullptr
			|| uninitializeFn_ == nullptr)
		{
			throw std::runtime_error("Managed runtime entrypoints are not bound.");
		}
	}

	void ManagedRuntimeService::OnManagedGameServerReady()
	{
		if (gameServerReadyCallback_)
		{
			gameServerReadyCallback_();
		}
	}

	void ManagedRuntimeService::Reset()
	{
		running_ = false;
		initializeFn_ = nullptr;
		buildStubDistributePayloadFn_ = nullptr;
		handleAllNodeReadyFn_ = nullptr;
		uninitializeFn_ = nullptr;
		gameServerReadyCallback_ = {};
		CloseDynamicLibrary(hostfxrLibraryHandle_);
		hostfxrLibraryHandle_ = nullptr;
		serverId_.clear();
		configPath_.clear();
		frameworkDllPath_.clear();
		gameplayDllPath_.clear();
		runtimeConfigPath_.clear();
	}
}
