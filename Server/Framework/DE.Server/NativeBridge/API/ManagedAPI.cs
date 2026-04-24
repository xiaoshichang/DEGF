using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

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
                DELogger.Info("ManagedAPI", $"Managed runtime initialized for {ManagedRuntimeState.ServerId}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
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

                var allGameNode = _ParseGameServerIds(_CopyPayloadFromNative(inputPayload, inputSizeBytes));
                var table = ServerStubDistributeTable.BuildTable(ManagedRuntimeState.StubTypes, allGameNode);
                var payload = ServerStubDistributeTable.ConvertServerStubDistributeTableToPayload(table);
                ManagedRuntimeState.StubDistributeTable = table;

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
                Console.Error.WriteLine(exception);
                DELogger.Error("ManagedAPI", $"BuildStubDistributePayloadNative failed: {exception.Message}");
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
                GameServerRuntimeState.HandleAllNodeReady(table);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                DELogger.Error("ManagedAPI", $"HandleAllNodeReadyNative failed: {exception.Message}");
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
                    DELogger.Info("ManagedAPI", $"Managed runtime uninitializing for {ManagedRuntimeState.ServerId}");
                }

                ManagedRuntimeState.Uninitialize();
                NativeAPI.Reset();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return -1;
            }
        }
    }
}
