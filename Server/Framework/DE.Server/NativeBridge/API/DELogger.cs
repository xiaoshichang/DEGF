using System;
using System.Runtime.InteropServices;

namespace DE.Server.NativeBridge
{
    public enum NativeLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }
    
    
    public static class DELogger
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeLogDelegate(IntPtr context, int level, IntPtr tagUtf8, IntPtr messageUtf8);

        private static IntPtr s_context;
        private static NativeLogDelegate s_log;

        public static void Initialize(IntPtr context, IntPtr nativeLog)
        {
            s_context = context;
            if (nativeLog == IntPtr.Zero)
            {
                s_log = null;
                return;
            }

            s_log = Marshal.GetDelegateForFunctionPointer<NativeLogDelegate>(nativeLog);
        }

        public static void Reset()
        {
            s_context = IntPtr.Zero;
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
        
        public static void Debug(string message)
        {
            Log(NativeLogLevel.Debug, string.Empty, message);
        }

        public static void Info(string message)
        {
            Log(NativeLogLevel.Info, string.Empty, message);
        }

        public static void Warn(string message)
        {
            Log(NativeLogLevel.Warn, string.Empty, message);
        }

        public static void Error(string message)
        {
            Log(NativeLogLevel.Error, string.Empty, message);
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
                s_log(s_context, (int)level, tagUtf8, messageUtf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(tagUtf8);
                Marshal.FreeCoTaskMem(messageUtf8);
            }
        }
    }
}
