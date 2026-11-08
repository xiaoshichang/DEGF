using System;
using System.Linq;
using DE.Server.Entities;
using DE.Server.NativeBridge;
using DE.Share.Data;
using DE.Share.DataFramework;
using DE.Share.Rpc;

namespace DE.Server.Stubs
{
    public partial class SpaceStub
    {
        public override void InitStub()
        {
            if (!IsInitialized)
            {
                DELogger.Error(nameof(SpaceStub), $"Cannot start SpaceStub before entity initialization, lifecycleState={LifecycleState}.");
                return;
            }
            if (State != SpaceStubState.Created)
            {
                DELogger.Error(nameof(SpaceStub), $"Cannot initialize SpaceStub in its current state, state={State}.");
                return;
            }
            State = SpaceStubState.Initializing;
            PlanPersistentSpaces();
            var runtime = ManagedRuntimeState.RequireCurrentGameServerRuntimeState();
            foreach (var space in _spaceProxiesById.Values)
            {
                space.CreationState = SpaceCreationState.Creating;
                try
                {
                    if (!RemoteEntityCaller.Create(space.MailBox.BindingGame, runtime.SpaceType, space.SpaceId,
                        new SpaceInitializer { SpaceNo = space.SpaceNo }))
                    {
                        space.CreationState = SpaceCreationState.Failed;
                        space.CreationError = "Remote creation request could not be sent or handled.";
                        DELogger.Error(nameof(SpaceStub),
                            $"Space creation request failed, spaceNo={space.SpaceNo}, spaceId={space.SpaceId}, game={space.MailBox.BindingGame}.");
                    }
                }
                catch (Exception exception)
                {
                    space.CreationState = SpaceCreationState.Failed;
                    space.CreationError = exception.ToString();
                    DELogger.Error(nameof(SpaceStub),
                        $"Space creation request threw an exception, spaceNo={space.SpaceNo}, spaceId={space.SpaceId}, "
                        + $"game={space.MailBox.BindingGame}: {exception}");
                }
            }
            if (_spaceProxiesById.Values.Any(space => space.CreationState == SpaceCreationState.Failed))
            {
                State = SpaceStubState.Failed;
                return;
            }
            State = SpaceStubState.WaitingForPersistentSpaceCreation;
            TryCompleteInitialization();
        }

        private void PlanPersistentSpaces()
        {
            var games = ManagedRuntimeState.RequireCurrent().GetGameServerIds();
            var rows = DataRuntime.GetTable<SpaceDataTable>().GetRows().OrderBy(row => row.Id.AsInt32()).ToArray();
            foreach (var row in rows)
            {
                if (row.Id.AsInt32() <= 0)
                {
                    throw new InvalidOperationException($"SpaceNo must be positive, spaceNo={row.Id.AsInt32()}.");
                }
                if (row.PersistentCount < 0)
                {
                    throw new InvalidOperationException(
                        $"Space PersistentCount must be nonnegative, spaceNo={row.Id.AsInt32()}, persistentCount={row.PersistentCount}.");
                }
                if (row.MaxPlayers < 0)
                {
                    throw new InvalidOperationException(
                        $"Space MaxPlayers must be nonnegative, spaceNo={row.Id.AsInt32()}, maxPlayers={row.MaxPlayers}.");
                }
            }
            if (CreationStrategy == null)
            {
                throw new InvalidOperationException("Space creation strategy is required.");
            }

            int creationIndex = 0;
            foreach (var row in rows)
            {
                for (int index = 0; index < row.PersistentCount; index++)
                {
                    var gameServerId = CreationStrategy.SelectGameServer(row.Id.AsInt32(), creationIndex++, games);
                    if (string.IsNullOrWhiteSpace(gameServerId))
                    {
                        throw new InvalidOperationException($"Space creation strategy selected an empty Game ID, spaceNo={row.Id.AsInt32()}.");
                    }
                    if (!games.Contains(gameServerId))
                    {
                        throw new InvalidOperationException(
                            $"Space creation strategy selected an unavailable Game, spaceNo={row.Id.AsInt32()}, game={gameServerId}.");
                    }
                    var spaceProxy = new SpaceEntityStubProxy(Guid.NewGuid(), row.Id.AsInt32(), gameServerId, row.MaxPlayers);
                    _spaceProxiesById.Add(spaceProxy.SpaceId, spaceProxy);
                }
            }
        }

        [ServerRpc]
        public void ReportSpaceCreated(EntityMailBox mailbox, int spaceNo)
        {
            if (State == SpaceStubState.UnInitialized)
            {
                DELogger.Error(nameof(SpaceStub), $"Space creation report rejected because the Stub is stopped, spaceId={mailbox.EntityId}.");
                return;
            }
            if (!mailbox.IsValid)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space creation report contains an invalid Mailbox, spaceId={mailbox.EntityId}, game={mailbox.BindingGame}.");
                return;
            }
            if (!_spaceProxiesById.TryGetValue(mailbox.EntityId, out var spaceProxy))
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space creation report refers to an unknown Space proxy, spaceId={mailbox.EntityId}, spaceNo={spaceNo}.");
                return;
            }
            if (spaceProxy.SpaceNo != spaceNo)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space creation report has a mismatched SpaceNo, spaceId={mailbox.EntityId}, "
                    + $"expectedSpaceNo={spaceProxy.SpaceNo}, spaceNo={spaceNo}.");
                return;
            }
            if (spaceProxy.MailBox.BindingGame != mailbox.BindingGame)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space creation report has a mismatched host, spaceId={mailbox.EntityId}, "
                    + $"expectedGame={spaceProxy.MailBox.BindingGame}, game={mailbox.BindingGame}.");
                return;
            }
            if (spaceProxy.IsReady)
            {
                return;
            }
            if (spaceProxy.CreationState != SpaceCreationState.Creating)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space creation report arrived in an invalid state, spaceId={mailbox.EntityId}, state={spaceProxy.CreationState}.");
                return;
            }

            spaceProxy.CreationState = SpaceCreationState.Created;
            DELogger.Info(nameof(SpaceStub),
                $"Persistent Space ready, spaceNo={spaceNo}, spaceId={mailbox.EntityId}, game={mailbox.BindingGame}.");
            TryCompleteInitialization();
        }

        private void TryCompleteInitialization()
        {
            // Local creation can confirm inline while the Stub is still initializing.
            if (State != SpaceStubState.WaitingForPersistentSpaceCreation)
            {
                return;
            }
            if (!AreSpacesReady)
            {
                return;
            }
            State = SpaceStubState.Ready;
            OnReady();
        }
    }
}
