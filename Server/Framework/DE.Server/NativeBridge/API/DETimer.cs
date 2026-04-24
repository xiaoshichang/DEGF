using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DE.Server.NativeBridge
{
    public sealed class DETimer
    {
        private sealed class TimerRegistration
        {
            public TimerRegistration(Action callback, bool repeat)
            {
                Callback = callback ?? throw new ArgumentNullException(nameof(callback));
                Repeat = repeat;
            }

            public ulong TimerId;
            public readonly Action Callback;
            public readonly bool Repeat;
            public GCHandle Handle;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeTimerFiredDelegate(IntPtr context, ulong timerId, IntPtr state);

        private static readonly object s_syncRoot = new object();
        private static readonly Dictionary<ulong, TimerRegistration> s_registrations = new Dictionary<ulong, TimerRegistration>();
        private static readonly NativeTimerFiredDelegate s_nativeTimerFired = OnNativeTimerFired;
        private static readonly IntPtr s_nativeTimerFiredPtr = Marshal.GetFunctionPointerForDelegate(s_nativeTimerFired);

        private readonly ulong _timerId;
        private readonly bool _repeat;

        private DETimer(ulong timerId, bool repeat)
        {
            _timerId = timerId;
            _repeat = repeat;
        }

        public ulong TimerId => _timerId;

        public bool IsRepeating => _repeat;


        public static ulong AddTimer(int delayMilliseconds, Action callback, bool repeat = false)
        {
            return AddTimer(TimeSpan.FromMilliseconds(delayMilliseconds), callback, repeat);
        }

        public static ulong AddTimer(TimeSpan delay, Action callback, bool repeat = false)
        {
            return Create(delay, callback, repeat).TimerId;
        }

        public static bool HasTimer(ulong timerId)
        {
            lock (s_syncRoot)
            {
                return s_registrations.ContainsKey(timerId);
            }
        }

        public static bool CancelTimer(ulong timerId)
        {
            TimerRegistration registration;
            lock (s_syncRoot)
            {
                if (!s_registrations.TryGetValue(timerId, out registration))
                {
                    return false;
                }

                s_registrations.Remove(timerId);
            }

            var cancelled = NativeAPI.CancelTimer(timerId);
            ReleaseRegistration(registration);
            return cancelled;
        }

        internal static void Reset()
        {
            List<TimerRegistration> registrations;
            lock (s_syncRoot)
            {
                registrations = new List<TimerRegistration>(s_registrations.Values);
                s_registrations.Clear();
            }

            foreach (var registration in registrations)
            {
                ReleaseRegistration(registration);
            }
        }

        private static DETimer Create(TimeSpan delay, Action callback, bool repeat)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var delayMilliseconds = ValidateDelay(delay);
            var registration = new TimerRegistration(callback, repeat);
            registration.Handle = GCHandle.Alloc(registration);
            var state = GCHandle.ToIntPtr(registration.Handle);

            try
            {
                var timerId = NativeAPI.AddTimer(delayMilliseconds, repeat, s_nativeTimerFiredPtr, state);
                if (timerId == 0)
                {
                    throw new InvalidOperationException("Failed to create native timer.");
                }

                registration.TimerId = timerId;
                lock (s_syncRoot)
                {
                    s_registrations[timerId] = registration;
                }

                return new DETimer(timerId, repeat);
            }
            catch
            {
                ReleaseRegistration(registration);
                throw;
            }
        }

        private static long ValidateDelay(TimeSpan delay)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            if (delay.TotalMilliseconds > long.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            return checked((long)delay.TotalMilliseconds);
        }

        private static void ReleaseRegistration(TimerRegistration registration)
        {
            if (registration == null || !registration.Handle.IsAllocated)
            {
                return;
            }

            registration.Handle.Free();
        }

        private static void OnNativeTimerFired(IntPtr context, ulong timerId, IntPtr state)
        {
            _ = context;

            TimerRegistration registration = null;
            try
            {
                if (state == IntPtr.Zero)
                {
                    return;
                }

                var handle = GCHandle.FromIntPtr(state);
                registration = handle.Target as TimerRegistration;
                if (registration == null)
                {
                    return;
                }

                lock (s_syncRoot)
                {
                    if (!s_registrations.TryGetValue(timerId, out var currentRegistration)
                        || !ReferenceEquals(currentRegistration, registration))
                    {
                        return;
                    }

                    if (!registration.Repeat)
                    {
                        s_registrations.Remove(timerId);
                    }
                }

                registration.Callback();
            }
            catch (Exception exception)
            {
                DELogger.Error(nameof(DETimer), $"Timer callback {timerId} failed: {exception}");
            }
            finally
            {
                if (registration != null && !registration.Repeat)
                {
                    ReleaseRegistration(registration);
                }
            }
        }
    }
}
