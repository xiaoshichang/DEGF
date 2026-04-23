using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

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

                Assembly gameplayAssembly = null;
                if (!string.IsNullOrEmpty(info.GameplayDllPath))
                {
                    var gameplayDllPath = Path.GetFullPath(info.GameplayDllPath);
                    if (File.Exists(gameplayDllPath))
                    {
                        gameplayAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(gameplayDllPath);
                    }
                    else
                    {
                        NativeAPI.Warn("ManagedAPI", $"Gameplay dll not found: {gameplayDllPath}");
                    }
                }

                ManagedRuntimeState.Initialize(info, gameplayAssembly);
                NativeAPI.Info("ManagedAPI", $"Managed runtime initialized for {ManagedRuntimeState.ServerId}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
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
                    NativeAPI.Info("ManagedAPI", $"Managed runtime uninitializing for {ManagedRuntimeState.ServerId}");
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
