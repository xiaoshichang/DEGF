using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Assets.Scripts.DE.Share;
using DE.Server.Entities;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.NativeBridge
{
    public sealed class GameServerRuntimeState
    {
        private readonly ManagedRuntimeState _managedRuntimeState;

        public GameServerRuntimeState(ManagedRuntimeState managedRuntimeState)
        {
            _managedRuntimeState = managedRuntimeState ?? throw new ArgumentNullException(nameof(managedRuntimeState));
            SearchImportEntityTypeFromGameplayDll();
        }

        public Dictionary<Type, DateTime> ReadyStubs { get; } = new Dictionary<Type, DateTime>();
        public ServerStubDistributeTable StubDistributeTable { get; private set; } = new ServerStubDistributeTable();
        public Dictionary<Type, ServerStubEntity> StubInstances { get; } = new Dictionary<Type, ServerStubEntity>();
        public Dictionary<Guid, AvatarEntity> Avatars { get; } = new Dictionary<Guid, AvatarEntity>();
        public Dictionary<Guid, ServerEntity> Entities { get; } = new Dictionary<Guid, ServerEntity>();

        public Type AvatarType { get; private set; }
        public Type NpcType { get; private set; }
        public Type SpaceType { get; private set; }

        public void HandleAllNodeReady(ServerStubDistributeTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            StubDistributeTable = table;

            var assignedStubTypeKeys = table
                .GetAssignedStubTypeKeys(_managedRuntimeState.ServerId)
                .Where(stubTypeKey => !string.IsNullOrWhiteSpace(stubTypeKey))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(stubTypeKey => stubTypeKey, StringComparer.Ordinal)
                .ToList();

            DELogger.Info($"Server {_managedRuntimeState.ServerId} assigned {assignedStubTypeKeys.Count} stub(s).");

            foreach (var stubTypeKey in assignedStubTypeKeys)
            {
                CreateStub(stubTypeKey);
            }

            NotifyIfAllAssignedStubsReady(assignedStubTypeKeys);
        }

        public void NotifyStubReady(ServerStubEntity stubEntity)
        {
            if (stubEntity == null)
            {
                throw new ArgumentNullException(nameof(stubEntity));
            }

            var stubType = stubEntity.GetType();
            var stubTypeKey = ManagedRuntimeState.GetStubTypeKey(stubType);
            var assignedStubTypeKeys = StubDistributeTable.GetAssignedStubTypeKeys(_managedRuntimeState.ServerId);

            if (!assignedStubTypeKeys.Contains(stubTypeKey))
            {
                DELogger.Warn(
                    $"Ignored ready notification for unassigned stub {stubType.FullName} on {_managedRuntimeState.ServerId}."
                );
                return;
            }

            if (ReadyStubs.ContainsKey(stubType))
            {
                return;
            }

            ReadyStubs[stubType] = DateTime.UtcNow;
            NotifyIfAllAssignedStubsReady(assignedStubTypeKeys);
        }

        public void Uninitialize()
        {
            ReadyStubs.Clear();
            StubInstances.Clear();
            Avatars.Clear();
            Entities.Clear();
            StubDistributeTable = new ServerStubDistributeTable();
            AvatarType = null;
            NpcType = null;
            SpaceType = null;
        }

        public bool HandleCreateAvatarReq(string sourceServerId, Guid avatarId)
        {
            var result = CreateAvatarLocal(avatarId, sourceServerId);
            return NativeAPI.SendCreateAvatarRsp(
                sourceServerId,
                avatarId,
                result.IsSuccess,
                result.StatusCode,
                result.Error,
                result.AvatarData
            );
        }

        public bool CreateAvatarRemote(Guid avatarId)
        {
            var gameServerId = _managedRuntimeState.SelectGameServerId(avatarId);
            if (string.IsNullOrWhiteSpace(gameServerId))
            {
                return false;
            }

            if (string.Equals(gameServerId, _managedRuntimeState.ServerId, StringComparison.Ordinal))
            {
                return CreateAvatarLocal(avatarId, string.Empty).IsSuccess;
            }

            return NativeAPI.SendCreateAvatarReq(gameServerId, avatarId);
        }

        public void RegisterLocalEntity(ServerEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (entity.Guid == Guid.Empty)
            {
                throw new InvalidOperationException("Cannot register an entity with an empty guid.");
            }

            entity.AttachToGameServer(_managedRuntimeState.ServerId);
            Entities[entity.Guid] = entity;
            if (entity is AvatarEntity avatar)
            {
                Avatars[entity.Guid] = avatar;
            }
        }

        public bool HandleAvatarRpc(string sourceServerId, byte[] payload)
        {
            return HandleAvatarRpcPayload(sourceServerId, payload);
        }

        public bool HandleServerRpc(string sourceServerId, byte[] payload)
        {
            if (!ServerRpcPayload.TryDeserialize(payload, 0, payload == null ? 0 : payload.Length, out var serverRpc))
            {
                DELogger.Warn(nameof(GameServerRuntimeState), "Received invalid server RPC payload.");
                return false;
            }

            return HandleServerRpcPayload(sourceServerId, serverRpc);
        }

        public string FindStubServerId(string stubName)
        {
            if (string.IsNullOrWhiteSpace(stubName))
            {
                return string.Empty;
            }

            foreach (var pair in StubDistributeTable.NodeToStubTypeKeys)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                foreach (var stubTypeKey in pair.Value)
                {
                    if (!_managedRuntimeState.TryResolveStubType(stubTypeKey, out var stubType))
                    {
                        continue;
                    }

                    if (string.Equals(stubType.Name, stubName, StringComparison.Ordinal)
                        || string.Equals(stubType.FullName, stubName, StringComparison.Ordinal)
                        || string.Equals(stubTypeKey, stubName, StringComparison.Ordinal))
                    {
                        return pair.Key;
                    }
                }
            }

            return string.Empty;
        }

        private bool HandleAvatarRpcPayload(string sourceServerId, byte[] payload)
        {
            MessageDef.AvatarRpc rpc;
            if (!MessageDef.AvatarRpc.TryDeserialize(payload, 0, payload == null ? 0 : payload.Length, out rpc))
            {
                DELogger.Warn(nameof(GameServerRuntimeState), "Received invalid avatar RPC payload.");
                return false;
            }

            if (!Avatars.TryGetValue(rpc.AvatarId, out var avatar) || avatar == null)
            {
                DELogger.Warn(
                    nameof(GameServerRuntimeState),
                    $"Received avatar RPC for unknown avatarId={rpc.AvatarId}, sourceServerId={sourceServerId}."
                );
                return false;
            }

            if (!InvokeGeneratedServerRpc(avatar, rpc.MethodId, rpc.ArgsPayload))
            {
                DELogger.Warn(
                    nameof(GameServerRuntimeState),
                    $"Failed to invoke avatar RPC, avatarId={rpc.AvatarId}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}."
                );
                return false;
            }

            DELogger.Info(nameof(GameServerRuntimeState), $"Avatar RPC invoked, avatarId={rpc.AvatarId}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}.");
            return true;
        }

        private bool HandleServerRpcPayload(string sourceServerId, ServerRpcPayload rpc)
        {
            switch (rpc.TargetKind)
            {
            case ServerRpcTargetKind.Stub:
                return InvokeStubRpc(sourceServerId, rpc);
            case ServerRpcTargetKind.Entity:
                return InvokeEntityRpc(sourceServerId, rpc);
            case ServerRpcTargetKind.AvatarProxy:
                return InvokeAvatarProxyRpc(sourceServerId, rpc);
            default:
                DELogger.Warn(nameof(GameServerRuntimeState), $"Unknown server RPC target kind {rpc.TargetKind}, sourceServerId={sourceServerId}.");
                return false;
            }
        }

        private bool InvokeStubRpc(string sourceServerId, ServerRpcPayload rpc)
        {
            if (!TryResolveLocalStub(rpc.StubName, out var stubEntity))
            {
                DELogger.Warn(nameof(GameServerRuntimeState), $"Received server RPC for unknown local stub {rpc.StubName}, sourceServerId={sourceServerId}.");
                return false;
            }

            if (!InvokeGeneratedServerRpc(stubEntity, rpc.MethodId, rpc.ArgsPayload))
            {
                DELogger.Warn(nameof(GameServerRuntimeState), $"Failed to invoke stub RPC, stubName={rpc.StubName}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}.");
                return false;
            }

            DELogger.Info(nameof(GameServerRuntimeState), $"Stub RPC invoked, stubName={rpc.StubName}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}.");
            return true;
        }

        private bool InvokeEntityRpc(string sourceServerId, ServerRpcPayload rpc)
        {
            if (!Entities.TryGetValue(rpc.EntityId, out var entity) || entity == null)
            {
                DELogger.Warn(nameof(GameServerRuntimeState), $"Received entity RPC for unknown entityId={rpc.EntityId}, sourceServerId={sourceServerId}.");
                return false;
            }

            if (!InvokeGeneratedServerRpc(entity, rpc.MethodId, rpc.ArgsPayload))
            {
                DELogger.Warn(nameof(GameServerRuntimeState), $"Failed to invoke entity RPC, entityId={rpc.EntityId}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}.");
                return false;
            }

            DELogger.Info(nameof(GameServerRuntimeState), $"Entity RPC invoked, entityId={rpc.EntityId}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}.");
            return true;
        }

        private bool InvokeAvatarProxyRpc(string sourceServerId, ServerRpcPayload rpc)
        {
            if (!Avatars.TryGetValue(rpc.EntityId, out var avatar) || avatar == null)
            {
                DELogger.Warn(nameof(GameServerRuntimeState), $"Received avatar proxy RPC for unknown avatarId={rpc.EntityId}, sourceServerId={sourceServerId}.");
                return false;
            }

            if (!InvokeGeneratedServerRpc(avatar, rpc.MethodId, rpc.ArgsPayload))
            {
                DELogger.Warn(nameof(GameServerRuntimeState), $"Failed to invoke avatar proxy RPC, avatarId={rpc.EntityId}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}.");
                return false;
            }

            DELogger.Info(nameof(GameServerRuntimeState), $"Avatar proxy RPC invoked, avatarId={rpc.EntityId}, methodId={rpc.MethodId}, sourceServerId={sourceServerId}.");
            return true;
        }

        private bool TryResolveLocalStub(string stubName, out ServerStubEntity stubEntity)
        {
            stubEntity = null;
            if (string.IsNullOrWhiteSpace(stubName))
            {
                return false;
            }

            foreach (var pair in StubInstances)
            {
                var stubType = pair.Key;
                if (stubType == null)
                {
                    continue;
                }

                if (string.Equals(stubType.Name, stubName, StringComparison.Ordinal)
                    || string.Equals(stubType.FullName, stubName, StringComparison.Ordinal)
                    || string.Equals(ManagedRuntimeState.GetStubTypeKey(stubType), stubName, StringComparison.Ordinal))
                {
                    stubEntity = pair.Value;
                    return stubEntity != null;
                }
            }

            return false;
        }

        private void CreateStub(string stubTypeKey)
        {
            if (!_managedRuntimeState.TryResolveStubType(stubTypeKey, out var stubType))
            {
                throw new InvalidOperationException($"Stub type not found: {stubTypeKey}");
            }

            if (StubInstances.TryGetValue(stubType, out var stubEntity))
            {
                DELogger.Error(string.Empty, $"Stub instance already exists: {stubType.FullName}");
                return;
            }

            stubEntity = Activator.CreateInstance(stubType) as ServerStubEntity;
            if (stubEntity == null)
            {
                throw new InvalidOperationException($"Failed to create stub entity: {stubTypeKey}");
            }

            StubInstances[stubType] = stubEntity;
            stubEntity.InitStub();
        }

        private CreateAvatarResult CreateAvatarLocal(Guid avatarId, string gateServerId)
        {
            if (Avatars.ContainsKey(avatarId))
            {
                var existedAvatar = Avatars[avatarId];
                if (!string.IsNullOrWhiteSpace(gateServerId))
                {
                    existedAvatar.AttachToGateServer(gateServerId);
                }

                return new CreateAvatarResult(
                    true,
                    200,
                    string.Empty,
                    EntitySerializer.Serialize(existedAvatar, EntitySerializeReason.OwnerSync)
                );
            }

            var avatar = Activator.CreateInstance(AvatarType) as AvatarEntity;
            if (avatar == null)
            {
                return new CreateAvatarResult(false, 500, "failed to create avatar entity", Array.Empty<byte>());
            }

            avatar.Guid = avatarId;
            avatar.AttachToGateServer(gateServerId);
            RegisterLocalEntity(avatar);

            StubCaller.Call("LoginStub", "OnAvatarLogin", avatar.Proxy);

            DELogger.Info(
                nameof(GameServerRuntimeState),
                $"Created avatar entity, avatarId={avatarId}, avatarType={AvatarType.FullName}."
            );
            return new CreateAvatarResult(
                true,
                200,
                string.Empty,
                EntitySerializer.Serialize(avatar, EntitySerializeReason.OwnerSync)
            );
        }

        private static bool InvokeGeneratedServerRpc(ServerEntity entity, uint methodId, byte[] argsPayload)
        {
            if (InvokeGeneratedServerRpcOnTarget(entity, methodId, argsPayload))
            {
                return true;
            }

            foreach (EntityComponent component in entity.Components)
            {
                if (InvokeGeneratedServerRpcOnTarget(component, methodId, argsPayload))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InvokeGeneratedServerRpcOnTarget(object target, uint methodId, byte[] argsPayload)
        {
            for (var currentType = target.GetType(); currentType != null; currentType = currentType.BaseType)
            {
                var methodInfo = currentType.GetMethod(
                    "__DEGF_RPC_InvokeServerRpc",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                );
                if (methodInfo == null)
                {
                    continue;
                }

                if ((bool)methodInfo.Invoke(null, new object[] { target, methodId, new RpcBinaryReader(argsPayload) }))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct CreateAvatarResult
        {
            public CreateAvatarResult(bool isSuccess, int statusCode, string error, byte[] avatarData)
            {
                IsSuccess = isSuccess;
                StatusCode = statusCode;
                Error = error ?? string.Empty;
                AvatarData = avatarData ?? Array.Empty<byte>();
            }

            public bool IsSuccess { get; }
            public int StatusCode { get; }
            public string Error { get; }
            public byte[] AvatarData { get; }
        }

        private void NotifyIfAllAssignedStubsReady(IReadOnlyCollection<string> assignedStubTypeKeys)
        {
            foreach (var stubTypeKey in assignedStubTypeKeys)
            {
                if (!_managedRuntimeState.TryResolveStubType(stubTypeKey, out var stubType)
                    || !ReadyStubs.ContainsKey(stubType))
                {
                    return;
                }
            }

            DELogger.Info($"All assigned stubs are ready on {_managedRuntimeState.ServerId}.");
            NativeAPI.NotifyGameServerReady();
        }

        private void SearchImportEntityTypeFromGameplayDll()
        {
            AvatarType = SearchSingleImportEntityType(typeof(AvatarEntity));
            NpcType = SearchSingleImportEntityType(typeof(NpcEntity));
            SpaceType = SearchSingleImportEntityType(typeof(SpaceEntity));

            DELogger.Info(
                nameof(GameServerRuntimeState),
                $"Import entity types resolved: avatar={AvatarType.FullName}, npc={NpcType.FullName}, space={SpaceType.FullName}."
            );
        }

        private Type SearchSingleImportEntityType(Type baseEntityType)
        {
            if (baseEntityType == null)
            {
                throw new ArgumentNullException(nameof(baseEntityType));
            }

            var gameplayAssembly = _managedRuntimeState.GameplayAssembly;
            if (gameplayAssembly == null)
            {
                DELogger.Warn(
                    nameof(GameServerRuntimeState),
                    $"Gameplay assembly is not loaded. Fallback to framework entity type {baseEntityType.FullName}."
                );
                return baseEntityType;
            }

            Type[] allTypes;
            try
            {
                allTypes = gameplayAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                var loaderExceptions = string.Join(
                    Environment.NewLine,
                    exception.LoaderExceptions
                        .Where(loaderException => loaderException != null)
                        .Select(loaderException => loaderException.ToString())
                );
                throw new InvalidOperationException(
                    $"Failed to enumerate gameplay assembly types from {gameplayAssembly.FullName}.{Environment.NewLine}{loaderExceptions}",
                    exception
                );
            }

            var candidateTypes = allTypes
                .Where(type => type != null && !type.IsAbstract && baseEntityType.IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();

            if (candidateTypes.Count == 0)
            {
                DELogger.Warn(
                    nameof(GameServerRuntimeState),
                    $"No gameplay entity type derived from {baseEntityType.FullName} was found in {gameplayAssembly.FullName}. Fallback to framework entity type."
                );
                return baseEntityType;
            }

            if (candidateTypes.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one gameplay entity type derived from {baseEntityType.FullName}, but found {candidateTypes.Count}: "
                    + string.Join(", ", candidateTypes.Select(type => type.FullName))
                );
            }

            return candidateTypes[0];
        }
    }
}
