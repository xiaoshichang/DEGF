using Assets.Scripts.DE.Client.Core;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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

        public KcpSession(EndPoint endPoint, uint conv, KcpSessionCallback callback)
        {
            _EndPoint = endPoint;
            _Conv = conv;
            _Callback = callback;
            _State = KcpSessionState.Created;
        }

        public bool IsRegistered => _State == KcpSessionState.Registered;

        public void Connect()
        {
            if (_State != KcpSessionState.Created)
            {
                DELogger.Error(LogTag, "Connect ignored because session is not in Created state.");
                return;
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

                _State = KcpSessionState.Registered;
                DELogger.Info(
                    LogTag,
                    "Session registered, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ".");
                _Callback?.OnRegistered?.Invoke();
            }
            catch (Exception exception)
            {
                _DisposeSocket();
                _Kcp = null;
                _State = KcpSessionState.Disconnected;
                DELogger.Error(
                    LogTag,
                    "Connect failed, remoteEndPoint=" + _EndPoint + ", conv=" + _Conv + ", error=" + exception + ".");
                throw;
            }
        }

        public void TickIncoming()
        {
            if (_State != KcpSessionState.Registered || _Socket == null || _Kcp == null)
            {
                return;
            }

            try
            {
                ArraySegment<byte> segment;
                while (_Socket.ReceiveNonBlocking(_RawReceiveBuffer, out segment))
                {
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
                        Disconnect();
                        return;
                    }
                }

                _DispatchReceivedMessages();
            }
            catch (SocketException exception)
            {
                DELogger.Error(
                    LogTag,
                    "Receive failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + exception + ".");
                Disconnect();
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
            }
        }

        public void TickOutgoing()
        {
            if (_State != KcpSessionState.Registered || _Kcp == null)
            {
                return;
            }

            try
            {
                _Kcp.Update((uint)_Stopwatch.ElapsedMilliseconds);
            }
            catch (SocketException exception)
            {
                DELogger.Error(
                    LogTag,
                    "TickOutgoing failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + exception + ".");
                Disconnect();
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
            }
        }

        public void Send(byte[] data)
        {
            if (_State != KcpSessionState.Registered)
            {
                DELogger.Error(LogTag, "Send failed because session is not registered.");
                return;
            }

            if (data == null || data.Length == 0)
            {
                DELogger.Error(LogTag, "Send failed because payload is empty.");
                return;
            }

            if (_Kcp == null)
            {
                DELogger.Error(LogTag, "Send failed because Kcp is not initialized.");
                return;
            }

            try
            {
                var sendResult = _Kcp.Send(data, 0, data.Length);
                if (sendResult < 0)
                {
                    DELogger.Error(
                        LogTag,
                        "Kcp send failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + sendResult + ", length=" + data.Length + ".");
                }
            }
            catch (Exception exception)
            {
                DELogger.Error(
                    LogTag,
                    "Send failed, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ", error=" + exception + ".");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            if (_State == KcpSessionState.Disconnected)
            {
                return;
            }

            _State = KcpSessionState.Disconnected;
            _Stopwatch.Reset();
            _Kcp = null;
            _DisposeSocket();

            DELogger.Info(
                LogTag,
                "Session disconnected, remoteEndPoint=" + _RemoteEndPoint + ", conv=" + _Conv + ".");
            _Callback?.OnDisconnected?.Invoke();
        }

        private void _DispatchReceivedMessages()
        {
            if (_Kcp == null)
            {
                return;
            }

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
                    Disconnect();
                    return;
                }

                var payload = new byte[receiveCount];
                Buffer.BlockCopy(_ReceiveMessageBuffer, 0, payload, 0, receiveCount);
                _Callback?.OnReceive?.Invoke(payload);
                if (_State != KcpSessionState.Registered || _Kcp == null)
                {
                    return;
                }
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
    }

}
