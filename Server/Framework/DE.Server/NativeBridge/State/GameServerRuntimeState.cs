using System;
using System.Collections.Generic;
using System.Linq;
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

            ManagedRuntimeState.StubDistributeTable = table;

            var assignedStubTypeKeys = table
                .GetAssignedStubTypeKeys(ManagedRuntimeState.ServerId)
                .Where(stubTypeKey => !string.IsNullOrWhiteSpace(stubTypeKey))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(stubTypeKey => stubTypeKey, StringComparer.Ordinal)
                .ToList();

            DELogger.Info(
                "GameServerRuntimeState",
                $"Server {ManagedRuntimeState.ServerId} assigned {assignedStubTypeKeys.Count} stub(s)."
            );

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
                DELogger.Warn(
                    "GameServerRuntimeState",
                    $"Ignored ready notification for unassigned stub {stubType.FullName} on {ManagedRuntimeState.ServerId}."
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

            NativeAPI.NotifyGameServerReady();
            DELogger.Info(
                "GameServerRuntimeState",
                $"All assigned stubs are ready on {ManagedRuntimeState.ServerId}."
            );
        }
    }
}
