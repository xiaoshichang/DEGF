using Assets.Scripts.DE.Client.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using kcp2k;

namespace Assets.Scripts.DE.Client.Network
{
    public enum KcpSessionState
    {
        Created,
        Registered,
        Disconnected
    }

    public class KcpSessionCallback
    {
        public Action OnRegistered;
        public Action<byte[]> OnReceive;
        public Action OnDisconnected;
    }


    public class KcpSession
    {
        private const string LogTag = "KcpSession";
        private const int SocketBufferSize = 1024 * 1024 * 7;
        private const int RawReceiveBufferSize = 1500;
        private const uint KcpInterval = 10;
        private const int WorkerIdleWaitMilliseconds = 1;

        public KcpSession(EndPoint endPoint, uint conv, KcpSessionCallback callback)
        {
            _EndPoint = endPoint;
            _Conv = conv;
            _Callback = callback;
            _State = KcpSessionState.Created;
        }

        public bool IsRegistered
        {
            get
            {
                lock (_LifecycleLock)
                {
                    return _State == KcpSessionState.Registered;
                }
            }
        }

        public void Connect()
        {
            Thread workerThread = null;

            lock (_LifecycleLock)
            {
                if (_State != KcpSessionState.Created)
                {
                    DELogger.Error(LogTag, "Connect ignored because session is not in Created state.");
                    return;
                }
            }

            try
            {
                _RemoteEndPoint = _ResolveRemoteEndPoint(_EndPoint);
                if (_RemoteEndPoint == null)
                {
                    throw new InvalidOperationException("Resolve remote endpoint failed.");
                }

                _Socket = new Socket(_RemoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                _Socket.Blocking = false;
                Common.ConfigureSocketBuffers(_Socket, SocketBufferSize, SocketBufferSize);
                _Socket.Connect(_RemoteEndPoint);

                _Kcp = new Kcp(_Conv, _HandleKcpOutput);
                _Kcp.SetNoDelay(1u, KcpInterval, 0, true);
                _Kcp.SetWindowSize(Kcp.WND_SND, Kcp.WND_RCV);
                _Kcp.SetMtu(Kcp.MTU_DEF);

                _RawReceiveBuffer = new byte[RawReceiveBufferSize];
                _ReceiveMessageBuffer = new byte[Kcp.MTU_DEF];
                _Stopwatch.Restart();

                lock (_LifecycleLock)
                {
                    _WorkerStopRequested = false;
                    _DisconnectedEventQueued = false;
                    _DisconnectLogged = false;
                    _State = KcpSessionState.Registered;
                    _WorkerThread = new Thread(_WorkerLoop);
                    _WorkerThread.IsBackground = true;
                    _WorkerThread.Name = "KcpSession-" + _Conv;
                    workerThread = _WorkerThread;
                }

                workerThread.Start();
                DELogger.Info(
                    LogTag,
                    "Session registered, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ".");
                _EnqueueSessionEvent(SessionEventType.Registered, null);
            }
            catch (Exception exception)
            {
                lock (_LifecycleLock)
                {
                    _WorkerStopRequested = true;
                    _State = KcpSessionState.Disconnected;
                }

                _DisposeSocket();
                _Kcp = null;
                DELogger.Error(
                    LogTag,
                    "Connect failed, remoteEndPoint=" + _EndPoint + ", conv=" + _Conv + ", error=" + exception + ".");
                throw;
            }
        }

        public void TickIncoming()
        {
            _DispatchPendingSessionEvents();
        }

        public void TickOutgoing()
        {
            // Socket IO and KCP update run on the session worker thread.
        }

        public void Send(byte[] data)
        {
            if (!IsRegistered)
            {
                DELogger.Error(LogTag, "Send failed because session is not registered.");
                return;
            }

            if (data == null || data.Length == 0)
            {
                DELogger.Error(LogTag, "Send failed because payload is empty.");
                return;
            }

            byte[] payload = new byte[data.Length];
            Buffer.BlockCopy(data, 0, payload, 0, data.Length);
            lock (_PendingSendPayloadsLock)
            {
                _PendingSendPayloads.Enqueue(payload);
            }

            _WorkerWakeEvent.Set();
        }

        public void Disconnect()
        {
            Thread workerThread = null;
            if (!_TryBeginDisconnect(out workerThread))
            {
                return;
            }

            _WorkerWakeEvent.Set();
            if (workerThread != null && workerThread != Thread.CurrentThread)
            {
                workerThread.Join(1000);
            }

            _Stopwatch.Reset();
            _DisposeSocket();
            _Kcp = null;
            _WorkerThread = null;

            if (_TryMarkDisconnectLogged())
            {
                DELogger.Info(
                    LogTag,
                    "Session disconnected, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ".");
            }

            _EnqueueDisconnectedEventOnce();
        }

        private void _WorkerLoop()
        {
            while (_ShouldWorkerContinue())
            {
                bool didWork = false;

                try
                {
                    didWork |= _ProcessIncomingPacketsOnWorker();
                    if (!_ShouldWorkerContinue())
                    {
                        break;
                    }

                    didWork |= _DispatchReceivedMessagesOnWorker();
                    if (!_ShouldWorkerContinue())
                    {
                        break;
                    }

                    didWork |= _FlushSendQueueOnWorker();
                    if (!_ShouldWorkerContinue())
                    {
                        break;
                    }

                    _Kcp.Update((uint)_Stopwatch.ElapsedMilliseconds);
                }
                catch (SocketException exception)
                {
                    DELogger.Error(
                        LogTag,
                        "Worker thread failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + exception + ".");
                    _DisconnectFromWorker();
                    return;
                }
                catch (ObjectDisposedException)
                {
                    _DisconnectFromWorker();
                    return;
                }
                catch (Exception exception)
                {
                    DELogger.Error(
                        LogTag,
                        "Worker thread failed unexpectedly, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + exception + ".");
                    _DisconnectFromWorker();
                    return;
                }

                if (!didWork)
                {
                    _WorkerWakeEvent.WaitOne(WorkerIdleWaitMilliseconds);
                }
            }

            _DisposeSocket();
            _Kcp = null;
            _WorkerThread = null;
        }

        private bool _ProcessIncomingPacketsOnWorker()
        {
            if (_Socket == null || _Kcp == null || _RawReceiveBuffer == null)
            {
                return false;
            }

            bool didWork = false;
            ArraySegment<byte> segment;
            while (_Socket.ReceiveNonBlocking(_RawReceiveBuffer, out segment))
            {
                didWork = true;
                if (segment.Count <= 0)
                {
                    continue;
                }

                var inputResult = _Kcp.Input(segment.Array, segment.Offset, segment.Count);
                if (inputResult < 0)
                {
                    DELogger.Error(
                        LogTag,
                        "Kcp input failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + inputResult + ".");
                    _DisconnectFromWorker();
                    return false;
                }
            }

            return didWork;
        }

        private bool _FlushSendQueueOnWorker()
        {
            if (_Kcp == null)
            {
                return false;
            }

            bool didWork = false;
            while (true)
            {
                byte[] payload = null;
                lock (_PendingSendPayloadsLock)
                {
                    if (_PendingSendPayloads.Count > 0)
                    {
                        payload = _PendingSendPayloads.Dequeue();
                    }
                }

                if (payload == null)
                {
                    break;
                }

                didWork = true;
                var sendResult = _Kcp.Send(payload, 0, payload.Length);
                if (sendResult < 0)
                {
                    DELogger.Error(
                        LogTag,
                        "Kcp send failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + sendResult + ", length=" + payload.Length + ".");
                }
            }

            return didWork;
        }

        private void _DisconnectFromWorker()
        {
            if (!_TryBeginDisconnect(out _))
            {
                return;
            }

            _DisposeSocket();
            _Kcp = null;
            _WorkerThread = null;

            if (_TryMarkDisconnectLogged())
            {
                DELogger.Info(
                    LogTag,
                    "Session disconnected, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ".");
            }

            _EnqueueDisconnectedEventOnce();
        }

        private bool _DispatchReceivedMessagesOnWorker()
        {
            if (_Kcp == null)
            {
                return false;
            }

            bool didWork = false;
            while (true)
            {
                var nextMessageSize = _Kcp.PeekSize();
                if (nextMessageSize < 0)
                {
                    break;
                }

                _EnsureReceiveMessageBufferCapacity(nextMessageSize);

                var receiveCount = _Kcp.Receive(_ReceiveMessageBuffer, _ReceiveMessageBuffer.Length);
                if (receiveCount < 0)
                {
                    DELogger.Error(
                        LogTag,
                        "Kcp receive failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + receiveCount + ".");
                    _DisconnectFromWorker();
                    return false;
                }

                didWork = true;
                var payload = new byte[receiveCount];
                Buffer.BlockCopy(_ReceiveMessageBuffer, 0, payload, 0, receiveCount);
                _EnqueueSessionEvent(SessionEventType.Receive, payload);
                if (!_ShouldWorkerContinue() || _Kcp == null)
                {
                    return didWork;
                }
            }

            return didWork;
        }

        private void _DispatchPendingSessionEvents()
        {
            while (true)
            {
                SessionEvent sessionEvent;
                lock (_PendingSessionEventsLock)
                {
                    if (_PendingSessionEvents.Count == 0)
                    {
                        break;
                    }

                    sessionEvent = _PendingSessionEvents.Dequeue();
                }

                switch (sessionEvent.Type)
                {
                    case SessionEventType.Registered:
                        _Callback?.OnRegistered?.Invoke();
                        break;
                    case SessionEventType.Receive:
                        _Callback?.OnReceive?.Invoke(sessionEvent.Payload);
                        break;
                    case SessionEventType.Disconnected:
                        _Callback?.OnDisconnected?.Invoke();
                        break;
                }
            }
        }

        private void _EnqueueSessionEvent(SessionEventType sessionEventType, byte[] payload)
        {
            lock (_PendingSessionEventsLock)
            {
                _PendingSessionEvents.Enqueue(new SessionEvent(sessionEventType, payload));
            }
        }

        private void _EnqueueDisconnectedEventOnce()
        {
            bool shouldEnqueue = false;
            lock (_LifecycleLock)
            {
                if (!_DisconnectedEventQueued)
                {
                    _DisconnectedEventQueued = true;
                    shouldEnqueue = true;
                }
            }

            if (shouldEnqueue)
            {
                _EnqueueSessionEvent(SessionEventType.Disconnected, null);
            }
        }

        private bool _TryBeginDisconnect(out Thread workerThread)
        {
            workerThread = null;
            lock (_LifecycleLock)
            {
                if (_State == KcpSessionState.Disconnected)
                {
                    return false;
                }

                _State = KcpSessionState.Disconnected;
                _WorkerStopRequested = true;
                workerThread = _WorkerThread;
                return true;
            }
        }

        private bool _TryMarkDisconnectLogged()
        {
            lock (_LifecycleLock)
            {
                if (_DisconnectLogged)
                {
                    return false;
                }

                _DisconnectLogged = true;
                return true;
            }
        }

        private bool _ShouldWorkerContinue()
        {
            lock (_LifecycleLock)
            {
                return _State == KcpSessionState.Registered && !_WorkerStopRequested;
            }
        }

        private void _EnsureReceiveMessageBufferCapacity(int requiredSize)
        {
            if (_ReceiveMessageBuffer != null && _ReceiveMessageBuffer.Length >= requiredSize)
            {
                return;
            }

            _ReceiveMessageBuffer = new byte[requiredSize];
        }

        private EndPoint _ResolveRemoteEndPoint(EndPoint endPoint)
        {
            var ipEndPoint = endPoint as IPEndPoint;
            if (ipEndPoint != null)
            {
                return ipEndPoint;
            }

            var dnsEndPoint = endPoint as DnsEndPoint;
            if (dnsEndPoint == null)
            {
                throw new NotSupportedException("Unsupported endpoint type: " + endPoint.GetType().FullName + ".");
            }

            IPAddress[] addresses;
            if (!Common.ResolveHostname(dnsEndPoint.Host, out addresses) || addresses == null || addresses.Length == 0)
            {
                throw new InvalidOperationException("Resolve host failed: " + dnsEndPoint.Host + ".");
            }

            return new IPEndPoint(addresses[0], dnsEndPoint.Port);
        }

        private void _HandleKcpOutput(byte[] buffer, int size)
        {
            if (_Socket == null || buffer == null || size <= 0)
            {
                return;
            }

            _Socket.SendNonBlocking(new ArraySegment<byte>(buffer, 0, size));
        }

        private void _DisposeSocket()
        {
            if (_Socket == null)
            {
                return;
            }

            _Socket.Close();
            _Socket = null;
        }

        private EndPoint _EndPoint;
        private uint _Conv;
        private KcpSessionState _State;
        private KcpSessionCallback _Callback;
        private EndPoint _RemoteEndPoint;
        private Socket _Socket;
        private Kcp _Kcp;
        private byte[] _RawReceiveBuffer;
        private byte[] _ReceiveMessageBuffer;
        private readonly Stopwatch _Stopwatch = new Stopwatch();
        private readonly object _LifecycleLock = new object();
        private readonly object _PendingSendPayloadsLock = new object();
        private readonly object _PendingSessionEventsLock = new object();
        private readonly Queue<byte[]> _PendingSendPayloads = new Queue<byte[]>();
        private readonly Queue<SessionEvent> _PendingSessionEvents = new Queue<SessionEvent>();
        private readonly AutoResetEvent _WorkerWakeEvent = new AutoResetEvent(false);
        private Thread _WorkerThread;
        private bool _WorkerStopRequested;
        private bool _DisconnectedEventQueued;
        private bool _DisconnectLogged;

        private sealed class SessionEvent
        {
            public SessionEvent(SessionEventType sessionEventType, byte[] payload)
            {
                Type = sessionEventType;
                Payload = payload;
            }

            public SessionEventType Type;
            public byte[] Payload;
        }

        private enum SessionEventType
        {
            Registered = 0,
            Receive = 1,
            Disconnected = 2,
        }
    }

}
