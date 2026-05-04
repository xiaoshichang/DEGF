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
        public IntPtr SendCreateAvatarReq;
        public IntPtr SendCreateAvatarRsp;
        public IntPtr SendAvatarLoginRsp;
        public IntPtr SendAvatarRpcToGame;
        public IntPtr SendAvatarRpcToClient;
        public IntPtr AddTimer;
        public IntPtr CancelTimer;
    }



    public static class NativeAPI
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeNotifyGameServerReadyDelegate(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeSendCreateAvatarReqDelegate(
            IntPtr context,
            IntPtr targetServerId,
            IntPtr avatarId
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeSendCreateAvatarRspDelegate(
            IntPtr context,
            IntPtr targetServerId,
            IntPtr avatarId,
            int isSuccess,
            int statusCode,
            IntPtr error,
            IntPtr avatarData,
            int avatarDataSizeBytes
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeSendAvatarLoginRspDelegate(
            IntPtr context,
            ulong clientSessionId,
            IntPtr avatarId,
            int isSuccess,
            int statusCode,
            IntPtr error,
            IntPtr avatarData,
            int avatarDataSizeBytes
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeSendAvatarRpcToGameDelegate(
            IntPtr context,
            IntPtr targetServerId,
            IntPtr payload,
            int payloadSizeBytes
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeSendAvatarRpcToClientDelegate(
            IntPtr context,
            ulong clientSessionId,
            IntPtr payload,
            int payloadSizeBytes
        );

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
        private static NativeSendCreateAvatarReqDelegate s_sendCreateAvatarReq;
        private static NativeSendCreateAvatarRspDelegate s_sendCreateAvatarRsp;
        private static NativeSendAvatarLoginRspDelegate s_sendAvatarLoginRsp;
        private static NativeSendAvatarRpcToGameDelegate s_sendAvatarRpcToGame;
        private static NativeSendAvatarRpcToClientDelegate s_sendAvatarRpcToClient;
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

            if (nativeApi.SendCreateAvatarReq == IntPtr.Zero)
            {
                s_sendCreateAvatarReq = null;
            }
            else
            {
                s_sendCreateAvatarReq = Marshal.GetDelegateForFunctionPointer<NativeSendCreateAvatarReqDelegate>(
                    nativeApi.SendCreateAvatarReq
                );
            }

            if (nativeApi.SendCreateAvatarRsp == IntPtr.Zero)
            {
                s_sendCreateAvatarRsp = null;
            }
            else
            {
                s_sendCreateAvatarRsp = Marshal.GetDelegateForFunctionPointer<NativeSendCreateAvatarRspDelegate>(
                    nativeApi.SendCreateAvatarRsp
                );
            }

            if (nativeApi.SendAvatarLoginRsp == IntPtr.Zero)
            {
                s_sendAvatarLoginRsp = null;
            }
            else
            {
                s_sendAvatarLoginRsp = Marshal.GetDelegateForFunctionPointer<NativeSendAvatarLoginRspDelegate>(
                    nativeApi.SendAvatarLoginRsp
                );
            }

            if (nativeApi.SendAvatarRpcToGame == IntPtr.Zero)
            {
                s_sendAvatarRpcToGame = null;
            }
            else
            {
                s_sendAvatarRpcToGame = Marshal.GetDelegateForFunctionPointer<NativeSendAvatarRpcToGameDelegate>(
                    nativeApi.SendAvatarRpcToGame
                );
            }

            if (nativeApi.SendAvatarRpcToClient == IntPtr.Zero)
            {
                s_sendAvatarRpcToClient = null;
            }
            else
            {
                s_sendAvatarRpcToClient = Marshal.GetDelegateForFunctionPointer<NativeSendAvatarRpcToClientDelegate>(
                    nativeApi.SendAvatarRpcToClient
                );
            }
        }

        public static void Reset()
        {
            DETimer.Reset();
            s_context = IntPtr.Zero;
            s_notifyGameServerReady = null;
            s_sendCreateAvatarReq = null;
            s_sendCreateAvatarRsp = null;
            s_sendAvatarLoginRsp = null;
            s_sendAvatarRpcToGame = null;
            s_sendAvatarRpcToClient = null;
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

        public static bool SendCreateAvatarReq(string targetServerId, Guid avatarId)
        {
            if (s_sendCreateAvatarReq == null || string.IsNullOrWhiteSpace(targetServerId))
            {
                return false;
            }

            IntPtr targetServerIdPtr = IntPtr.Zero;
            IntPtr avatarIdPtr = IntPtr.Zero;
            try
            {
                targetServerIdPtr = Marshal.StringToCoTaskMemUTF8(targetServerId);
                avatarIdPtr = CopyGuidToNative(avatarId);
                return s_sendCreateAvatarReq(s_context, targetServerIdPtr, avatarIdPtr) != 0;
            }
            finally
            {
                FreeNative(targetServerIdPtr);
                FreeNative(avatarIdPtr);
            }
        }

        public static bool SendCreateAvatarRsp(string targetServerId, Guid avatarId, bool isSuccess, int statusCode, string error, byte[] avatarData)
        {
            if (s_sendCreateAvatarRsp == null || string.IsNullOrWhiteSpace(targetServerId))
            {
                return false;
            }

            IntPtr targetServerIdPtr = IntPtr.Zero;
            IntPtr avatarIdPtr = IntPtr.Zero;
            IntPtr errorPtr = IntPtr.Zero;
            IntPtr avatarDataPtr = IntPtr.Zero;
            try
            {
                targetServerIdPtr = Marshal.StringToCoTaskMemUTF8(targetServerId);
                avatarIdPtr = CopyGuidToNative(avatarId);
                errorPtr = Marshal.StringToCoTaskMemUTF8(error ?? string.Empty);
                avatarDataPtr = CopyPayloadToNative(avatarData);
                return s_sendCreateAvatarRsp(
                    s_context,
                    targetServerIdPtr,
                    avatarIdPtr,
                    isSuccess ? 1 : 0,
                    statusCode,
                    errorPtr,
                    avatarDataPtr,
                    avatarData == null ? 0 : avatarData.Length
                ) != 0;
            }
            finally
            {
                FreeNative(targetServerIdPtr);
                FreeNative(avatarIdPtr);
                FreeNative(errorPtr);
                FreeNative(avatarDataPtr);
            }
        }

        public static bool SendAvatarLoginRsp(ulong clientSessionId, Guid avatarId, bool isSuccess, int statusCode, string error, byte[] avatarData)
        {
            if (s_sendAvatarLoginRsp == null)
            {
                return false;
            }

            IntPtr avatarIdPtr = IntPtr.Zero;
            IntPtr errorPtr = IntPtr.Zero;
            IntPtr avatarDataPtr = IntPtr.Zero;
            try
            {
                avatarIdPtr = CopyGuidToNative(avatarId);
                errorPtr = Marshal.StringToCoTaskMemUTF8(error ?? string.Empty);
                avatarDataPtr = CopyPayloadToNative(avatarData);
                return s_sendAvatarLoginRsp(
                    s_context,
                    clientSessionId,
                    avatarIdPtr,
                    isSuccess ? 1 : 0,
                    statusCode,
                    errorPtr,
                    avatarDataPtr,
                    avatarData == null ? 0 : avatarData.Length
                ) != 0;
            }
            finally
            {
                FreeNative(avatarIdPtr);
                FreeNative(errorPtr);
                FreeNative(avatarDataPtr);
            }
        }

        public static bool SendAvatarRpcToGame(string targetServerId, byte[] payload)
        {
            if (s_sendAvatarRpcToGame == null || string.IsNullOrWhiteSpace(targetServerId))
            {
                return false;
            }

            IntPtr targetServerIdPtr = IntPtr.Zero;
            IntPtr payloadPtr = IntPtr.Zero;
            try
            {
                targetServerIdPtr = Marshal.StringToCoTaskMemUTF8(targetServerId);
                payloadPtr = CopyPayloadToNative(payload);
                return s_sendAvatarRpcToGame(
                    s_context,
                    targetServerIdPtr,
                    payloadPtr,
                    payload == null ? 0 : payload.Length
                ) != 0;
            }
            finally
            {
                FreeNative(targetServerIdPtr);
                FreeNative(payloadPtr);
            }
        }

        public static bool SendAvatarRpcToClient(ulong clientSessionId, byte[] payload)
        {
            if (s_sendAvatarRpcToClient == null)
            {
                return false;
            }

            IntPtr payloadPtr = IntPtr.Zero;
            try
            {
                payloadPtr = CopyPayloadToNative(payload);
                return s_sendAvatarRpcToClient(
                    s_context,
                    clientSessionId,
                    payloadPtr,
                    payload == null ? 0 : payload.Length
                ) != 0;
            }
            finally
            {
                FreeNative(payloadPtr);
            }
        }

        private static IntPtr CopyGuidToNative(Guid guid)
        {
            var bytes = guid.ToByteArray();
            var ptr = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }

        private static IntPtr CopyPayloadToNative(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return IntPtr.Zero;
            }

            var ptr = Marshal.AllocCoTaskMem(payload.Length);
            Marshal.Copy(payload, 0, ptr, payload.Length);
            return ptr;
        }

        private static void FreeNative(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(ptr);
            }
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
