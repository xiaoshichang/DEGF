using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.Network;
using Assets.Scripts.DE.Share;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Framework
{
    [Serializable]
    public sealed class AuthAccountInfo
    {
        public string Account;
        public string ServerId;
        public ulong SessionId;
        public uint Conv;
        public string AuthHost;
        public int AuthPort;
        public string ClientHost;
        public int ClientPort;
    }

    public enum AuthState
    {
        Idle = 0,
        HttpAuthenticating = 1,
        KcpConnecting = 2,
        HandShaking = 3,
        Authenticated = 4,
    }

    internal sealed class GateEndpointConfig
    {
        public string ServerId;
        public string AuthHost;
        public int AuthPort;
        public string ClientHost;
    }

    [Serializable]
    public sealed class AuthHttpRequestDto
    {
        public string account;
        public string password;
    }

    [Serializable]
    public sealed class AuthHttpResponseDto
    {
        public string serverId;
        public long sessionId;
        public int conv;
        public int clientPort;
        public string error;
    }

    public class AuthSystem
    {
        private const string LogTag = "AuthSystem";
        private const string AuthPath = "/auth";
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        public static AuthSystem Instance;

        public AuthState State => _State;
        public bool IsBusy => _State == AuthState.HttpAuthenticating || _State == AuthState.KcpConnecting || _State == AuthState.HandShaking;
        public bool IsAuthenticated => _State == AuthState.Authenticated;
        public AuthAccountInfo CurrentAccountInfo => _CurrentAccountInfo;

        public void Init()
        {
            _IsInitialized = true;
            _State = AuthState.Idle;
            _ReceiveBuffer.Clear();
        }

        public void UnInit()
        {
            _IsInitialized = false;
            _ReceiveBuffer.Clear();
            _PendingGateConfig = null;
            _PendingAccount = string.Empty;
            _CurrentAccountInfo = null;
            _State = AuthState.Idle;

            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Disconnect();
            }
        }

        public void Login(string account, string password)
        {
            if (!_IsInitialized)
            {
                DELogger.Error(LogTag, "Login failed because AuthSystem is not initialized.");
                return;
            }

            if (NetworkManager.Instance == null)
            {
                DELogger.Error(LogTag, "Login failed because NetworkManager is not initialized.");
                return;
            }

            if (IsBusy)
            {
                DELogger.Warn(LogTag, "Login ignored because auth flow is already in progress.");
                return;
            }

            var normalizedAccount = account == null ? string.Empty : account.Trim();
            var normalizedPassword = password == null ? string.Empty : password.Trim();
            if (string.IsNullOrEmpty(normalizedAccount) || string.IsNullOrEmpty(normalizedPassword))
            {
                DELogger.Error(LogTag, "Login failed because account or password is empty.");
                return;
            }

            NetworkManager.Instance.Disconnect();

            _ReceiveBuffer.Clear();
            _CurrentAccountInfo = null;
            _PendingAccount = normalizedAccount;
            _PendingGateConfig = _SelectGateConfig(normalizedAccount);
            if (_PendingGateConfig == null)
            {
                DELogger.Error(LogTag, "Login failed because no gate config is available.");
                _State = AuthState.Idle;
                return;
            }

            _State = AuthState.HttpAuthenticating;

            var requestBody = JsonUtility.ToJson(new AuthHttpRequestDto
            {
                account = normalizedAccount,
                password = normalizedPassword,
            });

            DELogger.Info(
                LogTag,
                "Begin HTTP auth, account=" + normalizedAccount + ", gate=" + _PendingGateConfig.ServerId + ", endpoint=" + _PendingGateConfig.AuthHost + ":" + _PendingGateConfig.AuthPort + ".");

            HttpRequest.PostAsync(
                _BuildAuthUrl(_PendingGateConfig),
                requestBody,
                _HandleAuthHttpResponse);
        }

        private void _HandleAuthHttpResponse(HttpResponse response)
        {
            if (!_IsInitialized || _State != AuthState.HttpAuthenticating)
            {
                return;
            }

            if (response == null)
            {
                _FailAuth("HTTP auth failed because response is null.");
                return;
            }

            AuthHttpResponseDto responseDto = null;
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                try
                {
                    responseDto = JsonUtility.FromJson<AuthHttpResponseDto>(response.Text);
                }
                catch (Exception exception)
                {
                    DELogger.Warn(LogTag, "Parse HTTP auth response failed: " + exception.Message);
                }
            }

            if (!response.IsSuccess)
            {
                var errorMessage = responseDto == null || string.IsNullOrWhiteSpace(responseDto.error)
                    ? (string.IsNullOrWhiteSpace(response.Error) ? "HTTP auth request failed." : response.Error)
                    : responseDto.error;
                _FailAuth("HTTP auth failed, statusCode=" + response.StatusCode + ", error=" + errorMessage + ".");
                return;
            }

            if (responseDto == null)
            {
                _FailAuth("HTTP auth failed because response json is invalid.");
                return;
            }

            if (responseDto.sessionId <= 0 || responseDto.conv <= 0 || responseDto.clientPort <= 0 || string.IsNullOrWhiteSpace(responseDto.serverId))
            {
                _FailAuth("HTTP auth failed because response fields are invalid.");
                return;
            }

            if (_PendingGateConfig == null)
            {
                _FailAuth("HTTP auth failed because pending gate config is missing.");
                return;
            }

            if (!string.Equals(responseDto.serverId, _PendingGateConfig.ServerId, StringComparison.Ordinal))
            {
                _FailAuth(
                    "HTTP auth failed because gate routing mismatched, expected="
                    + _PendingGateConfig.ServerId
                    + ", actual="
                    + responseDto.serverId
                    + ".");
                return;
            }

            var gateConfig = _FindGateConfig(responseDto.serverId);
            if (gateConfig == null)
            {
                _FailAuth("HTTP auth failed because gate serverId is unknown: " + responseDto.serverId + ".");
                return;
            }

            _CurrentAccountInfo = new AuthAccountInfo
            {
                Account = _PendingAccount,
                ServerId = responseDto.serverId,
                SessionId = (ulong)responseDto.sessionId,
                Conv = (uint)responseDto.conv,
                AuthHost = gateConfig.AuthHost,
                AuthPort = gateConfig.AuthPort,
                ClientHost = gateConfig.ClientHost,
                ClientPort = responseDto.clientPort,
            };

            DELogger.Info(
                LogTag,
                "HTTP auth succeeded, account=" + _CurrentAccountInfo.Account + ", gate=" + _CurrentAccountInfo.ServerId + ", sessionId=" + _CurrentAccountInfo.SessionId + ", conv=" + _CurrentAccountInfo.Conv + ", clientPort=" + _CurrentAccountInfo.ClientPort + ".");

            _State = AuthState.KcpConnecting;

            KcpSessionCallback sessionCallback = new KcpSessionCallback();
            sessionCallback.OnRegistered = _OnKcpSessionRegistered;
            sessionCallback.OnReceive = _OnKcpReceive;
            sessionCallback.OnDisconnected = _OnKcpSessionDisconnected;

            NetworkManager.Instance.KcpConnectTo(
                new DnsEndPoint(_CurrentAccountInfo.ClientHost, _CurrentAccountInfo.ClientPort),
                _CurrentAccountInfo.Conv,
                sessionCallback);
        }

        private void _OnKcpSessionRegistered()
        {
            if (!_IsInitialized || _State != AuthState.KcpConnecting || _CurrentAccountInfo == null)
            {
                return;
            }

            _State = AuthState.HandShaking;

            byte[] handShakeFrame = _BuildClientHandShakeFrame(_CurrentAccountInfo.SessionId);
            DELogger.Info(
                LogTag,
                "KCP connected, sending handshake, account=" + _CurrentAccountInfo.Account + ", gate=" + _CurrentAccountInfo.ServerId + ", sessionId=" + _CurrentAccountInfo.SessionId + ".");
            NetworkManager.Instance.Send(handShakeFrame);
        }

        private void _OnKcpReceive(byte[] data)
        {
            if (!_IsInitialized || data == null || data.Length == 0)
            {
                return;
            }

            _ReceiveBuffer.AddRange(data);
            _TryHandleReceivedFrames();
        }

        private void _OnKcpSessionDisconnected()
        {
            if (!_IsInitialized)
            {
                return;
            }

            var keepAccountInfo = _State == AuthState.Authenticated;
            if (_State == AuthState.Authenticated)
            {
                DELogger.Warn(LogTag, "KCP session disconnected after auth success.");
            }
            else if (_State != AuthState.Idle)
            {
                DELogger.Warn(LogTag, "KCP session disconnected before auth flow completed.");
            }

            _ReceiveBuffer.Clear();
            _PendingGateConfig = null;
            _PendingAccount = string.Empty;
            if (!keepAccountInfo)
            {
                _CurrentAccountInfo = null;
            }

            _State = AuthState.Idle;
        }

        private void _TryHandleReceivedFrames()
        {
            while (_ReceiveBuffer.Count >= MessageDef.Header.WireSize)
            {
                byte[] buffer = _ReceiveBuffer.ToArray();
                MessageDef.Header header;
                if (!MessageDef.Header.TryDeserialize(buffer, 0, buffer.Length, out header))
                {
                    _FailAuth("Received invalid client frame header during auth handshake.");
                    return;
                }

                var frameLength = (int)header.GetFrameLength();
                if (_ReceiveBuffer.Count < frameLength)
                {
                    return;
                }

                byte[] payload = Array.Empty<byte>();
                if (header.Length > 0)
                {
                    payload = new byte[(int)header.Length];
                    Buffer.BlockCopy(buffer, header.HeaderLength, payload, 0, (int)header.Length);
                }

                _ReceiveBuffer.RemoveRange(0, frameLength);
                _HandleReceivedFrame(header, payload);
                if (!_IsInitialized || _State == AuthState.Idle)
                {
                    return;
                }
            }
        }

        private void _HandleReceivedFrame(MessageDef.Header header, byte[] payload)
        {
            if (_CurrentAccountInfo == null)
            {
                return;
            }

            if (!MessageDef.MessageID.IsCS(header.MessageId))
            {
                _FailAuth("Received non-CS message during client auth flow, messageId=" + header.MessageId + ".");
                return;
            }

            if (header.MessageId != (uint)MessageDef.MessageID.CS.HandShakeRsp)
            {
                DELogger.Warn(
                    LogTag,
                    "Ignore unexpected message during auth flow, messageId=" + header.MessageId + ", state=" + _State + ".");
                return;
            }

            if (_State != AuthState.HandShaking)
            {
                DELogger.Warn(LogTag, "Ignore handshake response because auth system is not in handshaking state.");
                return;
            }

            MessageDef.ClientHandShakeMessage handShakeMessage;
            if (!MessageDef.ClientHandShakeMessage.TryDeserialize(payload, 0, payload.Length, out handShakeMessage))
            {
                _FailAuth("Received invalid handshake response payload.");
                return;
            }

            if (handShakeMessage.SessionId != _CurrentAccountInfo.SessionId)
            {
                _FailAuth(
                    "Received handshake response with mismatched sessionId, expected="
                    + _CurrentAccountInfo.SessionId
                    + ", actual="
                    + handShakeMessage.SessionId
                    + ".");
                return;
            }

            _State = AuthState.Authenticated;
            DELogger.Info(
                LogTag,
                "Auth handshake succeeded, account=" + _CurrentAccountInfo.Account + ", gate=" + _CurrentAccountInfo.ServerId + ", sessionId=" + _CurrentAccountInfo.SessionId + ", conv=" + _CurrentAccountInfo.Conv + ".");
        }

        private byte[] _BuildClientHandShakeFrame(ulong sessionId)
        {
            MessageDef.ClientHandShakeMessage handShakeMessage = new MessageDef.ClientHandShakeMessage();
            handShakeMessage.Version = MessageDef.ClientHandShakeMessage.CurrentVersion;
            handShakeMessage.Reserved = 0;
            handShakeMessage.SessionId = sessionId;

            byte[] payload = handShakeMessage.Serialize();
            byte[] header = MessageDef.Header.CreateClient((uint)MessageDef.MessageID.CS.HandShakeReq, (uint)payload.Length).Serialize();
            byte[] frame = new byte[header.Length + payload.Length];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            Buffer.BlockCopy(payload, 0, frame, header.Length, payload.Length);
            return frame;
        }

        private GateEndpointConfig _SelectGateConfig(string account)
        {
            if (string.IsNullOrEmpty(account) || s_DefaultGateConfigs.Length == 0)
            {
                return null;
            }

            uint hash = _ComputeAccountGateHash(account);
            GateEndpointConfig[] orderedGateConfigs = _GetOrderedGateConfigs();
            var gateIndex = (int)(hash % (uint)orderedGateConfigs.Length);
            return orderedGateConfigs[gateIndex];
        }

        private uint _ComputeAccountGateHash(string account)
        {
            uint hash = FnvOffsetBasis;
            byte[] accountBytes = Encoding.UTF8.GetBytes(account);
            for (int index = 0; index < accountBytes.Length; index++)
            {
                hash ^= accountBytes[index];
                hash *= FnvPrime;
            }

            return hash;
        }

        private GateEndpointConfig[] _GetOrderedGateConfigs()
        {
            GateEndpointConfig[] orderedGateConfigs = new GateEndpointConfig[s_DefaultGateConfigs.Length];
            Array.Copy(s_DefaultGateConfigs, orderedGateConfigs, s_DefaultGateConfigs.Length);
            Array.Sort(
                orderedGateConfigs,
                (left, right) => _CompareGateServerId(left.ServerId, right.ServerId));
            return orderedGateConfigs;
        }

        private int _CompareGateServerId(string leftServerId, string rightServerId)
        {
            int leftIndex;
            int rightIndex;
            bool hasLeftIndex = _TryParseGateIndex(leftServerId, out leftIndex);
            bool hasRightIndex = _TryParseGateIndex(rightServerId, out rightIndex);
            if (hasLeftIndex && hasRightIndex && leftIndex != rightIndex)
            {
                return leftIndex.CompareTo(rightIndex);
            }

            return string.CompareOrdinal(leftServerId, rightServerId);
        }

        private bool _TryParseGateIndex(string serverId, out int gateIndex)
        {
            gateIndex = 0;
            if (string.IsNullOrEmpty(serverId) || !serverId.StartsWith("Gate", StringComparison.Ordinal) || serverId.Length <= 4)
            {
                return false;
            }

            return int.TryParse(serverId.Substring(4), out gateIndex);
        }

        private GateEndpointConfig _FindGateConfig(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                return null;
            }

            for (int index = 0; index < s_DefaultGateConfigs.Length; index++)
            {
                GateEndpointConfig gateConfig = s_DefaultGateConfigs[index];
                if (string.Equals(gateConfig.ServerId, serverId, StringComparison.Ordinal))
                {
                    return gateConfig;
                }
            }

            return null;
        }

        private string _BuildAuthUrl(GateEndpointConfig gateConfig)
        {
            return "http://" + gateConfig.AuthHost + ":" + gateConfig.AuthPort + AuthPath;
        }

        private void _FailAuth(string message)
        {
            DELogger.Error(LogTag, message);
            _ReceiveBuffer.Clear();
            _PendingGateConfig = null;
            _PendingAccount = string.Empty;
            _CurrentAccountInfo = null;
            _State = AuthState.Idle;

            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Disconnect();
            }
        }

        private static readonly GateEndpointConfig[] s_DefaultGateConfigs =
        {
            new GateEndpointConfig
            {
                ServerId = "Gate0",
                AuthHost = "127.0.0.1",
                AuthPort = 4100,
                ClientHost = "127.0.0.1",
            },
            new GateEndpointConfig
            {
                ServerId = "Gate1",
                AuthHost = "127.0.0.1",
                AuthPort = 4101,
                ClientHost = "127.0.0.1",
            },
        };

        private bool _IsInitialized;
        private AuthState _State = AuthState.Idle;
        private string _PendingAccount = string.Empty;
        private GateEndpointConfig _PendingGateConfig;
        private AuthAccountInfo _CurrentAccountInfo;
        private readonly List<byte> _ReceiveBuffer = new List<byte>();
    }
}
