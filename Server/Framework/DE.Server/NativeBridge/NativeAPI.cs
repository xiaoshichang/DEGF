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
        public IntPtr Log;
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
        private delegate void NativeLogDelegate(int level, IntPtr tagUtf8, IntPtr messageUtf8);

        private static NativeLogDelegate s_log;

        public static void Initialize(NativeApiTable nativeApi)
        {
            if (nativeApi.Log == IntPtr.Zero)
            {
                s_log = null;
                return;
            }

            s_log = Marshal.GetDelegateForFunctionPointer<NativeLogDelegate>(nativeApi.Log);
        }

        public static void Reset()
        {
            s_log = null;
        }

        public static void Debug(string tag, string message)
        {
            Log(NativeLogLevel.Debug, tag, message);
        }

        public static void Info(string tag, string message)
        {
            Log(NativeLogLevel.Info, tag, message);
        }

        public static void Warn(string tag, string message)
        {
            Log(NativeLogLevel.Warn, tag, message);
        }

        public static void Error(string tag, string message)
        {
            Log(NativeLogLevel.Error, tag, message);
        }

        private static void Log(NativeLogLevel level, string tag, string message)
        {
            if (s_log == null)
            {
                return;
            }

            var safeTag = tag ?? string.Empty;
            var safeMessage = message ?? string.Empty;
            var tagUtf8 = Marshal.StringToCoTaskMemUTF8(safeTag);
            var messageUtf8 = Marshal.StringToCoTaskMemUTF8(safeMessage);

            try
            {
                s_log((int)level, tagUtf8, messageUtf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(tagUtf8);
                Marshal.FreeCoTaskMem(messageUtf8);
            }
        }
    }
}
