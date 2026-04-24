using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    public static class GameServerRuntimeState
    {
        public static void HandleAllNodeReady(ServerStubDistributeTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            SearchImportEntityTypeFromGameplayDll();
            ManagedRuntimeState.StubDistributeTable = table;

            var assignedStubTypeKeys = table
                .GetAssignedStubTypeKeys(ManagedRuntimeState.ServerId)
                .Where(stubTypeKey => !string.IsNullOrWhiteSpace(stubTypeKey))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(stubTypeKey => stubTypeKey, StringComparer.Ordinal)
                .ToList();

            DELogger.Info($"Server {ManagedRuntimeState.ServerId} assigned {assignedStubTypeKeys.Count} stub(s).");

            foreach (var stubTypeKey in assignedStubTypeKeys)
            {
                CreateStub(stubTypeKey);
            }

            NotifyIfAllAssignedStubsReady(assignedStubTypeKeys);
        }

        public static void NotifyStubReady(ServerStubEntity stubEntity)
        {
            if (stubEntity == null)
            {
                throw new ArgumentNullException(nameof(stubEntity));
            }

            var stubType = stubEntity.GetType();
            var stubTypeKey = ManagedRuntimeState.GetStubTypeKey(stubType);
            var assignedStubTypeKeys = ManagedRuntimeState
                .StubDistributeTable
                .GetAssignedStubTypeKeys(ManagedRuntimeState.ServerId);

            if (!assignedStubTypeKeys.Contains(stubTypeKey))
            {
                DELogger.Warn($"Ignored ready notification for unassigned stub {stubType.FullName} on {ManagedRuntimeState.ServerId}."
                );
                return;
            }

            if (ManagedRuntimeState.ReadyStubs.ContainsKey(stubType))
            {
                return;
            }

            ManagedRuntimeState.ReadyStubs[stubType] = DateTime.UtcNow;
            NotifyIfAllAssignedStubsReady(assignedStubTypeKeys);
        }

        private static void CreateStub(string stubTypeKey)
        {
            if (!ManagedRuntimeState.TryResolveStubType(stubTypeKey, out var stubType))
            {
                throw new InvalidOperationException($"Stub type not found: {stubTypeKey}");
            }

            if (ManagedRuntimeState.StubInstances.TryGetValue(stubType, out var stubEntity))
            {
                DELogger.Error(string.Empty, $"Stub instance already exists: {stubType.FullName}");
                return;
            }
            
            stubEntity = Activator.CreateInstance(stubType) as ServerStubEntity;
            if (stubEntity == null)
            {
                throw new InvalidOperationException($"Failed to create stub entity: {stubTypeKey}");
            }

            ManagedRuntimeState.StubInstances[stubType] = stubEntity;
            stubEntity.InitStub();
        }

        private static void NotifyIfAllAssignedStubsReady(IReadOnlyCollection<string> assignedStubTypeKeys)
        {
            foreach (var stubTypeKey in assignedStubTypeKeys)
            {
                if (!ManagedRuntimeState.TryResolveStubType(stubTypeKey, out var stubType)
                    || !ManagedRuntimeState.ReadyStubs.ContainsKey(stubType))
                {
                    return;
                }
            }
            DELogger.Info($"All assigned stubs are ready on {ManagedRuntimeState.ServerId}." );
            NativeAPI.NotifyGameServerReady();
            
        }

        public static Type AvatarType;
        public static Type NpcType;
        public static Type SpaceType;
        public static void SearchImportEntityTypeFromGameplayDll()
        {
            AvatarType = SearchSingleImportEntityType(typeof(AvatarEntity));
            NpcType = SearchSingleImportEntityType(typeof(NpcEntity));
            SpaceType = SearchSingleImportEntityType(typeof(SpaceEntity));

            DELogger.Info(
                nameof(GameServerRuntimeState),
                $"Import entity types resolved: avatar={AvatarType.FullName}, npc={NpcType.FullName}, space={SpaceType.FullName}."
            );
        }

        private static Type SearchSingleImportEntityType(Type baseEntityType)
        {
            if (baseEntityType == null)
            {
                throw new ArgumentNullException(nameof(baseEntityType));
            }

            var gameplayAssembly = ManagedRuntimeState.GameplayAssembly;
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
