using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Share;
using System;
using System.Net;
using kcp2k;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Network
{
    public class NetworkManager
    {
        private const string LogTag = "NetworkManager";
        private const float HeartbeatIntervalSeconds = 30.0f;

        public static NetworkManager Instance;

        public bool IsConnected => _Session != null && _Session.IsRegistered;

        public void Init()
        {
            Log.Info = message => DELogger.Info("KCP", message);
            Log.Warning = message => DELogger.Warn("KCP", message);
            Log.Error = message => DELogger.Error("KCP", message);
            DELogger.Info("KCP", "KCP initialized.");
        }

        public void UnInit()
        {
            Disconnect();
            _Callback = null;
            _Session = null;
            DELogger.Info("KCP", "KCP uninitialized.");
        }

        public void KcpConnectTo(EndPoint endPoint, uint conv, KcpSessionCallback callback)
        {
            if (_Session != null)
            {
                DELogger.Error(LogTag, "KcpConnectTo ignored because a session already exists.");
                return;
            }

            if (endPoint == null)
            {
                DELogger.Error(LogTag, "KcpConnectTo failed because endPoint is null.");
                return;
            }

            _Callback = callback;

            KcpSessionCallback innerCallback = new KcpSessionCallback();
            innerCallback.OnRegistered = _OnKcpSessionRegistered;
            innerCallback.OnReceive = _OnKcpReceive;
            innerCallback.OnDisconnected = _OnKcpSessionDisconnected;

            try
            {
                _Session = new KcpSession(endPoint, conv, innerCallback);
                _Session.Connect();
                _NextHeartbeatRealtime = Time.realtimeSinceStartup + HeartbeatIntervalSeconds;
            }
            catch
            {
                _Session = null;
                _Callback?.OnDisconnected?.Invoke();
                _Callback = null;
            }
        }

        private void _OnKcpSessionRegistered()
        {
            if (_Callback?.OnRegistered == null)
            {
                return;
            }

            try
            {
                _Callback.OnRegistered.Invoke();
            }
            catch (System.Exception exception)
            {
                DELogger.Error(LogTag, "OnRegistered callback failed: " + exception);
            }
        }

        private void _OnKcpReceive(byte[] data)
        {
            if (_Callback?.OnReceive == null)
            {
                return;
            }

            try
            {
                _Callback.OnReceive(data);
            }
            catch (System.Exception exception)
            {
                DELogger.Error(LogTag, "OnReceive callback failed: " + exception);
            }
        }

        private void _OnKcpSessionDisconnected()
        {
            if (_Callback?.OnDisconnected != null)
            {
                try
                {
                    _Callback.OnDisconnected.Invoke();
                }
                catch (System.Exception exception)
                {
                    DELogger.Error(LogTag, "OnDisconnected callback failed: " + exception);
                }
            }

            _Session = null;
            _Callback = null;
            _NextHeartbeatRealtime = 0.0f;
        }

        public void TickIncoming()
        {
            _Session?.TickIncoming();
        }

        public void TickOutgoing()
        {
            _Session?.TickOutgoing();
            TickHeartbeat();
        }

        public void Disconnect()
        {
            if (_Session == null)
            {
                return;
            }

            _Session.Disconnect();
        }

        public void Send(byte[] data)
        {
            if (_Session == null)
            {
                DELogger.Error(LogTag, "Send failed because session is null.");
                return;
            }

            _Session.Send(data);
        }

        private void TickHeartbeat()
        {
            if (_Session == null || !_Session.IsRegistered)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _NextHeartbeatRealtime)
            {
                return;
            }

            _NextHeartbeatRealtime = now + HeartbeatIntervalSeconds;
            _Session.Send(BuildEmptyClientFrame((uint)MessageDef.MessageID.CS.HeartBeatNtf));
        }

        private static byte[] BuildEmptyClientFrame(uint messageId)
        {
            byte[] header = MessageDef.Header.CreateClient(messageId, 0).Serialize();
            byte[] frame = new byte[header.Length];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            return frame;
        }

        private KcpSession _Session;
        private KcpSessionCallback _Callback;
        private float _NextHeartbeatRealtime;
    }

}
