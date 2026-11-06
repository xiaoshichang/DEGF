using System;
using System.Linq;

namespace DE.Server.NativeBridge
{
    internal static class SystemCommandCaller
    {
        internal static bool Send(SystemCommandPayload command)
        {
            var runtime = ManagedRuntimeState.RequireCurrent();
            bool targetIsGame = runtime.GetGameServerIds().Contains(command.TargetServerId);
            bool targetIsGate = runtime.GetGateServerIds().Contains(command.TargetServerId);
            if (!targetIsGame && !targetIsGate)
            {
                return false;
            }
            byte[] payload = command.Serialize();
            if (command.TargetServerId == runtime.ServerId)
            {
                return runtime.ServerType == ManagedRuntimeServerType.Game
                    ? runtime.GameServerRuntimeState.HandleSystemCommand(runtime.ServerId, payload)
                    : runtime.GateServerRuntimeState.HandleSystemCommand(runtime.ServerId, payload);
            }
            string nextServer = command.TargetServerId;
            if (runtime.ServerType == ManagedRuntimeServerType.Game && targetIsGame)
            {
                nextServer = runtime.SelectGateServerId(command.EntityId.ToString());
            }
            return !string.IsNullOrEmpty(nextServer) && NativeAPI.SendSystemCommandToServer(nextServer, payload);
        }
    }
}
