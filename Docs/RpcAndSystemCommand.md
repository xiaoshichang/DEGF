# RPC and SystemCommand

RPC addresses entity methods. SystemCommand addresses framework operations dispatched by Game or Gate RuntimeState.
They have separate message IDs, envelopes, native send APIs and managed receive entry points.

## RPC targets

`RpcTargetKind` contains exactly these kinds:

| Kind | Address | Delivery |
| --- | --- | --- |
| ServerStub | Hosting Game ID and Stub name | Resolve the local singleton and invoke its ServerRpc method. |
| ServerMailbox | Game ID and entity Guid | Invoke the exact entity on that Game. |
| ServerAvatarProxy | OwnerGate ID and Avatar Guid | OwnerGate resolves the current Game, buffering during migration. |
| ClientAvatarProxy | OwnerGate ID and Avatar Guid | OwnerGate resolves the current client session, buffering during migration. |

`ServerStubCaller`, `ServerMailboxCaller`, `AvatarRpcCaller.CallAvatarProxy` and `ServerAvatarEntity.CallClient` provide these paths.
Game-to-Game calls pass through a Gate. ServerMailbox is a fixed address; Avatar proxies follow the OwnerGate's dynamic route.
The Gate converts ClientAvatarProxy calls to the existing client Avatar RPC wire format, so the login/client protocol remains unchanged.
Client-originated Avatar RPCs are assigned the authenticated Avatar ID at the Gate and forwarded as ServerAvatarProxy calls.

The server RPC envelope version is 3. The target enum is independent of framework command categories.

## System commands

`SystemCommandNtf` is SS message ID `0x0002000F`, alongside `ServerRpcNtf` (`0x0002000B`).
`NativeAPI.SendSystemCommandToServer` and `ManagedAPI.HandleSystemCommandNative` form its native/managed boundary.
The native API table appends the new send pointer after CompleteGateAuthValidation; Engine and Framework must be rebuilt together.

The version-2 envelope uses big-endian integer fields, the existing Guid byte representation, and UTF-8 server IDs:

| Field | Bytes |
| --- | --- |
| Version / Kind | 2 / 2 |
| EntityId | 16 |
| TargetServerId byte length | 2 |
| CommandId / Data byte length | 4 / 4 |
| TargetServerId / Data | Variable |

The fixed header is 30 bytes. Data is bounded to 16 MiB; the final destination server is mandatory.
RuntimeState dispatches these framework categories:

- AvatarMigration: freeze, prepare, activate, commit, complete and rollback, retaining the existing migration state machine.
- CreateEntity: generic remote construction with entity assembly/type, public constructor signature and typed JSON arguments.
- CreateAvatar / CreateAvatarResponse: remote Avatar creation and its result, including the existing login session checks.

SystemCommand does not invoke entity RPC methods. Migration still freezes and buffers business RPCs; queued RPCs replay on the RPC channel.
Native legacy message hooks remain available, but framework migration and remote creation call sites use the SystemCommand channel.

## Remote entities and Space management

`RemoteEntityCaller.Create(targetGameId, entityType, entityId, constructorArguments)` sends a CreateEntity system command.
Its boolean result means the command was sent/handled, not that an asynchronous remote creation has completed.
Constructor arguments must be JSON-serializable data. The caller records the exact public constructor signature;
the target resolves entity types only from configured framework, gameplay and preloaded assemblies.
Arguments use System.Text.Json with IncludeFields enabled, including DTO structs such as SpaceInitializer.
Live component instances should be composed on the target; they are not remote constructor data.

The target constructs the entity, runs InitializeEntity and registers it with a valid Mailbox.
Identical retried commands reuse the original entity; a conflicting request for an occupied Guid is rejected.
Creation failure is logged on the target. The owning service determines readiness through the entity's own RPC report and timeout policy.

SpaceStub initiates persistent instances with:

```csharp
RemoteEntityCaller.Create(gameId, runtime.SpaceType, spaceId,
    new SpaceInitializer { SpaceNo = spaceNo });
```

SpaceStub directly owns the instance records; there is no separate SpaceRegistry.
Its main file keeps InitStub and allocation entry points, SpaceStub.Init.cs handles planning, remote creation and creation confirmations,
and SpaceStub.Stat.cs handles player statistics.
InitStub plans PersistentCount instances per SpaceNo, then sends CreateEntity commands to hosts selected by ISpaceCreationStrategy.
The default RoundRobinSpaceCreationStrategy distributes instances across the ordered Game list.
Host selection is separate from ISpaceAllocationStrategy, which chooses an existing instance for an Avatar.

After initialization and registration, GameServerRuntimeState explicitly completes Space creation.
ServerSpaceEntity starts its statistics timer and calls SpaceStub.ReportSpaceCreated with its Mailbox and SpaceNo.
SpaceEntityStubProxy represents each Space on the Stub and records Planned, Creating, Created or Failed, plus a creation error.
After planning all proxies, InitStub synchronously issues every creation request in one loop, without a pending-creation timer or retries.
It marks each proxy Creating before sending, so local creation can confirm inline; send/handling failures and exceptions mark only that proxy Failed.
One failure does not prevent requests for the other proxies. Readiness is checked after the loop and on subsequent confirmations.
SpaceStub.State tracks Created, Initializing, WaitingForPersistentSpaceCreation, Ready, Failed and UnInitialized.
Inline local confirmations update proxies during Initializing; only WaitingForPersistentSpaceCreation can advance to Ready.
After all requests are issued, a failed proxy leaves the Stub Failed; otherwise it waits for completion reports.
Only creation confirmations mark proxies Created; the Stub calls OnReady after all proxies are Created.
Remote creation remains asynchronous: a sent request stays Creating until its completion report arrives.
An identical repeated CreateEntity command still resends the confirmation and reuses the existing timer and entity.

ServerSpaceEntity sends an initial statistic and repeats every 60 seconds through SpaceStub.ReportSpaceStat.
Reports carry Mailbox, SpaceNo, UTC Unix milliseconds and current player count. SpaceEntityStubProxy stores PlayerCount and ReportTimeMilliseconds.
The Stub validates the planned identity and host, ignores reports older than the stored report time, and never uses a revision counter.
Membership changes no longer trigger immediate reports. Entity.UnInitializeEntity cancels Space statistics timers through its UnInitialize hook.
Game runtime teardown invokes the same lifecycle for Stubs, registered entities and unregistered migration candidates, deduplicating shared references.
Entity and component lifecycle states prevent repeated cleanup and reuse. Components clean up in reverse order and release their owner references.
Initialization failures clean up partially initialized entities; cleanup continues across entities/components before reporting aggregated errors.

Avatar allocation requests use SpaceStub.RequestSpaceByNo/RequestSpaceById RPCs; the reply invokes ServerAvatarEntity.OnRequestedSpaceReady through its Mailbox.
Space allocation and reporting do not use SystemCommand, and GameServerRuntimeState has no Space management partial or protocol.

## Client Space entry

ServerAvatarEntity and ClientAvatarEntity are separate types, as are ServerNpcEntity and ClientNpcEntity.
ServerEntity and ClientEntity manage their respective ServerEntityComponent and ClientEntityComponent collections.
Shared SpatialState and EntityComponentCollection objects provide common logic without sharing concrete components.

After entry or migration commit, ServerAvatarEntity.OnAfterEnterSpace invokes ClientAvatarEntity.OnEnterSpace through an entity RPC.
Space membership changes are bracketed by OnBeforeEnterSpace/OnAfterEnterSpace and OnBeforeLeaveSpace/OnAfterLeaveSpace.
Migration activation runs only the before-entry phase; the after-entry phase follows the Gate route commit and AOI registration.
AOISpaceComponent.AddAOIAgent/RemoveAOIAgent manage agents without an Avatar-entry completion concept.
The arguments are the Space type name, serialized Space state, serialized OwnerSync state, visible type names and visible entity states.
All entity states use EntitySerializer; string[] and byte[][] use the existing RPC binary reader, writer and generated dispatch.
Visibility deltas use the entity type name and serialized state directly. There is no separate Space snapshot codec.
Game sends the creation response before OnAvatarClientAttached starts entry, without a client-ready RPC.
Gate sends LoginRsp when handling the creation response and forwards subsequent Avatar RPCs directly.
Reconnection and old in-flight RPCs are outside the current scope; there is no login-stage RPC queue.
AuthSystem constructs the Avatar and completes login callbacks while processing LoginRsp before dispatching later RPCs.
An RPC before Avatar creation is a protocol-order error. ClientSpaceManager retains pending entities when no loader is installed.
Installing the loader starts the pending context once; AOI deltas continue updating it while waiting or loading.
ClientSpaceManager applies OwnerSync state when loading completes.

## Verification

Migration protocol tests cover the envelope round trip and mismatched command rejection.
RPC array tests cover generated entry calls and invalid array lengths; component tests cover typed ownership and lifecycle ordering.
Verify the framework solution, client solution and native engine after changing the shared transport or native API table.
