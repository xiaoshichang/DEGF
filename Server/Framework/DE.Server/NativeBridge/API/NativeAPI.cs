using System;
using System.Runtime.InteropServices;

namespace DE.Server.NativeBridge
{
    /// <summary>
    /// Native API called from managed code
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeApiTable
    {
        public IntPtr Context;
        public IntPtr Log;
        public IntPtr NotifyGameServerReady;
    }

    public enum NativeLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    public static class NativeAPI
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeNotifyGameServerReadyDelegate(IntPtr context);

        private static IntPtr s_context;
        private static NativeNotifyGameServerReadyDelegate s_notifyGameServerReady;

        public static void Initialize(NativeApiTable nativeApi)
        {
            s_context = nativeApi.Context;
            DELogger.Initialize(nativeApi.Context, nativeApi.Log);

            if (nativeApi.NotifyGameServerReady == IntPtr.Zero)
            {
                s_notifyGameServerReady = null;
            }
            else
            {
                s_notifyGameServerReady = Marshal.GetDelegateForFunctionPointer<NativeNotifyGameServerReadyDelegate>(
                    nativeApi.NotifyGameServerReady
                );
            }
        }

        public static void Reset()
        {
            s_context = IntPtr.Zero;
            s_notifyGameServerReady = null;
            DELogger.Reset();
        }

        public static void NotifyGameServerReady()
        {
            if (s_notifyGameServerReady == null)
            {
                return;
            }

            s_notifyGameServerReady(s_context);
        }
    }
}
