using System;
using System.Linq;
using DE.Server.NativeBridge;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public static class ServerMailboxCaller
    {
        public static bool Call(EntityMailBox mailbox, string methodName, params object[] args)
        {
            var runtime = ManagedRuntimeState.RequireCurrent();
            if (!mailbox.IsValid || !runtime.GetGameServerIds().Contains(mailbox.BindingGame))
            {
                return false;
            }
            byte[] payload = RpcCaller.BuildServerRpcPayload(
                RpcTargetKind.ServerMailbox, mailbox.EntityId, mailbox.BindingGame, string.Empty, methodName, args);
            if (runtime.ServerType == ManagedRuntimeServerType.Game && mailbox.BindingGame == runtime.ServerId)
            {
                return runtime.GameServerRuntimeState.HandleServerRpc(runtime.ServerId, payload);
            }
            string nextServer = runtime.ServerType == ManagedRuntimeServerType.Gate
                ? mailbox.BindingGame : runtime.SelectGateServerId(mailbox.EntityId.ToString());
            return !string.IsNullOrEmpty(nextServer) && NativeAPI.SendServerRpcToServer(nextServer, payload);
        }
    }
}
