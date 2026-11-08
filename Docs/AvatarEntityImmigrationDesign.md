# ServerAvatarEntity Migration Design

本文档描述 DEGF 中 `ServerAvatarEntity` 从一个 Game 节点迁移到另一个 Game 节点，并在迁移完成后进入目标 `ServerSpaceEntity` 的方案。文档中的“源 Game”表示迁移开始时承载 Avatar 的 Game，“目标 Game”表示目标 Space 所在的 Game，“OwnerGate”表示 Avatar 的 `EntityProxy.BindingGate` 指向的 Gate。

## 设计结论

迁移采用由源 Game 驱动、OwnerGate 充当路由屏障、目标 Game 预创建实体的分阶段提交协议。第一版固定以 `EntityMailBox` 指定目标 Space；迁移成功后的标准行为是进入该 Space，不支持业务自定义完成回调。

核心原则如下：

1. 源 Avatar 在 OwnerGate 冻结路由并确认之前保持正常工作。
2. OwnerGate 冻结后，不再把新的 Client RPC 或 Proxy RPC 发给源 Game，而是按到达顺序暂存。
3. 收到路由冻结确认后才序列化 Avatar，保证冻结前已经由 Gate 转发的 RPC 先于确认到达源 Game，迁移快照包含这些 RPC 造成的状态变化。
4. 目标 Game 先反序列化出不可寻址的候选 Avatar，验证目标 Space 后再激活候选 Avatar并进入目标 Space。
5. 只有目标 Avatar 激活并进入 Space 成功后，OwnerGate 才原子切换路由，并把迁移期间暂存的消息发往目标 Game或 Client。
6. OwnerGate 的路由切换是提交点。提交前失败可以回滚到源 Avatar；提交后所有动态寻址都只到目标 Avatar。
7. 每条迁移控制消息都携带全局唯一的 `MigrationId`，各阶段处理必须幂等，并且只能在协议规定的精确状态下推进。

## 范围

第一版覆盖：

- Avatar 的 `EntitySerializeReason.Migrate` 序列化和反序列化。
- Avatar 当前 OwnerGate、客户端会话关联和动态 Proxy 路由的保持。
- Client 到 Avatar 的 RPC 在迁移期间不丢失。
- 其他服务器通过 Avatar Proxy 发往 Avatar 的 RPC 在迁移期间不丢失。
- Avatar 发往 Client 的 RPC 在路由冻结期间暂存，并在提交或回滚后按顺序发送。
- 目标 Space 的存在性验证、目标端进入 Space、回滚时退出目标 Space。
- 控制消息的幂等处理。

第一版不覆盖：

- Game 或 Gate 进程崩溃后的跨进程持久化恢复。
- 已缓存消息落盘、缓存上限和集群级流量治理。
- 旧 `EntityMailBox` 的自动刷新。Avatar 的可迁移远端引用仍应使用 `EntityProxy`。
- 迁移中的业务 Tick 自动冻结。业务代码可以通过 `ImmigrationState` 判断并停止产生新的权威状态变更；框架保证经 OwnerGate 的 RPC 输入被冻结。
- 自定义迁移完成回调。成功后的动作固定为进入目标 Space。

## 公共 API

`GameServerRuntimeState` 提供统一的 Avatar 传送入口：

```csharp
TeleportErrorCode result = gameRuntimeState.Teleport(avatar, targetSpace.MailBox);
if (result != TeleportErrorCode.Success)
{
    // 调用方根据错误码和业务场景决定是否记录错误、提示玩家或执行恢复。
}
```

`Teleport` 根据目标 Space 的 `BindingGame` 自动选择流程：目标 Space 在当前 Game 时显式调用旧 Space 的 `Leave`，成功后再调用目标 Space 的 `Enter`；目标 Space 在其他 Game 时才启动 Avatar 迁移协议。Avatar 本身不再提供 `Immigrate` 入口。

同一 Game 内的场景关系由 Space 统一维护：

```csharp
space.Enter(avatar);
space.Leave(avatar);
IReadOnlyDictionary<Guid, ServerAvatarEntity> avatars = space.Avatars;
```

进入前后分别调用 Avatar 的 `OnBeforeEnterSpace(ServerSpaceEntity)`、`OnAfterEnterSpace(ServerSpaceEntity)`；
离开前后分别调用 `OnBeforeLeaveSpace(ServerSpaceEntity)`、`OnAfterLeaveSpace(ServerSpaceEntity)`。
Before 回调观察旧成员关系，After 回调观察更新后的成员和 AOI 状态。`ServerSpaceEntity.Enter` 不会隐式退出旧 Space；
Avatar 尚未离开旧 Space，或者重复进入同一个 Space 时，操作会记录 `Error` 并返回失败。

约束：

- `targetSpace` 必须有效，且实体 ID 是目标 Game 上已注册的 `ServerSpaceEntity`。
- Avatar 必须是当前 `GameServerRuntimeState` 中注册的同一对象实例。
- 本地传送不要求 Avatar 具有 Mailbox 或 Proxy；跨 Game 迁移要求 Avatar 具有有效的 Proxy，以便 OwnerGate 冻结和切换路由。
- 本地传送在 `Enter` 完成后返回 `TeleportErrorCode.Success`，此时 Avatar 已进入目标 Space。
- 跨 Game 传送只允许处于 `Idle` 的 Avatar；返回 `Success` 仅表示迁移已发起，调用方仍需等待异步结果或检查 `ImmigrationState`。
- `Teleport` 和同步迁移发起阶段通过错误码返回失败，不记录错误或通知客户端；调用方根据业务情况处理日志与提示。
- 目标入场失败会尝试恢复原场景；恢复成功返回 `EnterTargetSpaceFailed`，恢复也失败则返回 `RestoreSourceSpaceFailed`。
- 首个 `FreezeRoute` 发送失败返回 `FreezeRouteRequestFailed`，Avatar 进入 `Failed`，并移除源迁移上下文。
- 迁移中的 Avatar 禁止本地传送；`Completed` 或 `Failed` 等稳定状态仍可在当前 Game 内直接进入其他 Space。

迁移状态：

```text
Idle
  -> FreezingRoute
  -> Serializing
  -> PreparingTarget
  -> TargetPrepared
  -> ActivatingTarget
  -> TargetActivated
  -> CommittingRoute
  -> CompletingTarget
  -> Completed

FreezingRoute -> Failed
Serializing / PreparingTarget / ActivatingTarget / CommittingRoute
  -> RollingBack
  -> Failed

目标 Game 候选对象：
Idle -> TargetPrepared -> TargetActivated -> Completed
TargetPrepared / TargetActivated -> Failed
```

跨 Game 的 `Teleport` 最终进入内部 `BeginAvatarImmigration`，该流程只接受处于 `Idle` 的 Avatar。
`Completed` 和 `Failed` 都是稳定终态，不能再次启动跨 Game 迁移；非 Idle 状态返回 `ImmigrationStateNotIdle`，日志由调用方决定。
如果未来需要让终态 Avatar 再次跨 Game 迁移，必须新增显式且可审计的生命周期重置流程，不能由 `Teleport` 隐式重置状态。

状态转换采用白名单控制。每条协议响应只能在其对应的精确状态下处理；迁移期间 `MigrationId`、目标 Space 和目标 Game 必须保持不变。任何越级、回退、跨迁移编号或更换目标的状态修改都会记录 `Error` 并被拒绝。

## 控制协议

迁移控制协议使用与 RPC 并列的 `SystemCommandNtf`（0x0002000F），不占用 RPC 目标类型。
`SystemCommandPayload.Kind` 为 `AvatarMigration`，`TargetServerId` 明确指定最终 Game 或 OwnerGate，`CommandId` 标识迁移命令。
OwnerGate 处理发给自己的命令，其余迁移命令转发到指定 Game；Game 间仍通过 OwnerGate 通信。

内部 `AvatarMigrationMessage` 使用带版本号的 UTF-8 JSON 负载，封装在独立的二进制 `SystemCommandPayload` 中。该消息包含：

- 协议版本。
- 命令类型。
- `MigrationId`。
- `AvatarId`。
- `TargetSpaceId`。
- 源 Game、目标 Game、OwnerGate。
- 成功标记和错误文本。
- 仅 `PrepareTarget` 使用的 Avatar 迁移快照。

命令集合：

| 命令 | 方向 | 作用 |
| --- | --- | --- |
| `FreezeRoute` / `RouteFrozen` | 源 Game ↔ OwnerGate | 建立路由屏障，开始缓存 RPC |
| `PrepareTarget` / `TargetPrepared` | 源 Game ↔ 目标 Game | 反序列化候选 Avatar 并验证 Space |
| `ActivateTarget` / `TargetActivated` | 源 Game ↔ 目标 Game | 注册目标 Avatar 并进入目标 Space |
| `CommitRoute` / `RouteCommitted` | 源 Game ↔ OwnerGate | 切换动态路由并释放缓存 |
| `CompleteTarget` / `TargetCompleted` | 源 Game ↔ 目标 Game | 把目标 Avatar 标记为迁移完成并清理协议上下文 |
| `RollbackRoute` / `RouteRolledBack` | 源 Game ↔ OwnerGate | 提交前失败时恢复源路由并释放缓存 |
| `AbortTarget` / `TargetAborted` | 源 Game ↔ 目标 Game | 删除候选或尚未提交的目标 Avatar，并确认目标端回调已结束 |

## 正常时序

```text
Source Game                 OwnerGate                    Target Game
     |                          |                             |
     | FreezeRoute             |                             |
     |------------------------->| freeze + start queue       |
     | RouteFrozen             |                             |
     |<-------------------------|                             |
     | leave old Space         |                             |
     | serialize latest state  |                             |
     | PrepareTarget           | relay                       |
     |------------------------->|---------------------------->|
     |                          |       deserialize detached  |
     |                          |       validate target Space |
     | TargetPrepared          |<-----------------------------|
     |<-------------------------|                             |
     | ActivateTarget          | relay                       |
     |------------------------->|---------------------------->|
     |                          |       register + enter Space|
     | TargetActivated         |<-----------------------------|
     |<-------------------------|                             |
     | CommitRoute             |                             |
     |------------------------->| flush queued RPC ---------->|
     |                          | route := Target Game        |
     | RouteCommitted          |                             |
     |<-------------------------|                             |
     | unregister source       |                             |
     | CompleteTarget          | relay                       |
     |------------------------->|---------------------------->|
     | TargetCompleted         |<-----------------------------|
     |<-------------------------|                             |
```

### 1. 冻结动态路由

源 Avatar 创建 `MigrationId`，进入 `FreezingRoute`，向 OwnerGate 发送 `FreezeRoute`。

OwnerGate 只有在以下条件全部满足时才接受：

- Avatar 登录路由存在。
- 当前路由仍指向消息声明的源 Game。
- 没有另一个 MigrationId 正在迁移该 Avatar。

Gate 创建迁移队列后返回 `RouteFrozen`。同一个 `MigrationId` 的重复请求只重复返回确认，不重复创建上下文。

因为 Gate 到同一源 Game 使用同一有序内网连接，在 `FreezeRoute` 之前已被 Gate 接受并转发的 RPC 会先于 `RouteFrozen` 到达源 Game。源 Game 只在收到确认后生成快照。

源 Game 收到 `RouteFrozen` 后先让 Avatar 退出旧 Space并调用 `OnAfterLeaveSpace`，再生成迁移快照。退出回调产生的 Client RPC 会被已冻结的 OwnerGate 缓存，因此顺序早于目标端的进入回调。若迁移回滚，源 Avatar 会重新进入旧 Space并调用 `OnAfterEnterSpace`。

### 2. 生成快照并准备目标 Avatar

源 Game 使用 `EntitySerializer.Serialize(avatar, EntitySerializeReason.Migrate)` 生成快照。`Migrate` 包含 Entity 上的
`ServerOnly`、`ClientServer` 和 `AllClients` 属性，组件不声明 EntityProperty。
此时 `CurrentSpace` 已清空，目标场景由迁移请求中的 `TargetSpaceId` 指定。

所有 `ServerEntity` 构造函数都不执行注册。目标 Game 先构造候选 Avatar，再反序列化迁移快照；准备阶段的候选 Avatar 不注册到 `Entities` / `Avatars`，因此任何 RPC 都无法提前寻址到它。

目标端验证：

- 快照可反序列化。
- 快照中的 Avatar Guid 与协议 `AvatarId` 一致。
- 本地不存在同 Guid 的活动 Avatar。
- `TargetSpaceId` 对应本地已注册的 `ServerSpaceEntity`。
- OwnerGate 信息有效。

验证通过后返回 `TargetPrepared`。

### 3. 激活并进入目标 Space

收到 `ActivateTarget` 后，目标 Game：

1. 再次确认目标 Space 仍存在。
2. 把候选 Avatar 注册到本地 `Entities` 和 `Avatars`。
3. 绑定迁移前相同的 OwnerGate Proxy。
4. 调用目标 Space 的 `Enter`，加入 `ServerSpaceEntity.Avatars`、更新 `CurrentSpace`，此前执行 OnBeforeEnterSpace；OnAfterEnterSpace 留到路由提交后执行。
5. 返回 `TargetActivated`。

进入 Space 期间发往 Client 的 RPC 会到达 OwnerGate；此时 Gate 仍处于冻结状态，因此这些 RPC 会进入缓存，不会提前暴露未提交的目标状态。

### 4. 提交路由并释放缓存

源 Game 只在收到 `TargetActivated` 后发送 `CommitRoute`。

OwnerGate 先按缓存顺序发送所有消息：

- Client RPC 和 Proxy RPC 发送到目标 Game。
- 源、目标 Game 发往 Client 的 Avatar RPC 发送到当前 Client 会话。

每条消息只有在底层发送函数确认接收后才从队列移除。发送失败时保留队首并返回失败，避免静默丢失；当前版本不会由源 Game 定时重发 `CommitRoute`，需要通过日志和外部恢复流程处理。

缓存清空后，Gate 将 Avatar 的 `GameServerId` 原子更新为目标 Game，记录最近一次已提交的 `MigrationId`，删除活动迁移上下文，然后返回 `RouteCommitted`。后续 Client RPC 和 Proxy RPC 直接去目标 Game。

迁移期间若 Client 断开，Gate 会保留 Avatar 路由和迁移上下文，把断开通知作为 Proxy RPC 放入同一队列；已经无法投递给断开会话的 Avatar→Client RPC 会被清理。为避免替换登录破坏正在提交的路由，新的同账号登录在路由冻结期间返回冲突，客户端可在迁移结束后重试。迁移完成后的再次登录复用 Gate 记录的当前 Game，而不是重新按 AvatarId 选择初始 Game。

`RouteCommitted` 是不可回滚的提交点。源 Game 收到确认后从本地实体表删除已经退出旧 Space 的源 Avatar，随后要求目标 Game 完成协议上下文清理。

## RPC 无损语义

OwnerGate 在冻结期间使用一个统一 FIFO 队列记录以下消息：

1. Client → Avatar 的 Avatar RPC。
2. Server → Avatar Proxy 的 Server RPC。
3. Avatar → Client 的 Avatar RPC。

统一队列保留 Gate 观察到的到达顺序。它不承诺不同网络连接在到达 Gate 之前的全局因果顺序，但保证 Gate 接受后的消息不因迁移路由切换而进入不存在的 Avatar。

正常提交时，输入 RPC 发往目标 Avatar，输出 RPC 发往 Client。回滚时，输入 RPC 发回源 Avatar；源 Game 产生的输出 RPC 发往 Client，目标候选产生的推测性输出被丢弃，因为对应的目标激活已撤销。

“不丢失”的边界是消息已经被活着的 OwnerGate 接受。第一版队列位于内存，Gate 进程崩溃会丢失队列；需要严格跨进程容灾时应增加持久化日志、迁移租约和恢复协调器。

## 回滚

以下失败发生在提交点前时进入 `RollingBack`：

- 目标 Game 拒绝快照或目标 Space 不存在。
- 目标 Avatar 激活或进入 Space 失败。
- 协议字段与当前迁移上下文不一致。

源 Game先向目标 Game 发送幂等 `AbortTarget`。目标 Avatar 退出目标 Space、完成 `OnAfterLeaveSpace` 后返回 `TargetAborted`；该确认与回调产生的 RPC 共用同一有序连接，因此源 Game 只在收到确认后向 OwnerGate 发送 `RollbackRoute`。OwnerGate 保持源路由，把缓存的输入 RPC 重新发往源 Game，把源端输出 RPC 发往 Client，并丢弃目标候选产生的推测性输出；全部成功后删除迁移上下文并返回 `RouteRolledBack`。源 Avatar 随后重新进入原 Space、触发 `OnAfterEnterSpace` 并进入 `Failed`。`Failed` 是不可重入的终态，再次通过 `Teleport` 发起跨 Game 迁移会被当作错误拒绝。

在收到 `RouteRolledBack` 前不能把源 Avatar 标记为恢复完成，否则 Gate 中仍可能存在尚未释放的消息。

## 幂等和发送失败

当前版本不维护迁移重试定时器，也不会按状态自动重发控制命令。
首次 `FreezeRoute` 发送失败时，源迁移进入 `Failed` 并向 Teleport 调用方返回错误码，由调用方决定日志与提示。
后续异步阶段发送失败时记录 `Error`，保留当前状态和迁移上下文，交由外部监控与恢复流程处理。

接收端仍使用 `MigrationId` 处理重复消息。Gate 额外记录最近一次提交或回滚结果，以便活动上下文已经删除后仍可回答重复请求。目标 Game 只在迁移编号、协议字段和当前状态一致时接受重复的 Prepare、Activate、Complete 或 Abort；不一致的重入请求记录 `Error` 并返回失败。

## Space 关系

`ServerAvatarEntity.CurrentSpace` 表示玩家当前所在的场景，不序列化；未进入或离开场景后为 null，需要场景 ID 时直接读取其 `Guid`。
入场成功加入成员表后设置该引用，离场成功移除成员后立即置空，再移除 AOIAgent 并执行 OnAfterLeaveSpace 回调。
`SpaceId` 是持久化的最后场景记录，退出时仍保留。服务器在 `OnAvatarClientAttached` 中主动发起入场。
不等待客户端就绪 RPC，也不保存或迁移客户端就绪标志。入场和迁移提交成功后，`ServerAvatarEntity.OnAfterEnterSpace` 主动向客户端发起入场 RPC。
普通入场先加入成员表和 AOI 再执行 After 回调；迁移激活阶段仅加入成员表，路由提交后才加入 AOI、执行 After 回调。
AOI 组件仅通过 AddAOIAgent/RemoveAOIAgent 管理 Agent，不提供 Avatar 入场完成接口。
Game 成功发出创建响应后才执行 OnAvatarClientAttached，从而将入场 RPC 排在创建响应之后。
Gate 处理创建响应时发送 LoginRsp，后续 Avatar RPC 直接转发；当前不处理重连或在途旧 RPC，不增加登录阶段的 RPC 缓存。
客户端按有序连接处理 LoginRsp，完成 Avatar 创建及登录回调后才处理后续 RPC；若在 Avatar 创建前收到 RPC，则记录协议顺序错误，不缓存。
ClientSpaceManager 在资源加载器尚未设置时保留 Pending 场景，期间继续应用 AOI 增减；设置加载器后自动开始加载，每个上下文只启动一次。
场景分配结果指向当前场景，或迁移已 Completed 却再次请求跨 Game 入场时，记录错误、通知入场失败并返回，不重发场景或重置迁移状态。
RPC 携带空间类型名及其 `Broadcase` 序列化数据、OwnerSync 数据，以及可见实体的类型名和序列化数据数组。
客户端用 `ClientEntityFactory` 创建对应类型并通过 `EntitySerializer` 恢复状态，资源加载完成后应用 OwnerSync 数据。
不使用 `EntitySnapshot`、`SpaceEnterSnapshot` 或场景专用编解码器。
`ServerSpaceEntity` 使用 `IReadOnlyDictionary<Guid, ServerAvatarEntity> Avatars` 暴露本地成员，不序列化集合。

- 本地传送：由 `GameServerRuntimeState.Teleport` 显式调用旧 Space 的 `Leave`，成功后再调用新 Space 的 `Enter`，依次触发 `OnAfterLeaveSpace`、`OnAfterEnterSpace`。
- 跨 Game 迁移：路由冻结后源 Avatar 退出旧 Space，目标激活时进入目标 Space。
- 回滚目标：目标 Avatar 退出目标 Space并注销；Gate 回滚完成后源 Avatar 重新进入旧 Space。
- 提交完成：源 Avatar 已离开旧 Space，只需注销；目标 Avatar 保留在目标 Space。

短暂的准备/激活阶段可能在两个 Game 内存中同时存在同 Guid 的对象，但只有源对象或目标对象之一能通过 OwnerGate 路由被寻址；候选对象在准备阶段完全不注册。

## 实体注册生命周期

`ServerEntity`、`ServerAvatarEntity`、`ServerNpcEntity` 和 `ServerSpaceEntity` 的构造函数只组装组件和保留输入，不调用虚方法或自动注册。
创建方先调用 `InitializeEntity`：实体 `PreInitialize` 设置默认属性，恢复序列化数据后执行实体 `Initialize`，
再由 `ServerEntity` 的组件集合执行组件三阶段，最后执行实体 `PostInitialize`。
`ClientEntity` 使用对应的客户端组件集合；组件不声明 EntityProperty，所有同步字段由 Entity 持有。完成初始化后统一调用：

```csharp
gameRuntimeState.RegisterLocalEntity(entity);
```

正常登录创建 Avatar 和迁移目标激活都走该入口。`RegisterLocalEntity` 同时维护 `Entities`，并在对象是 Avatar 时维护 `Avatars`；重复 Guid 且对象实例不同会抛出异常。`ServerStubEntity` 由 Stub 创建流程放入 `StubInstances`，不混入普通实体表。

## 状态与可观测性

`ServerAvatarEntity` 暴露只读状态：

- `ImmigrationState`
- `ImmigrationId`
- `ImmigrationTargetSpace`
- `CurrentSpace?.Guid`

每次状态变化记录包含 AvatarId、MigrationId、源 Game、目标 Game 和目标 Space 的结构化文本日志。出现长时间停留时，首先检查：

1. `FreezingRoute`：源 Game 到 OwnerGate 的内网连接。
2. `PreparingTarget` / `ActivatingTarget`：OwnerGate 到目标 Game 的转发连接、目标 Space 是否仍存在。
3. `CommittingRoute`：Gate 缓存释放是否持续失败。
4. `CompletingTarget`：路由已提交，业务已在目标 Game 可用，只剩协议上下文确认清理。
5. `RollingBack`：Gate 正在把缓存恢复到源端，不应手工删除源 Avatar。

## 安全条件

实现必须始终满足：

1. Gate 未冻结时，源 Avatar 保持注册且可寻址。
2. Gate 冻结后到提交/回滚前，所有动态输入只进入 Gate 队列。
3. 目标 Avatar 未完成反序列化和 Space 验证前不可注册。
4. Gate 提交前不得删除源 Avatar。
5. Gate 提交前不得释放队列到目标 Avatar。
6. Gate 回滚完成前不得宣告源 Avatar恢复。
7. Gate 提交后不得再把 Proxy 或 Client RPC 发往源 Game。
8. 每次删除实体都按 Guid 与对象身份同时校验，避免旧迁移删除新实例。

## 后续演进

如果要支持进程崩溃恢复，建议在现有协议上增加：

- Gate 侧持久化路由版本和 RPC 队列。
- Game 侧持久化迁移事务记录与快照摘要。
- 带租约的集群迁移协调器。
- `RouteEpoch`，并在所有 Avatar RPC 中携带 epoch 以拒绝过期路由。
- 队列大小、字节数、等待时长指标以及磁盘溢写策略。
- 管理命令：查询、强制重试和基于持久化状态的人工恢复。
