using System;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    public static class ServerStubCaller
    {
        public static bool Call(string stubName, string methodName, params object[] args)
        {
            var runtimeState = ManagedRuntimeState.RequireCurrent();
            var targetServerId = runtimeState.FindStubServerId(stubName);
            if (string.IsNullOrWhiteSpace(targetServerId))
            {
                DELogger.Warn(nameof(ServerStubCaller), $"Stub target not found or stub distribute table is not ready, stubName={stubName}.");
                return false;
            }

            var payload = RpcCaller.BuildServerRpcPayload(ServerRpcTargetKind.Stub, Guid.Empty, targetServerId, stubName, methodName, args);
            if (runtimeState.ServerType == ManagedRuntimeServerType.Game
                && string.Equals(targetServerId, runtimeState.ServerId, StringComparison.Ordinal))
            {
                return runtimeState.GameServerRuntimeState.HandleServerRpc(targetServerId, payload);
            }

            if (runtimeState.ServerType == ManagedRuntimeServerType.Gate)
            {
                return NativeAPI.SendServerRpcToServer(targetServerId, payload);
            }

            var gateServerId = runtimeState.SelectGateServerId(stubName);
            if (string.IsNullOrWhiteSpace(gateServerId))
            {
                DELogger.Warn(nameof(ServerStubCaller), $"Gate relay not found for stub RPC, stubName={stubName}, targetServerId={targetServerId}.");
                return false;
            }

            return NativeAPI.SendServerRpcToServer(gateServerId, payload);
        }
    }
}