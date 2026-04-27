
using Assets.Scripts.DE.Client.Core;
using System.Net;

namespace Assets.Scripts.DE.Client.Network
{
    public class NetworkManager
    {
        public static NetworkManager Instance;

        public void KcpConnectTo(EndPoint endPoint, uint conv, KcpSessionCallback callback)
        {
            if (_Session != null)
            {
                DELogger.Error("not ready to connect");
                return;
            }
            _Callback = callback;

            KcpSessionCallback innerCallback = new KcpSessionCallback();
            innerCallback.OnRegistered = _OnKcpSessionRegistered;
            innerCallback.OnDisconnected = _OnKcpSessionDisconnected;

            _Session = new KcpSession(endPoint, conv, innerCallback);
        }


        private void _OnKcpSessionRegistered()
        {
            _Callback?.OnRegistered?.Invoke();
        }

        private void _OnKcpSessionDisconnected()
        {
            _Callback?.OnDisconnected?.Invoke();
        }

        private KcpSession _Session;
        private KcpSessionCallback _Callback;
    }

}