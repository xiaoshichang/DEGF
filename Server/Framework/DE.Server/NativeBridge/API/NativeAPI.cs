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
        public IntPtr AddTimer;
        public IntPtr CancelTimer;
    }



    public static class NativeAPI
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeNotifyGameServerReadyDelegate(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong NativeAddTimerDelegate(
            IntPtr context,
            long delayMilliseconds,
            int repeat,
            IntPtr callback,
            IntPtr state
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeCancelTimerDelegate(IntPtr context, ulong timerId);

        private static IntPtr s_context;
        private static NativeNotifyGameServerReadyDelegate s_notifyGameServerReady;
        private static NativeAddTimerDelegate s_addTimer;
        private static NativeCancelTimerDelegate s_cancelTimer;

        public static void Initialize(NativeApiTable nativeApi)
        {
            DETimer.Reset();
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

            if (nativeApi.AddTimer == IntPtr.Zero)
            {
                s_addTimer = null;
            }
            else
            {
                s_addTimer = Marshal.GetDelegateForFunctionPointer<NativeAddTimerDelegate>(nativeApi.AddTimer);
            }

            if (nativeApi.CancelTimer == IntPtr.Zero)
            {
                s_cancelTimer = null;
            }
            else
            {
                s_cancelTimer = Marshal.GetDelegateForFunctionPointer<NativeCancelTimerDelegate>(nativeApi.CancelTimer);
            }
        }

        public static void Reset()
        {
            DETimer.Reset();
            s_context = IntPtr.Zero;
            s_notifyGameServerReady = null;
            s_addTimer = null;
            s_cancelTimer = null;
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

        internal static ulong AddTimer(long delayMilliseconds, bool repeat, IntPtr callback, IntPtr state)
        {
            if (s_addTimer == null)
            {
                throw new InvalidOperationException("Native timer API is not available.");
            }

            return s_addTimer(s_context, delayMilliseconds, repeat ? 1 : 0, callback, state);
        }

        internal static bool CancelTimer(ulong timerId)
        {
            if (timerId == 0 || s_cancelTimer == null)
            {
                return false;
            }

            return s_cancelTimer(s_context, timerId) != 0;
        }
    }
}
