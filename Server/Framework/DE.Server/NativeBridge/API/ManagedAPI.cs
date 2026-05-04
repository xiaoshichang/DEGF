using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DE.Server.Auth;

namespace DE.Server.NativeBridge
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ManagedRuntimeInitInfoNative
    {
        public IntPtr ServerId;
        public IntPtr ConfigPath;
        public IntPtr FrameworkDllPath;
        public IntPtr GameplayDllPath;
        public NativeApiTable NativeApi;
    }

    public struct ManagedRuntimeInitInfo
    {
        public string ServerId;
        public string ConfigPath;
        public string FrameworkDllPath;
        public string GameplayDllPath;
        public NativeApiTable NativeApi;
    }

    /// <summary>
    /// Managed API called from native code
    /// </summary>
    public static class ManagedAPI
    {
        private static readonly JsonSerializerOptions s_jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private static void _LogManagedEntryException(string operation, Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            Console.Error.WriteLine(exception);
            DELogger.Error("ManagedAPI", $"{operation} failed: {exception}");
        }

        private static byte[] _CopyPayloadFromNative(IntPtr payload, int sizeBytes)
        {
            if (sizeBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            }

            if (sizeBytes == 0)
            {
                return Array.Empty<byte>();
            }

            if (payload == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var managedPayload = new byte[sizeBytes];
            Marshal.Copy(payload, managedPayload, 0, sizeBytes);
            return managedPayload;
        }

        private static List<string> _ParseGameServerIds(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return new List<string>();
            }

            return Encoding.UTF8
                .GetString(payload)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(serverId => serverId.Trim())
                .Where(serverId => !string.IsNullOrWhiteSpace(serverId))
                .ToList();
        }

        private static Guid _ReadGuidFromNative(IntPtr value)
        {
            if (value == IntPtr.Zero)
            {
                return Guid.Empty;
            }

            var bytes = new byte[16];
            Marshal.Copy(value, bytes, 0, bytes.Length);
            return new Guid(bytes);
        }

        private static int _WritePayloadToNative(byte[] payload, IntPtr outputBuffer, int outputBufferSizeBytes)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (outputBuffer == IntPtr.Zero || outputBufferSizeBytes == 0)
            {
                return payload.Length;
            }

            if (outputBufferSizeBytes < payload.Length)
            {
                return -3;
            }

            if (payload.Length > 0)
            {
                Marshal.Copy(payload, 0, outputBuffer, payload.Length);
            }

            return payload.Length;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int InitializeNative(IntPtr initInfo, int sizeBytes)
        {
            try
            {
                if (initInfo == IntPtr.Zero || sizeBytes != Marshal.SizeOf<ManagedRuntimeInitInfoNative>())
                {
                    return -1;
                }

                var nativeInfo = Marshal.PtrToStructure<ManagedRuntimeInitInfoNative>(initInfo);
                var info = new ManagedRuntimeInitInfo
                {
                    ServerId = Marshal.PtrToStringUTF8(nativeInfo.ServerId) ?? string.Empty,
                    ConfigPath = Marshal.PtrToStringUTF8(nativeInfo.ConfigPath) ?? string.Empty,
                    FrameworkDllPath = Marshal.PtrToStringUTF8(nativeInfo.FrameworkDllPath) ?? string.Empty,
                    GameplayDllPath = Marshal.PtrToStringUTF8(nativeInfo.GameplayDllPath) ?? string.Empty,
                    NativeApi = nativeInfo.NativeApi,
                };

                NativeAPI.Initialize(info.NativeApi);
                
                ManagedRuntimeState.Initialize(info);
                DELogger.Info("ManagedAPI", $"Managed runtime initialized for {ManagedRuntimeState.RequireCurrent().ServerId}");
                return 0;
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(InitializeNative), exception);
                return -2;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int BuildStubDistributePayloadNative(IntPtr inputPayload, int inputSizeBytes, IntPtr outputBuffer, int outputBufferSizeBytes)
        {
            try
            {
                if (!ManagedRuntimeState.IsInitialized)
                {
                    return -1;
                }

                var runtimeState = ManagedRuntimeState.RequireCurrent();
                var allGameNode = _ParseGameServerIds(_CopyPayloadFromNative(inputPayload, inputSizeBytes));
                var table = ServerStubDistributeTable.BuildTable(runtimeState.StubTypes, allGameNode);
                var payload = ServerStubDistributeTable.ConvertServerStubDistributeTableToPayload(table);

                if (outputBuffer == IntPtr.Zero || outputBufferSizeBytes == 0)
                {
                    return payload.Length;
                }

                if (outputBufferSizeBytes < payload.Length)
                {
                    return -3;
                }

                if (payload.Length > 0)
                {
                    Marshal.Copy(payload, 0, outputBuffer, payload.Length);
                }

                DELogger.Info(
                    "ManagedAPI",
                    $"Built stub distribute payload for {allGameNode.Count} game node(s), payload={payload.Length} byte(s)."
                );
                return payload.Length;
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(BuildStubDistributePayloadNative), exception);
                return -2;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int HandleAllNodeReadyNative(IntPtr payload, int sizeBytes)
        {
            try
            {
                if (!ManagedRuntimeState.IsInitialized)
                {
                    return -1;
                }

                var managedPayload = _CopyPayloadFromNative(payload, sizeBytes);
                var table = ServerStubDistributeTable.ConvertServerStubDistributeTableFromPayload(managedPayload);
                ManagedRuntimeState.RequireCurrentGameServerRuntimeState().HandleAllNodeReady(table);
                return 0;
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(HandleAllNodeReadyNative), exception);
                return -2;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int ValidateGateAuthNative(IntPtr inputPayload, int inputSizeBytes, IntPtr outputBuffer, int outputBufferSizeBytes)
        {
            try
            {
                if (!ManagedRuntimeState.IsInitialized)
                {
                    return -1;
                }

                var requestPayload = _CopyPayloadFromNative(inputPayload, inputSizeBytes);
                var request = JsonSerializer.Deserialize<GateAuthValidationRequest>(requestPayload, s_jsonSerializerOptions);
                if (request == null)
                {
                    return -3;
                }

                var result = ManagedRuntimeState.RequireCurrentGateServerRuntimeState().ValidateAuth(request);
                var responsePayload = JsonSerializer.SerializeToUtf8Bytes(result, s_jsonSerializerOptions);
                return _WritePayloadToNative(responsePayload, outputBuffer, outputBufferSizeBytes);
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(ValidateGateAuthNative), exception);
                return -2;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int HandleAvatarLoginReqNative(
            ulong clientSessionId,
            IntPtr account
        )
        {
            try
            {
                if (!ManagedRuntimeState.IsInitialized)
                {
                    return -1;
                }

                var accountText = account == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUTF8(account) ?? string.Empty;
                return ManagedRuntimeState
                    .RequireCurrentGateServerRuntimeState()
                    .HandleAvatarLoginReq(clientSessionId, accountText) ? 0 : -3;
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(HandleAvatarLoginReqNative), exception);
                return -2;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int HandleCreateAvatarReqNative(
            IntPtr sourceServerId,
            IntPtr avatarId
        )
        {
            try
            {
                if (!ManagedRuntimeState.IsInitialized)
                {
                    return -1;
                }

                var sourceServerIdText = sourceServerId == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUTF8(sourceServerId) ?? string.Empty;
                var avatarGuid = _ReadGuidFromNative(avatarId);
                return ManagedRuntimeState
                    .RequireCurrentGameServerRuntimeState()
                    .HandleCreateAvatarReq(sourceServerIdText, avatarGuid) ? 0 : -3;
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(HandleCreateAvatarReqNative), exception);
                return -2;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int HandleCreateAvatarRspNative(
            IntPtr sourceServerId,
            IntPtr avatarId,
            int isSuccess,
            int statusCode,
            IntPtr error,
            IntPtr avatarData,
            int avatarDataSizeBytes
        )
        {
            try
            {
                if (!ManagedRuntimeState.IsInitialized)
                {
                    return -1;
                }

                var sourceServerIdText = sourceServerId == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUTF8(sourceServerId) ?? string.Empty;
                var avatarGuid = _ReadGuidFromNative(avatarId);
                var errorText = error == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUTF8(error) ?? string.Empty;
                var managedAvatarData = _CopyPayloadFromNative(avatarData, avatarDataSizeBytes);
                return ManagedRuntimeState
                    .RequireCurrentGateServerRuntimeState()
                    .HandleCreateAvatarRsp(sourceServerIdText, avatarGuid, isSuccess != 0, statusCode, errorText, managedAvatarData) ? 0 : -3;
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(HandleCreateAvatarRspNative), exception);
                return -2;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int UninitializeNative(IntPtr arg, int sizeBytes)
        {
            _ = arg;
            _ = sizeBytes;

            try
            {
                if (ManagedRuntimeState.IsInitialized)
                {
                    DELogger.Info("ManagedAPI", $"Managed runtime uninitializing for {ManagedRuntimeState.RequireCurrent().ServerId}");
                }

                ManagedRuntimeState.Uninitialize();
                NativeAPI.Reset();
                return 0;
            }
            catch (Exception exception)
            {
                _LogManagedEntryException(nameof(UninitializeNative), exception);
                return -1;
            }
        }
    }
}
