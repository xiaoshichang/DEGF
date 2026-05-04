using System;
using System.Collections.Generic;
using System.Net;
using Assets.Scripts.DE.Client.Core;
using DE.Client.Entities;
using Assets.Scripts.DE.Client.Network;
using Assets.Scripts.DE.Share;
using DE.Share.Entities;
using DE.Share.Utils;
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
        public Guid AvatarId;
        public AvatarEntity Avatar;
    }

    public enum AuthState
    {
        Idle = 0,
        HttpAuthenticating = 1,
        KcpConnecting = 2,
        HandShaking = 3,
        LoggingIn = 4,
        Authenticated = 5,
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

        public static AuthSystem Instance;

        public AuthState State => _State;
        public bool IsBusy => _State == AuthState.HttpAuthenticating || _State == AuthState.KcpConnecting || _State == AuthState.HandShaking || _State == AuthState.LoggingIn;
        public bool IsAuthenticated => _State == AuthState.Authenticated;
        public AuthAccountInfo CurrentAccountInfo => _CurrentAccountInfo;

        public void Init()
        {
            _IsInitialized = true;
            _SetState(AuthState.Idle);
            _ReceiveBuffer.Clear();
            DELogger.Info(LogTag, "AuthSystem initialized.");
        }

        public void UnInit()
        {
            _IsInitialized = false;
            _ReceiveBuffer.Clear();
            _PendingGateConfig = null;
            _PendingAccount = string.Empty;
            _CurrentAccountInfo = null;
            _SetState(AuthState.Idle);
            _ClearLoginCallbacks();

            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Disconnect();
            }
            DELogger.Info(LogTag, "AuthSystem uninitialized.");
        }

        public void Login(
            string account,
            string password,
            Action<AuthState> onStateChanged = null,
            Action<AuthAccountInfo> onSucceeded = null,
            Action<string> onFailed = null)
        {
            if (!_IsInitialized)
            {
                _NotifyLoginFailedImmediately(onFailed, "Login failed because AuthSystem is not initialized.");
                return;
            }

            if (NetworkManager.Instance == null)
            {
                _NotifyLoginFailedImmediately(onFailed, "Login failed because NetworkManager is not initialized.");
                return;
            }

            if (IsBusy)
            {
                _NotifyLoginFailedImmediately(onFailed, "Login ignored because auth flow is already in progress.");
                return;
            }

            var normalizedAccount = account == null ? string.Empty : account.Trim();
            var normalizedPassword = password == null ? string.Empty : password.Trim();
            if (string.IsNullOrEmpty(normalizedAccount) || string.IsNullOrEmpty(normalizedPassword))
            {
                _NotifyLoginFailedImmediately(onFailed, "Login failed because account or password is empty.");
                return;
            }

            _PendingStateChangedCallback = onStateChanged;
            _PendingSucceededCallback = onSucceeded;
            _PendingFailedCallback = onFailed;

            NetworkManager.Instance.Disconnect();

            _ReceiveBuffer.Clear();
            _CurrentAccountInfo = null;
            _PendingAccount = normalizedAccount;
            _PendingGateConfig = _SelectGateConfig(normalizedAccount);
            if (_PendingGateConfig == null)
            {
                _FailAuth("Login failed because no gate config is available.");
                return;
            }

            _SetState(AuthState.HttpAuthenticating);

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

            _SetState(AuthState.KcpConnecting);

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

            _SetState(AuthState.HandShaking);

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

            _SetState(AuthState.Idle);
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

            if (header.MessageId == (uint)MessageDef.MessageID.CS.HandShakeRsp)
            {
                _HandleHandShakeRsp(payload);
                return;
            }

            if (header.MessageId == (uint)MessageDef.MessageID.CS.LoginRsp)
            {
                _HandleLoginRsp(payload);
                return;
            }

            DELogger.Warn(
                LogTag,
                "Ignore unexpected message during auth flow, messageId=" + header.MessageId + ", state=" + _State + ".");
        }

        private void _HandleHandShakeRsp(byte[] payload)
        {
            if (_CurrentAccountInfo == null)
            {
                return;
            }

            if (_State != AuthState.HandShaking)
            {
                DELogger.Warn(
                    LogTag,
                    "Ignore handshake response because auth system is not in handshaking state, state=" + _State + ".");
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

            _SetState(AuthState.LoggingIn);
            DELogger.Info(
                LogTag,
                "Auth handshake succeeded, sending LoginReq, account=" + _CurrentAccountInfo.Account + ", gate=" + _CurrentAccountInfo.ServerId + ", sessionId=" + _CurrentAccountInfo.SessionId + ", conv=" + _CurrentAccountInfo.Conv + ".");
            NetworkManager.Instance.Send(_BuildLoginReqFrame(_CurrentAccountInfo.Account));
        }

        private void _HandleLoginRsp(byte[] payload)
        {
            if (_CurrentAccountInfo == null)
            {
                return;
            }

            if (_State != AuthState.LoggingIn && _State != AuthState.Authenticated)
            {
                DELogger.Warn(LogTag, "Ignore login response because auth system is not logging in, state=" + _State + ".");
                return;
            }

            MessageDef.LoginRsp loginRsp;
            if (!MessageDef.LoginRsp.TryDeserialize(payload, 0, payload.Length, out loginRsp))
            {
                _FailAuth("Received invalid login response payload.");
                return;
            }

            if (!loginRsp.IsSuccess)
            {
                _FailAuth("Login failed, statusCode=" + loginRsp.StatusCode + ", error=" + loginRsp.Error + ".");
                return;
            }

            if (loginRsp.AvatarId == Guid.Empty)
            {
                _FailAuth("Login failed because avatar id is empty.");
                return;
            }

            if (loginRsp.AvatarData == null || loginRsp.AvatarData.Length == 0)
            {
                _FailAuth("Login failed because avatar data is empty.");
                return;
            }

            var avatar = new AvatarEntity();
            if (!EntitySerializer.TryDeserialize(avatar, EntitySerializeReason.OwnerSync, loginRsp.AvatarData))
            {
                _FailAuth("Login failed because avatar data is invalid.");
                return;
            }

            if (avatar.Guid != loginRsp.AvatarId)
            {
                _FailAuth("Login failed because avatar data id mismatched, expected=" + loginRsp.AvatarId + ", actual=" + avatar.Guid + ".");
                return;
            }

            _CurrentAccountInfo.AvatarId = loginRsp.AvatarId;
            _CurrentAccountInfo.Avatar = avatar;
            _SetState(AuthState.Authenticated);
            DELogger.Info(
                LogTag,
                "Login succeeded, account=" + _CurrentAccountInfo.Account + ", gate=" + _CurrentAccountInfo.ServerId + ", sessionId=" + _CurrentAccountInfo.SessionId + ", conv=" + _CurrentAccountInfo.Conv + ", avatarId=" + _CurrentAccountInfo.AvatarId + ".");
            _NotifyLoginSucceeded(_CurrentAccountInfo);
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

        private byte[] _BuildLoginReqFrame(string account)
        {
            MessageDef.LoginReq loginReq = new MessageDef.LoginReq();
            loginReq.Version = MessageDef.LoginReq.CurrentVersion;
            loginReq.Reserved = 0;
            loginReq.Account = account ?? string.Empty;

            byte[] payload = loginReq.Serialize();
            byte[] header = MessageDef.Header.CreateClient((uint)MessageDef.MessageID.CS.LoginReq, (uint)payload.Length).Serialize();
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

            string[] gateServerIds = new string[s_DefaultGateConfigs.Length];
            for (int index = 0; index < s_DefaultGateConfigs.Length; index++)
            {
                gateServerIds[index] = s_DefaultGateConfigs[index].ServerId;
            }

            string targetGateServerId = GateSelector.SelectTargetGateServerId(account, gateServerIds);
            if (string.IsNullOrEmpty(targetGateServerId))
            {
                return null;
            }

            return _FindGateConfig(targetGateServerId);
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
            _SetState(AuthState.Idle);

            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.Disconnect();
            }

            _NotifyLoginFailed(message);
        }

        private void _SetState(AuthState state)
        {
            if (_State == state)
            {
                return;
            }

            _State = state;
            var callback = _PendingStateChangedCallback;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void _NotifyLoginSucceeded(AuthAccountInfo accountInfo)
        {
            var callback = _PendingSucceededCallback;
            _ClearLoginCallbacks();
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(accountInfo);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void _NotifyLoginFailed(string message)
        {
            var callback = _PendingFailedCallback;
            _ClearLoginCallbacks();
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void _NotifyLoginFailedImmediately(Action<string> callback, string message)
        {
            DELogger.Error(LogTag, message);
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void _ClearLoginCallbacks()
        {
            _PendingStateChangedCallback = null;
            _PendingSucceededCallback = null;
            _PendingFailedCallback = null;
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
        private Action<AuthState> _PendingStateChangedCallback;
        private Action<AuthAccountInfo> _PendingSucceededCallback;
        private Action<string> _PendingFailedCallback;
        private readonly List<byte> _ReceiveBuffer = new List<byte>();
    }
}
