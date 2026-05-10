using System;
using System.Runtime.InteropServices;

namespace DE.Server.NativeBridge
{
    public static class DEThreadDispatcher
    {
        private sealed class PostRegistration
        {
            public PostRegistration(Action action)
            {
                Action = action ?? throw new ArgumentNullException(nameof(action));
            }

            public readonly Action Action;
            public GCHandle Handle;
        }

        private static readonly NativeAPI.ManagedPostCallbackDelegate s_postCallback = OnPostCallback;
        private static readonly IntPtr s_postCallbackPtr = Marshal.GetFunctionPointerForDelegate(s_postCallback);

        public static bool Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var registration = new PostRegistration(action);
            registration.Handle = GCHandle.Alloc(registration);
            var state = GCHandle.ToIntPtr(registration.Handle);
            if (NativeAPI.PostToIoContext(s_postCallbackPtr, state))
            {
                return true;
            }

            ReleaseRegistration(registration);
            return false;
        }

        private static void OnPostCallback(IntPtr context, IntPtr state)
        {
            _ = context;

            PostRegistration registration = null;
            try
            {
                if (state == IntPtr.Zero)
                {
                    return;
                }

                var handle = GCHandle.FromIntPtr(state);
                registration = handle.Target as PostRegistration;
                registration?.Action();
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(DEThreadDispatcher), "Posted action failed: " + exception);
            }
            finally
            {
                ReleaseRegistration(registration);
            }
        }

        private static void ReleaseRegistration(PostRegistration registration)
        {
            if (registration == null || !registration.Handle.IsAllocated)
            {
                return;
            }

            registration.Handle.Free();
        }
    }
}
