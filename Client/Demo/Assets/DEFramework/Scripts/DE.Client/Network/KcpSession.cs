

using System;
using System.Net;

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
        public Action OnDisconnected;
    }


    public class KcpSession
    {
        public KcpSession(EndPoint endPoint, uint conv, KcpSessionCallback callback)
        {
            _EndPoint = endPoint;
            _Conv = conv;
            _Callback = callback;
            _State = KcpSessionState.Created;
        }

        private EndPoint _EndPoint;
        private uint _Conv;
        private KcpSessionState _State;
        private KcpSessionCallback _Callback;
    }

}