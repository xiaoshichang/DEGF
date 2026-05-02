using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DE.Server.Entities;

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
        public Dictionary<Type, ServerStubEntity> StubInstances { get; } = new Dictionary<Type, ServerStubEntity>();
        public ServerStubDistributeTable StubDistributeTable { get; private set; } = new ServerStubDistributeTable();
        public Type AvatarType { get; private set; }
        public Type NpcType { get; private set; }
        public Type SpaceType { get; private set; }
        public Dictionary<Guid, AvatarEntity> Avatars { get; } = new Dictionary<Guid, AvatarEntity>();

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
            StubDistributeTable = new ServerStubDistributeTable();
            AvatarType = null;
            NpcType = null;
            SpaceType = null;
        }

        public bool HandleCreateAvatarReq(string sourceServerId, Guid avatarId)
        {
            var result = CreateAvatarLocal(avatarId);
            return NativeAPI.SendCreateAvatarRsp(
                sourceServerId,
                avatarId,
                result.IsSuccess,
                result.StatusCode,
                result.Error
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
                return CreateAvatarLocal(avatarId).IsSuccess;
            }

            return NativeAPI.SendCreateAvatarReq(gameServerId, avatarId);
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

        private CreateAvatarResult CreateAvatarLocal(Guid avatarId)
        {
            if (Avatars.ContainsKey(avatarId))
            {
                return new CreateAvatarResult(true, 200, string.Empty);
            }

            var avatar = Activator.CreateInstance(AvatarType) as AvatarEntity;
            if (avatar == null)
            {
                return new CreateAvatarResult(false, 500, "failed to create avatar entity");
            }

            avatar.Guid = avatarId;
            Avatars[avatarId] = avatar;

            DELogger.Info(
                nameof(GameServerRuntimeState),
                $"Created avatar entity, avatarId={avatarId}, avatarType={AvatarType.FullName}."
            );
            return new CreateAvatarResult(true, 200, string.Empty);
        }

        private readonly struct CreateAvatarResult
        {
            public CreateAvatarResult(bool isSuccess, int statusCode, string error)
            {
                IsSuccess = isSuccess;
                StatusCode = statusCode;
                Error = error ?? string.Empty;
            }

            public bool IsSuccess { get; }
            public int StatusCode { get; }
            public string Error { get; }
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
