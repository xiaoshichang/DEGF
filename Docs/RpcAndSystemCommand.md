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
Membership changes no longer trigger immediate reports. ServerEntity.UnInitializeEntity cancels Space statistics timers through its PreUnInitialize hook.
Game runtime teardown invokes the same lifecycle for Stubs, registered entities and unregistered migration candidates, deduplicating shared references.
Entity lifecycle state prevents repeated cleanup and reuse; components carry no lifecycle state or initialization flag. `PreInitialize` prepares entity fields,
constructs components and immediately prepares each component's fields without component dependencies. Snapshot restoration
follows this stage. `Initialize` processes dependencies after all components have been constructed and properties restored;
`PostInitialize` runs finalization after all component dependency initialization has finished.
Cleanup reverses this order: `PreUnInitialize` undoes finalization and stops activity, `UnInitialize` releases dependencies,
and `PostUnInitialize` clears fields and detaches components. Each stage runs across components in reverse assembly order
before the next stage begins. Component collections aggregate errors within the current phase;
`UnInitializeEntity` does not catch exceptions, so a failed phase interrupts subsequent phases.
`ServerEntity` and `ClientEntity` drive components from their corresponding lifecycle methods. Components can only be added
in entity `PreInitialize`; derived entities call the corresponding base methods.
Callers clean up partially initialized entities through the same lifecycle entry point.

Avatar allocation requests use SpaceStub.RequestSpaceByNo/RequestSpaceById RPCs; the reply invokes ServerAvatarEntity.OnRequestedSpaceReady through its Mailbox.
Space allocation and reporting do not use SystemCommand, and GameServerRuntimeState has no Space management partial or protocol.

## Client Space entry

ServerAvatarEntity and ClientAvatarEntity are separate types, as are ServerNpcEntity and ClientNpcEntity.
ServerEntity and ClientEntity manage their respective ServerEntityComponent and ClientEntityComponent collections.
ServerEntity and ClientEntity are independent inheritance roots, each owning its lifecycle, component collection and EntitySerializer.
Entity property markers are shared through DE.Share.Entities.
Transform components directly access their generated Position and Rotation properties using shared mathematics types.
EntityPropertySG generates specialized, strongly typed serialization and deserialization methods for each class. Each property is identified by
the pair of component-holding property-name hash and synchronized property-name hash, using UTF-8 FNV-1a 64-bit hashes with no incremental IDs.
The entity is the tree root; Guid is a direct leaf and [EntityComponent] holding properties are structural child nodes.
Inherited properties are flattened into their current node; different components may reuse leaf property names.
Components match by the entity property holding them, such as TransformCmpt; component class names and attachment order may differ across peers.
AddComponent(component) attaches a component without a protocol-name argument.
The SG emits TryGetComponentPropertyHash reference comparisons for [EntityComponent] properties, returning their unchanged property-name hash constants.
Derived routes fall back to base routes; components store only their owner entity, without a property-name string.
There is no cross-component property-name analyzer. Inheritance and property order may differ across peers;
matching component names, property names, compatible types and synchronization flags define the shared contract.
The generator checks sibling hash collisions across the inheritance chain, including component slots and leaves.
Business declarations, lifecycle and callbacks stay in handwritten files; typed accessors and codecs live in generated .g.cs files.
Entity property-tree version 4 rejects older snapshots; deploy matching protocol versions together.

After entry or migration commit, ServerAvatarEntity.OnAfterEnterSpace invokes ClientAvatarEntity.OnEnterSpace through an entity RPC.
Space membership changes are bracketed by OnBeforeEnterSpace/OnAfterEnterSpace and OnBeforeLeaveSpace/OnAfterLeaveSpace.
Migration activation runs only the before-entry phase; the after-entry phase follows the Gate route commit and AOI registration.
AOISpaceComponent.AddAOIAgent/RemoveAOIAgent manage agents without an Avatar-entry completion concept.
The arguments are the Space type name, serialized Space state, serialized Owner state, visible type names and visible entity states.
All entity states use EntitySerializer; string[] and byte[][] use the existing RPC binary reader, writer and generated dispatch.
Visibility deltas use the entity type name and serialized state directly. There is no separate Space snapshot codec.
Game sends the creation response before OnAvatarClientAttached starts entry, without a client-ready RPC.
Gate sends LoginRsp when handling the creation response and forwards subsequent Avatar RPCs directly.
Reconnection and old in-flight RPCs are outside the current scope; there is no login-stage RPC queue.
AuthSystem constructs the Avatar and completes login callbacks while processing LoginRsp before dispatching later RPCs.
An RPC before Avatar creation is a protocol-order error. ClientSpaceManager retains pending entities when no loader is installed.
Installing the loader starts the pending context once; AOI deltas continue updating it while waiting or loading.
ClientSpaceManager applies Owner state immediately when beginning the pending context, before subsequent patches arrive.

## Entity property-tree encoding

EntityPropertyAttribute, EntityComponentAttribute and EntityPropertyFlag are shared contracts in DE.Share/Entities (namespace DE.Share.Entities).
The SG identifies these shared markers and selects the server/client runtime from the declaring type's inheritance.
Server hash utilities, codecs, contexts and the entry point live in DE.Server/Entities/Serialization.
Their namespace remains DE.Server.Entities, and generated code continues to reference the same types.
All integer fields use big-endian encoding. Guid retains the existing .NET byte representation.
Profile selects the audience independently of Mode: Owner includes ClientServer and AllClients; Observer includes AllClients;
Migration includes server state, including Persistent fields, and excludes ClientOnly. Migration only supports Snapshot.

| Level | Fields | Encoding |
| --- | --- | --- |
| Packet header (24 bytes) | Version=4, Profile, Mode, BodyLength, BaseSequence, ThroughSequence | uint16, uint8, uint8, uint32, uint64, uint64 |
| Snapshot body / object payload | ChildCount, followed by entries | uint16, entries |
| Entry | PropertyNameHash, WireTag, PayloadLength, Payload | uint64, uint8, uint32, bytes |
| Patch body | ChangeCount, followed by change records | uint32, records |
| Patch change | Sequence, exactly one sparse root entry | uint64, entry |

WireTag 0 represents an object node. Each object on a Patch record's path has exactly one child.
A root leaf such as Guid has a one-element path; Position in TransformCmpt has the path
(hash("TransformCmpt"), hash("Position")). No empty component group or class-name key is used.
Names are case-sensitive UTF-8 FNV-1a 64-bit hashes, with offset 14695981039346656037 and prime 1099511628211,
using unchecked arithmetic. The SG emits hash constants. Inheritance and declaration order do not affect existing paths.
Renaming a holding property or leaf creates a new protocol identity; renaming a component class does not.

A Snapshot has equal BaseSequence and ThroughSequence. Sequences describe server changes and migration continuity; receivers store no baseline.
Owner / Observer readers skip unknown nodes by length, preserve missing optional fields, and require Guid.
Migration readers reject unknown fields and require every selected field. Known type/profile mismatches, duplicate siblings,
bad lengths, invalid values and trailing bytes reject the complete packet. Live snapshots also enforce entity identity; snapshots are applied in receive order without sequence-age checks.
The reader owns a copy of the input, validates the entire tree first, and then runs the generated typed reader again to assign backing fields.

Generated hash switches call statically selected codecs. Enums use their underlying primitive codecs;
nullable value types use a presence byte. Strings encode presence 0 for null or 1 plus the remaining UTF-8 bytes, distinguishing null and empty.
There are no runtime property descriptors, reflection, boxed values or getter/setter delegates.
Business declares EntityProperty on private __ fields in partial classes; handwritten properties and auto-properties produce a compiler diagnostic.
Arrays, List, Dictionary and other container EntityProperty values are rejected at compilation; RPC argument arrays remain supported.

ServerEntity.AutoSyncProperty controls dirty tracking and automatic synchronization, and defaults to false.
ServerAvatarEntity and ServerSpaceEntity override it to true. Dirty tracking, pending-log limits, recipient selection and the 50 ms synchronizer live in ServerEntity.AutoSyncProperties.cs.
The ordered journal is created lazily; GameRuntimeState only starts/stops the synchronizer and handles pending Space-entry notifications.
Generated server component setters pass their component instance to OwnerEntity; the generated route supplies the component path hash.
Entities with the flag disabled do not record changes. Paths still use holding-property names, and assignment records retain their original order.
Clients have no dirty log or Patch encoder; their setters only assign fields and invoke business callbacks.
EntityPropertyChange remains an immutable record also used transiently while applying received changes.
Each initialized server entity with AutoSyncProperty enabled owns one sequence across itself and its components. A successful local setter encodes the assigned value,
writes the backing field, appends an immutable record, and then invokes OnAfterXChange. OnBeforeXChange precedes this sequence.
Every actual assignment is retained, including repeated writes to one path; changes are never coalesced or grouped by component.
Setter reentry therefore follows actual assignment order. Unchanged assignments create no record.
Writes, captures and codecs use a simple single-threaded calling model without runtime thread binding or journal lifecycle guards.

Each synchronization sends all pending changes for one entity in one Patch per selected Profile, preserving assignment order.
There is no batch capture, prefix acknowledgement, or repeated draining loop. BaseSequence describes the first pending change's predecessor;
ThroughSequence is the entity's current sequence. The Game skips sends when a recipient's profile has no selected changes.
Patch flows from the server to clients only; the server has no Patch application entry point.
The client validates the complete packet and then applies all recognized records in wire order, relying on ordered transport.
It does not store a receive baseline, deduplicate retries, reject sequence gaps, or sort by sequence; a repeated Patch is applied again.
After every intended send succeeds, the entity clears its pending log without resetting its sequence; a failed send retains the log.
Remote application bypasses setters and callbacks; OnPropertiesApplied runs after commit with the ordered recognized records.
On an auto-synchronized server entity this hook may create tracked local changes; client hooks do not maintain dirty logs.
Snapshots report an empty change list. Locally serialized client snapshots have sequence zero.

Game scans registered entities every 50 ms and flushes only entities with AutoSyncProperty enabled; Stubs are not scanned.
Avatar changes go to the Owner and actual AOI observers; Space changes go to its current Avatar members.
Npc entities keep the flag disabled by default; Stubs never participate in automatic synchronization.
Before sending a new Owner entry snapshot, all pending changes are sent first.
Entry notification failures retain an Avatar/Space-bound retry item; retries never repeat the business OnAfterEnterSpace callback.
Migration drains changes before leaving, then freezes source writes and drains remaining Owner changes before taking its snapshot.
The target continues the source sequence. During rollback, retained changes precede the restored Owner snapshot.
The guarantee covers each entity's assignment sequence and an active ordered session; there is no cross-entity global order or reconnect replay log.

A scalar payload is limited to 1 MiB, an entity packet to 4 MiB, and depth to 16.
The pending log uses a conservative one-packet budget (4 MiB minus header/count), with at most 65535 records.
Capacity and encoded value checks occur before assignment; oversized pending data is rejected instead of split into batches.
Both KCP endpoints enqueue each application frame as an uninterrupted sequence of MSS-sized chunks; existing length framing reassembles it.
A partial enqueue failure closes the session so later frames cannot be parsed as a truncated predecessor.
The native engine and NativeBridge transport property packets as opaque byte arrays without ABI changes.

## Verification

Migration protocol tests cover the envelope round trip and mismatched command rejection.
RPC array tests cover generated entry calls and invalid array lengths; component tests cover typed ownership and lifecycle ordering.
Verify the framework solution, client solution and native engine after changing the shared transport or native API table.
