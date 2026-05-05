#include "managed/ManagedRuntimeService.h"

#include <boost/json.hpp>

#include "core/Logger.h"

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

#include <chrono>
#include <cstring>
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
		constexpr const char_t* kHandleStubDistributeMethodName = DE_HOST_CHAR_LITERAL("HandleStubDistributeNative");
		constexpr const char_t* kValidateGateAuthMethodName = DE_HOST_CHAR_LITERAL("ValidateGateAuthNative");
		constexpr const char_t* kHandleAvatarLoginReqMethodName = DE_HOST_CHAR_LITERAL("HandleAvatarLoginReqNative");
		constexpr const char_t* kHandleCreateAvatarReqMethodName = DE_HOST_CHAR_LITERAL("HandleCreateAvatarReqNative");
		constexpr const char_t* kHandleCreateAvatarRspMethodName = DE_HOST_CHAR_LITERAL("HandleCreateAvatarRspNative");
		constexpr const char_t* kHandleClientAvatarRpcMethodName = DE_HOST_CHAR_LITERAL("HandleClientAvatarRpcNative");
		constexpr const char_t* kHandleServerAvatarRpcMethodName = DE_HOST_CHAR_LITERAL("HandleServerAvatarRpcNative");
		constexpr const char_t* kHandleServerRpcMethodName = DE_HOST_CHAR_LITERAL("HandleServerRpcNative");
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

		std::string SerializeGateAuthRequest(
			const std::string& account,
			const std::string& password,
			const std::vector<std::string>& gateServerIds
		)
		{
			boost::json::array gateServerIdArray;
			for (const auto& gateServerId : gateServerIds)
			{
				gateServerIdArray.emplace_back(gateServerId);
			}

			return boost::json::serialize(
				boost::json::object{
					{ "account", account },
					{ "password", password },
					{ "gateServerIds", std::move(gateServerIdArray) }
				}
			);
		}

		bool TryParseGateAuthValidationResultPayload(std::string_view payload, GateAuthValidationResult& result)
		{
			result = GateAuthValidationResult{};
			if (payload.empty())
			{
				return false;
			}

			boost::json::error_code error;
			const auto value = boost::json::parse(payload, error);
			if (error || !value.is_object())
			{
				return false;
			}

			const auto& object = value.as_object();
			if (const auto* successValue = object.if_contains("isSuccess");
				successValue != nullptr && successValue->is_bool())
			{
				result.IsSuccess = successValue->as_bool();
			}

			if (const auto* statusCodeValue = object.if_contains("statusCode");
				statusCodeValue != nullptr)
			{
				if (statusCodeValue->is_int64())
				{
					result.StatusCode = static_cast<int>(statusCodeValue->as_int64());
				}
				else if (statusCodeValue->is_uint64())
				{
					result.StatusCode = static_cast<int>(statusCodeValue->as_uint64());
				}
			}

			if (const auto* errorValue = object.if_contains("error");
				errorValue != nullptr && errorValue->is_string())
			{
				result.Error = boost::json::value_to<std::string>(*errorValue);
			}

			if (const auto* expectedServerIdValue = object.if_contains("expectedServerId");
				expectedServerIdValue != nullptr && expectedServerIdValue->is_string())
			{
				result.ExpectedServerId = boost::json::value_to<std::string>(*expectedServerIdValue);
			}

			if (result.IsSuccess && result.StatusCode == 0)
			{
				result.StatusCode = 200;
			}
			else if (!result.IsSuccess && result.StatusCode == 0)
			{
				result.StatusCode = 503;
			}

			return true;
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

	ManagedRuntimeService::ManagedRuntimeService(TimerManager& timerManager)
		: timerManager_(&timerManager)
	{
	}

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

	std::int32_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeSendCreateAvatarReq(
		void* context,
		const char* targetServerId,
		const std::uint8_t* avatarId
	)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr || targetServerId == nullptr || avatarId == nullptr || !service->createAvatarReqSender_)
		{
			return 0;
		}

		try
		{
			network::GuidBytes guidBytes;
			std::memcpy(guidBytes.bytes.data(), avatarId, guidBytes.bytes.size());
			return service->createAvatarReqSender_(targetServerId, guidBytes) ? 1 : 0;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendCreateAvatarReq failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendCreateAvatarReq failed with unknown exception.");
			return 0;
		}
	}

	std::int32_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeSendCreateAvatarRsp(
		void* context,
		const char* targetServerId,
		const std::uint8_t* avatarId,
		std::int32_t isSuccess,
		std::int32_t statusCode,
		const char* error,
		const void* avatarData,
		std::int32_t avatarDataSizeBytes
	)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr || targetServerId == nullptr || avatarId == nullptr || avatarDataSizeBytes < 0 || !service->createAvatarRspSender_)
		{
			return 0;
		}

		try
		{
			network::CreateAvatarRspMessage message;
			std::memcpy(message.avatarId.bytes.data(), avatarId, message.avatarId.bytes.size());
			message.isSuccess = isSuccess != 0;
			message.statusCode = statusCode;
			message.error = error == nullptr ? std::string{} : std::string(error);
			if (avatarDataSizeBytes > 0)
			{
				if (avatarData == nullptr)
				{
					return 0;
				}

				const auto* avatarDataBytes = static_cast<const std::byte*>(avatarData);
				message.avatarData.assign(avatarDataBytes, avatarDataBytes + avatarDataSizeBytes);
			}

			return service->createAvatarRspSender_(targetServerId, message) ? 1 : 0;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendCreateAvatarRsp failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendCreateAvatarRsp failed with unknown exception.");
			return 0;
		}
	}

	std::int32_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeSendAvatarLoginRsp(
		void* context,
		std::uint64_t clientSessionId,
		const std::uint8_t* avatarId,
		std::int32_t isSuccess,
		std::int32_t statusCode,
		const char* error,
		const void* avatarData,
		std::int32_t avatarDataSizeBytes
	)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr || avatarId == nullptr || avatarDataSizeBytes < 0 || !service->avatarLoginRspSender_)
		{
			return 0;
		}

		try
		{
			network::LoginRspMessage message;
			std::memcpy(message.avatarId.bytes.data(), avatarId, message.avatarId.bytes.size());
			message.isSuccess = isSuccess != 0;
			message.statusCode = statusCode;
			message.error = error == nullptr ? std::string{} : std::string(error);
			if (avatarDataSizeBytes > 0)
			{
				if (avatarData == nullptr)
				{
					return 0;
				}

				const auto* avatarDataBytes = static_cast<const std::byte*>(avatarData);
				message.avatarData.assign(avatarDataBytes, avatarDataBytes + avatarDataSizeBytes);
			}

			return service->avatarLoginRspSender_(clientSessionId, message) ? 1 : 0;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendAvatarLoginRsp failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendAvatarLoginRsp failed with unknown exception.");
			return 0;
		}
	}

	std::int32_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeSendAvatarRpcToServer(
		void* context,
		const char* targetServerId,
		const std::uint8_t* payload,
		std::int32_t payloadSizeBytes
	)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr || targetServerId == nullptr || payloadSizeBytes < 0 || !service->avatarRpcToServerSender_)
		{
			return 0;
		}

		try
		{
			std::vector<std::byte> bytes;
			if (payloadSizeBytes > 0)
			{
				if (payload == nullptr)
				{
					return 0;
				}

				const auto* begin = reinterpret_cast<const std::byte*>(payload);
				bytes.assign(begin, begin + payloadSizeBytes);
			}

			return service->avatarRpcToServerSender_(targetServerId, bytes) ? 1 : 0;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendAvatarRpcToServer failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendAvatarRpcToServer failed with unknown exception.");
			return 0;
		}
	}

	std::int32_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeSendAvatarRpcToClient(
		void* context,
		std::uint64_t clientSessionId,
		const std::uint8_t* payload,
		std::int32_t payloadSizeBytes
	)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr || payloadSizeBytes < 0 || !service->avatarRpcToClientSender_)
		{
			return 0;
		}

		try
		{
			std::vector<std::byte> bytes;
			if (payloadSizeBytes > 0)
			{
				if (payload == nullptr)
				{
					return 0;
				}

				const auto* begin = reinterpret_cast<const std::byte*>(payload);
				bytes.assign(begin, begin + payloadSizeBytes);
			}

			return service->avatarRpcToClientSender_(clientSessionId, bytes) ? 1 : 0;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendAvatarRpcToClient failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendAvatarRpcToClient failed with unknown exception.");
			return 0;
		}
	}

	std::int32_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeSendServerRpcToServer(
		void* context,
		const char* targetServerId,
		const std::uint8_t* payload,
		std::int32_t payloadSizeBytes
	)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr || targetServerId == nullptr || payloadSizeBytes < 0 || !service->serverRpcToServerSender_)
		{
			return 0;
		}

		try
		{
			std::vector<std::byte> bytes;
			if (payloadSizeBytes > 0)
			{
				if (payload == nullptr)
				{
					return 0;
				}

				const auto* begin = reinterpret_cast<const std::byte*>(payload);
				bytes.assign(begin, begin + payloadSizeBytes);
			}

			return service->serverRpcToServerSender_(targetServerId, bytes) ? 1 : 0;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendServerRpcToServer failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeSendServerRpcToServer failed with unknown exception.");
			return 0;
		}
	}

	std::uint64_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeAddTimer(
		void* context,
		std::int64_t delayMilliseconds,
		std::int32_t repeat,
		managed::NativeManagedTimerCallbackFn callback,
		void* state
	)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr)
		{
			return 0;
		}

		try
		{
			return service->AddManagedTimer(
				std::chrono::milliseconds(delayMilliseconds),
				repeat != 0,
				callback,
				state
			);
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeAddTimer failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeAddTimer failed with unknown exception.");
			return 0;
		}
	}

	std::int32_t DE_MANAGED_CALLTYPE ManagedRuntimeService::NativeCancelTimer(void* context, std::uint64_t timerId)
	{
		auto* service = static_cast<ManagedRuntimeService*>(context);
		if (service == nullptr)
		{
			return 0;
		}

		try
		{
			return service->CancelManagedTimer(timerId) ? 1 : 0;
		}
		catch (const std::exception& exception)
		{
			Logger::Error("ManagedRuntimeService", "NativeCancelTimer failed: " + std::string(exception.what()));
			return 0;
		}
		catch (...)
		{
			Logger::Error("ManagedRuntimeService", "NativeCancelTimer failed with unknown exception.");
			return 0;
		}
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
				&ManagedRuntimeService::NativeNotifyGameServerReady,
				&ManagedRuntimeService::NativeSendCreateAvatarReq,
				&ManagedRuntimeService::NativeSendCreateAvatarRsp,
				&ManagedRuntimeService::NativeSendAvatarLoginRsp,
				&ManagedRuntimeService::NativeSendAvatarRpcToServer,
				&ManagedRuntimeService::NativeSendAvatarRpcToClient,
				&ManagedRuntimeService::NativeSendServerRpcToServer,
				&ManagedRuntimeService::NativeAddTimer,
				&ManagedRuntimeService::NativeCancelTimer
			}
		};

		running_ = true;
		const int result = initializeFn_(&initInfo, static_cast<std::int32_t>(sizeof(initInfo)));
		if (result != 0)
		{
			Reset();
			throw std::runtime_error("Managed runtime initialize failed with code: " + std::to_string(result));
		}

		Logger::Info("ManagedRuntimeService", "Managed runtime initialized for " + serverId_);
	}

	void ManagedRuntimeService::Stop()
	{
		if (!running_ && hostfxrLibraryHandle_ == nullptr)
		{
			return;
		}

		CancelAllManagedTimers();

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

	bool ManagedRuntimeService::TryValidateGateAuth(
		const std::string& account,
		const std::string& password,
		const std::vector<std::string>& gateServerIds,
		GateAuthValidationResult& result
	)
	{
		result = GateAuthValidationResult{};
		if (!running_ || validateGateAuthFn_ == nullptr)
		{
			return false;
		}

		const auto requestPayload = SerializeGateAuthRequest(account, password, gateServerIds);
		const auto* requestPayloadData = requestPayload.empty() ? nullptr : requestPayload.data();
		const auto requestPayloadSize = static_cast<std::int32_t>(requestPayload.size());

		const int requiredSize = validateGateAuthFn_(requestPayloadData, requestPayloadSize, nullptr, 0);
		if (requiredSize <= 0)
		{
			Logger::Warn(
				"ManagedRuntimeService",
				"ValidateGateAuthNative failed to query output size, code: " + std::to_string(requiredSize)
			);
			return false;
		}

		std::string responsePayload(static_cast<std::size_t>(requiredSize), '\0');
		const int writtenSize = validateGateAuthFn_(
			requestPayloadData,
			requestPayloadSize,
			responsePayload.data(),
			requiredSize
		);
		if (writtenSize != requiredSize)
		{
			Logger::Warn(
				"ManagedRuntimeService",
				"ValidateGateAuthNative wrote unexpected size: " + std::to_string(writtenSize)
			);
			return false;
		}

		if (!TryParseGateAuthValidationResultPayload(responsePayload, result))
		{
			Logger::Warn("ManagedRuntimeService", "ValidateGateAuthNative returned invalid payload.");
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleStubDistribute(const std::vector<std::byte>& payload)
	{
		if (!running_ || handleStubDistributeFn_ == nullptr)
		{
			return false;
		}

		const int result = handleStubDistributeFn_(
			payload.empty() ? nullptr : payload.data(),
			static_cast<std::int32_t>(payload.size())
		);
		if (result != 0)
		{
			Logger::Warn("ManagedRuntimeService", "HandleStubDistributeNative failed with code: " + std::to_string(result));
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleAvatarLoginReq(std::uint64_t clientSessionId, const std::string& account)
	{
		if (!running_ || handleAvatarLoginReqFn_ == nullptr)
		{
			return false;
		}

		const int result = handleAvatarLoginReqFn_(clientSessionId, account.c_str());
		if (result != 0)
		{
			Logger::Warn("ManagedRuntimeService", "HandleAvatarLoginReqNative failed with code: " + std::to_string(result));
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleCreateAvatarReq(const std::string& sourceServerId, const network::GuidBytes& avatarId)
	{
		if (!running_ || handleCreateAvatarReqFn_ == nullptr)
		{
			return false;
		}

		const int result = handleCreateAvatarReqFn_(
			sourceServerId.c_str(),
			avatarId.bytes.data()
		);
		if (result != 0)
		{
			Logger::Warn("ManagedRuntimeService", "HandleCreateAvatarReqNative failed with code: " + std::to_string(result));
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleCreateAvatarRsp(
		const std::string& sourceServerId,
		const network::GuidBytes& avatarId,
		bool isSuccess,
		std::int32_t statusCode,
		const std::string& error,
		const std::vector<std::byte>& avatarData
	)
	{
		if (!running_ || handleCreateAvatarRspFn_ == nullptr)
		{
			return false;
		}

		const int result = handleCreateAvatarRspFn_(
			sourceServerId.c_str(),
			avatarId.bytes.data(),
			isSuccess ? 1 : 0,
			statusCode,
			error.c_str(),
			avatarData.empty() ? nullptr : avatarData.data(),
			static_cast<std::int32_t>(avatarData.size())
		);
		if (result != 0)
		{
			Logger::Warn("ManagedRuntimeService", "HandleCreateAvatarRspNative failed with code: " + std::to_string(result));
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleClientAvatarRpc(std::uint64_t clientSessionId, const std::vector<std::byte>& payload)
	{
		if (!running_ || handleClientAvatarRpcFn_ == nullptr)
		{
			return false;
		}

		const int result = handleClientAvatarRpcFn_(
			clientSessionId,
			payload.empty() ? nullptr : payload.data(),
			static_cast<std::int32_t>(payload.size())
		);
		if (result != 0)
		{
			Logger::Warn("ManagedRuntimeService", "HandleClientAvatarRpcNative failed with code: " + std::to_string(result));
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleServerAvatarRpc(const std::string& sourceServerId, const std::vector<std::byte>& payload)
	{
		if (!running_ || handleServerAvatarRpcFn_ == nullptr)
		{
			return false;
		}

		const int result = handleServerAvatarRpcFn_(
			sourceServerId.c_str(),
			payload.empty() ? nullptr : payload.data(),
			static_cast<std::int32_t>(payload.size())
		);
		if (result != 0)
		{
			Logger::Warn("ManagedRuntimeService", "HandleServerAvatarRpcNative failed with code: " + std::to_string(result));
			return false;
		}

		return true;
	}

	bool ManagedRuntimeService::HandleServerRpc(const std::string& sourceServerId, const std::vector<std::byte>& payload)
	{
		if (!running_ || handleServerRpcFn_ == nullptr)
		{
			return false;
		}

		const int result = handleServerRpcFn_(
			sourceServerId.c_str(),
			payload.empty() ? nullptr : payload.data(),
			static_cast<std::int32_t>(payload.size())
		);
		if (result != 0)
		{
			Logger::Warn("ManagedRuntimeService", "HandleServerRpcNative failed with code: " + std::to_string(result));
			return false;
		}

		return true;
	}

	void ManagedRuntimeService::SetGameServerReadyCallback(std::function<void()> callback)
	{
		gameServerReadyCallback_ = std::move(callback);
	}

	void ManagedRuntimeService::SetCreateAvatarReqSender(std::function<bool(const std::string&, const network::GuidBytes&)> sender)
	{
		createAvatarReqSender_ = std::move(sender);
	}

	void ManagedRuntimeService::SetCreateAvatarRspSender(
		std::function<bool(const std::string&, const network::CreateAvatarRspMessage&)> sender
	)
	{
		createAvatarRspSender_ = std::move(sender);
	}

	void ManagedRuntimeService::SetAvatarLoginRspSender(
		std::function<bool(std::uint64_t, const network::LoginRspMessage&)> sender
	)
	{
		avatarLoginRspSender_ = std::move(sender);
	}

	void ManagedRuntimeService::SetAvatarRpcToServerSender(std::function<bool(const std::string&, const std::vector<std::byte>&)> sender)
	{
		avatarRpcToServerSender_ = std::move(sender);
	}

	void ManagedRuntimeService::SetAvatarRpcToClientSender(std::function<bool(std::uint64_t, const std::vector<std::byte>&)> sender)
	{
		avatarRpcToClientSender_ = std::move(sender);
	}

	void ManagedRuntimeService::SetServerRpcToServerSender(std::function<bool(const std::string&, const std::vector<std::byte>&)> sender)
	{
		serverRpcToServerSender_ = std::move(sender);
	}

	std::uint64_t ManagedRuntimeService::AddManagedTimer(
		std::chrono::milliseconds delay,
		bool repeat,
		managed::NativeManagedTimerCallbackFn callback,
		void* state
	)
	{
		if (!running_)
		{
			throw std::runtime_error("Managed runtime is not running.");
		}

		if (timerManager_ == nullptr)
		{
			throw std::runtime_error("TimerManager is not available.");
		}

		if (callback == nullptr)
		{
			throw std::invalid_argument("Managed timer callback must not be null.");
		}

		const auto timerId = timerManager_->AddTimer(
			delay,
			[this, callback, state, repeat](TimerManager::TimerID timerId)
			{
				if (!repeat)
				{
					managedTimerIds_.erase(timerId);
				}

				if (!running_)
				{
					return;
				}

				callback(this, timerId, state);
			},
			repeat
		);

		managedTimerIds_.insert(timerId);
		return timerId;
	}

	bool ManagedRuntimeService::CancelManagedTimer(std::uint64_t timerId)
	{
		if (!running_ || timerManager_ == nullptr)
		{
			return false;
		}

		const auto iterator = managedTimerIds_.find(timerId);
		if (iterator == managedTimerIds_.end())
		{
			return false;
		}

		managedTimerIds_.erase(iterator);
		return timerManager_->CancelTimer(timerId);
	}

	void ManagedRuntimeService::CancelAllManagedTimers()
	{
		if (timerManager_ == nullptr || managedTimerIds_.empty())
		{
			return;
		}

		std::vector<TimerManager::TimerID> timerIds;
		timerIds.reserve(managedTimerIds_.size());
		for (const auto timerId : managedTimerIds_)
		{
			timerIds.push_back(timerId);
		}

		managedTimerIds_.clear();
		for (const auto timerId : timerIds)
		{
			timerManager_->CancelTimer(timerId);
		}
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

		void* handleStubDistributePointer = nullptr;
		bindMethod(kHandleStubDistributeMethodName, &handleStubDistributePointer);
		handleStubDistributeFn_ = reinterpret_cast<managed::ManagedHandleStubDistributeFn>(handleStubDistributePointer);

		void* validateGateAuthPointer = nullptr;
		bindMethod(kValidateGateAuthMethodName, &validateGateAuthPointer);
		validateGateAuthFn_ = reinterpret_cast<managed::ManagedValidateGateAuthFn>(validateGateAuthPointer);

		void* handleAvatarLoginReqPointer = nullptr;
		bindMethod(kHandleAvatarLoginReqMethodName, &handleAvatarLoginReqPointer);
		handleAvatarLoginReqFn_ = reinterpret_cast<managed::ManagedHandleAvatarLoginReqFn>(handleAvatarLoginReqPointer);

		void* handleCreateAvatarReqPointer = nullptr;
		bindMethod(kHandleCreateAvatarReqMethodName, &handleCreateAvatarReqPointer);
		handleCreateAvatarReqFn_ = reinterpret_cast<managed::ManagedHandleCreateAvatarReqFn>(handleCreateAvatarReqPointer);

		void* handleCreateAvatarRspPointer = nullptr;
		bindMethod(kHandleCreateAvatarRspMethodName, &handleCreateAvatarRspPointer);
		handleCreateAvatarRspFn_ = reinterpret_cast<managed::ManagedHandleCreateAvatarRspFn>(handleCreateAvatarRspPointer);

		void* handleClientAvatarRpcPointer = nullptr;
		bindMethod(kHandleClientAvatarRpcMethodName, &handleClientAvatarRpcPointer);
		handleClientAvatarRpcFn_ = reinterpret_cast<managed::ManagedHandleClientAvatarRpcFn>(handleClientAvatarRpcPointer);

		void* handleServerAvatarRpcPointer = nullptr;
		bindMethod(kHandleServerAvatarRpcMethodName, &handleServerAvatarRpcPointer);
		handleServerAvatarRpcFn_ = reinterpret_cast<managed::ManagedHandleServerAvatarRpcFn>(handleServerAvatarRpcPointer);

		void* handleServerRpcPointer = nullptr;
		bindMethod(kHandleServerRpcMethodName, &handleServerRpcPointer);
		handleServerRpcFn_ = reinterpret_cast<managed::ManagedHandleServerRpcFn>(handleServerRpcPointer);

		void* uninitializePointer = nullptr;
		bindMethod(kUninitializeMethodName, &uninitializePointer);
		uninitializeFn_ = reinterpret_cast<managed::ManagedUninitializeFn>(uninitializePointer);
	}

	void ManagedRuntimeService::BindManagedEntrypoints()
	{
		if (initializeFn_ == nullptr
			|| buildStubDistributePayloadFn_ == nullptr
			|| handleAllNodeReadyFn_ == nullptr
			|| handleStubDistributeFn_ == nullptr
			|| validateGateAuthFn_ == nullptr
			|| handleAvatarLoginReqFn_ == nullptr
			|| handleCreateAvatarReqFn_ == nullptr
			|| handleCreateAvatarRspFn_ == nullptr
			|| handleClientAvatarRpcFn_ == nullptr
			|| handleServerAvatarRpcFn_ == nullptr
			|| handleServerRpcFn_ == nullptr
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
		handleStubDistributeFn_ = nullptr;
		validateGateAuthFn_ = nullptr;
		handleAvatarLoginReqFn_ = nullptr;
		handleCreateAvatarReqFn_ = nullptr;
		handleCreateAvatarRspFn_ = nullptr;
		handleClientAvatarRpcFn_ = nullptr;
		handleServerAvatarRpcFn_ = nullptr;
		handleServerRpcFn_ = nullptr;
		uninitializeFn_ = nullptr;
		gameServerReadyCallback_ = {};
		createAvatarReqSender_ = {};
		createAvatarRspSender_ = {};
		avatarLoginRspSender_ = {};
		avatarRpcToServerSender_ = {};
		avatarRpcToClientSender_ = {};
		serverRpcToServerSender_ = {};
		managedTimerIds_.clear();
		CloseDynamicLibrary(hostfxrLibraryHandle_);
		hostfxrLibraryHandle_ = nullptr;
		serverId_.clear();
		configPath_.clear();
		frameworkDllPath_.clear();
		gameplayDllPath_.clear();
		runtimeConfigPath_.clear();
	}
}
