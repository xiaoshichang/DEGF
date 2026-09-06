# AvatarEntity Migration Design

本文档描述 DEGF 中 `AvatarEntity` 从一个 Game 节点迁移到另一个 Game 节点，并在迁移完成后进入目标 `SpaceEntity` 的方案。文档中的“源 Game”表示迁移开始时承载 Avatar 的 Game，“目标 Game”表示目标 Space 所在的 Game，“OwnerGate”表示 Avatar 的 `EntityProxy.BindingGate` 指向的 Gate。

## 设计结论

迁移采用由源 Game 驱动、OwnerGate 充当路由屏障、目标 Game 预创建实体的分阶段提交协议。第一版固定以 `EntityMailBox` 指定目标 Space；迁移成功后的标准行为是进入该 Space，不支持业务自定义完成回调。

核心原则如下：

1. 源 Avatar 在 OwnerGate 冻结路由并确认之前保持正常工作。
2. OwnerGate 冻结后，不再把新的 Client RPC 或 Proxy RPC 发给源 Game，而是按到达顺序暂存。
3. 收到路由冻结确认后才序列化 Avatar，保证冻结前已经由 Gate 转发的 RPC 先于确认到达源 Game，迁移快照包含这些 RPC 造成的状态变化。
4. 目标 Game 先反序列化出不可寻址的候选 Avatar，验证目标 Space 后再激活候选 Avatar并进入目标 Space。
5. 只有目标 Avatar 激活并进入 Space 成功后，OwnerGate 才原子切换路由，并把迁移期间暂存的消息发往目标 Game或 Client。
6. OwnerGate 的路由切换是提交点。提交前失败可以回滚到源 Avatar；提交后所有动态寻址都只到目标 Avatar。
7. 每条迁移控制消息都携带全局唯一的 `MigrationId`，各阶段处理必须幂等；源 Game 会定时重发当前阶段的请求。

## 范围

第一版覆盖：

- Avatar 的 `EntitySerializeReason.Migrate` 序列化和反序列化。
- Avatar 当前 OwnerGate、客户端会话关联和动态 Proxy 路由的保持。
- Client 到 Avatar 的 RPC 在迁移期间不丢失。
- 其他服务器通过 Avatar Proxy 发往 Avatar 的 RPC 在迁移期间不丢失。
- Avatar 发往 Client 的 RPC 在路由冻结期间暂存，并在提交或回滚后按顺序发送。
- 目标 Space 的存在性验证、目标端进入 Space、回滚时退出目标 Space。
- 控制消息幂等和源端阶段超时重试。

第一版不覆盖：

- Game 或 Gate 进程崩溃后的跨进程持久化恢复。
- 已缓存消息落盘、缓存上限和集群级流量治理。
- 旧 `EntityMailBox` 的自动刷新。Avatar 的可迁移远端引用仍应使用 `EntityProxy`。
- 迁移中的业务 Tick 自动冻结。业务代码可以通过 `ImmigrationState` 判断并停止产生新的权威状态变更；框架保证经 OwnerGate 的 RPC 输入被冻结。
- 自定义迁移完成回调。成功后的动作固定为进入目标 Space。

## 公共 API

Avatar 使用目标 Space 的 Mailbox 发起迁移：

```csharp
bool accepted = avatar.Immigrate(targetSpace.MailBox);
```

同一 Game 内的场景关系由 Space 统一维护：

```csharp
space.Enter(avatar);
space.Leave(avatar);
IReadOnlyDictionary<Guid, AvatarEntity> avatars = space.Avatars;
```

进入和退出成功后分别调用 Avatar 的 `OnEnterSpace(SpaceEntity)` 与 `OnLeaveSpace(SpaceEntity)` 虚方法。重复进入同一个 Space 不会重复触发回调。

约束：

- `targetSpace` 必须有效，且实体 ID 是目标 Game 上已注册的 `SpaceEntity`。
- Avatar 必须已绑定有效的 `MailBox` 和 `Proxy`。
- 同一 Avatar 同一时间只允许一个迁移。
- 如果目标 Space 与 Avatar 已在同一个 Game，则不经过跨 Game 协议，直接进入该 Space。
- 返回 `true` 表示迁移请求已被框架接受，不代表迁移已经完成。调用方可检查 `ImmigrationState`。

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

任一提交前阶段 -> RollingBack -> Failed
```

`Completed` 和 `Failed` 都是稳定终态，允许再次调用 `Immigrate`。新一次迁移会生成新的 `MigrationId` 并重新进入 `FreezingRoute`。

## 控制协议

迁移控制协议复用现有 `ServerRpcNtf` 传输，不增加 Engine 的网络 Message ID。`ServerRpcPayload.TargetKind` 增加两个框架内部目标：

- `AvatarMigrationGame`：由 Gate 根据 `TargetServerId` 转发到指定 Game，并交给 Game 的迁移处理器。
- `AvatarMigrationGate`：由 OwnerGate 自己处理，不继续转发。

内部 `AvatarMigrationMessage` 使用带版本号的 UTF-8 JSON 负载，并封装在已有的二进制 `ServerRpcPayload` 中。该消息包含：

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

源 Game 收到 `RouteFrozen` 后先让 Avatar 退出旧 Space并调用 `OnLeaveSpace`，再生成迁移快照。退出回调产生的 Client RPC 会被已冻结的 OwnerGate 缓存，因此顺序早于目标端的进入回调。若迁移回滚，源 Avatar 会重新进入旧 Space并调用 `OnEnterSpace`。

### 2. 生成快照并准备目标 Avatar

源 Game 使用 `EntitySerializer.Serialize(avatar, EntitySerializeReason.Migrate)` 生成快照。`Migrate` 包含 `ServerOnly`、`ClientServer` 和 `AllClients` 属性以及组件属性。此时 `CurrentSpaceId` 已清空，目标场景由迁移请求中的 `TargetSpaceId` 指定。

所有 `ServerEntity` 构造函数都不执行注册。目标 Game 先构造候选 Avatar，再反序列化迁移快照；准备阶段的候选 Avatar 不注册到 `Entities` / `Avatars`，因此任何 RPC 都无法提前寻址到它。

目标端验证：

- 快照可反序列化。
- 快照中的 Avatar Guid 与协议 `AvatarId` 一致。
- 本地不存在同 Guid 的活动 Avatar。
- `TargetSpaceId` 对应本地已注册的 `SpaceEntity`。
- OwnerGate 信息有效。

验证通过后返回 `TargetPrepared`。

### 3. 激活并进入目标 Space

收到 `ActivateTarget` 后，目标 Game：

1. 再次确认目标 Space 仍存在。
2. 把候选 Avatar 注册到本地 `Entities` 和 `Avatars`。
3. 绑定迁移前相同的 OwnerGate Proxy。
4. 调用目标 Space 的 `Enter`，加入 `SpaceEntity.Avatars`、更新 `CurrentSpaceId` 并触发 `OnEnterSpace`。
5. 返回 `TargetActivated`。

进入 Space 期间发往 Client 的 RPC 会到达 OwnerGate；此时 Gate 仍处于冻结状态，因此这些 RPC 会进入缓存，不会提前暴露未提交的目标状态。

### 4. 提交路由并释放缓存

源 Game 只在收到 `TargetActivated` 后发送 `CommitRoute`。

OwnerGate 先按缓存顺序发送所有消息：

- Client RPC 和 Proxy RPC 发送到目标 Game。
- 源、目标 Game 发往 Client 的 Avatar RPC 发送到当前 Client 会话。

每条消息只有在底层发送函数确认接收后才从队列移除。发送失败时保留队首，等待源 Game 重试 `CommitRoute`，避免静默丢失。

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

源 Game先向目标 Game 发送幂等 `AbortTarget`。目标 Avatar 退出目标 Space、完成 `OnLeaveSpace` 后返回 `TargetAborted`；该确认与回调产生的 RPC 共用同一有序连接，因此源 Game 只在收到确认后向 OwnerGate 发送 `RollbackRoute`。OwnerGate 保持源路由，把缓存的输入 RPC 重新发往源 Game，把源端输出 RPC 发往 Client，并丢弃目标候选产生的推测性输出；全部成功后删除迁移上下文并返回 `RouteRolledBack`。源 Avatar 随后重新进入原 Space、触发 `OnEnterSpace` 并进入 `Failed`，可以再次发起迁移。

在收到 `RouteRolledBack` 前不能把源 Avatar 标记为恢复完成，否则 Gate 中仍可能存在尚未释放的消息。

## 重试和幂等

源 Game 为活动迁移维护一个定时器，超时后根据当前状态重发对应请求：

- `FreezingRoute` 重发 `FreezeRoute`。
- `PreparingTarget` 重发同一份快照的 `PrepareTarget`。
- `ActivatingTarget` 重发 `ActivateTarget`。
- `CommittingRoute` 重发 `CommitRoute`。
- `CompletingTarget` 重发 `CompleteTarget`。
- `RollingBack` 在收到 `TargetAborted` 前重发 `AbortTarget`，收到后重发 `RollbackRoute`。

所有接收端用 `MigrationId` 去重。Gate 额外记录最近一次提交或回滚结果，以便活动上下文已经删除后仍可回答重复请求。目标 Game 对重复 Prepare、Activate、Complete 和 Abort 返回与第一次一致的结果。

当前版本不设置自动放弃次数：控制消息暂时发送失败时迁移保持在当前安全状态并继续重试，从而优先保证消息不丢失。运维层可以通过日志和状态监控发现长时间未完成的迁移。

## Space 关系

`AvatarEntity.CurrentSpaceId` 是仅服务端属性。`SpaceEntity` 在本地使用 `IReadOnlyDictionary<Guid, AvatarEntity> Avatars` 对外暴露场景成员，不序列化整个成员集合。

- 本地进入 Space：旧 Space 先 `Leave`，新 Space 再 `Enter`，依次触发 `OnLeaveSpace`、`OnEnterSpace`。
- 跨 Game 迁移：路由冻结后源 Avatar 退出旧 Space，目标激活时进入目标 Space。
- 回滚目标：目标 Avatar 退出目标 Space并注销；Gate 回滚完成后源 Avatar 重新进入旧 Space。
- 提交完成：源 Avatar 已离开旧 Space，只需注销；目标 Avatar 保留在目标 Space。

短暂的准备/激活阶段可能在两个 Game 内存中同时存在同 Guid 的对象，但只有源对象或目标对象之一能通过 OwnerGate 路由被寻址；候选对象在准备阶段完全不注册。

## 实体注册生命周期

`ServerEntity`、`AvatarEntity`、`NpcEntity` 和 `SpaceEntity` 的构造函数只负责对象初始化，不访问全局 Game 运行时，也不自动注册。创建方应在 Guid、组件和反序列化数据准备完成后统一调用：

```csharp
gameRuntimeState.RegisterLocalEntity(entity);
```

正常登录创建 Avatar 和迁移目标激活都走该入口。`RegisterLocalEntity` 同时维护 `Entities`，并在对象是 Avatar 时维护 `Avatars`；重复 Guid 且对象实例不同会抛出异常。`ServerStubEntity` 由 Stub 创建流程放入 `StubInstances`，不混入普通实体表。

## 状态与可观测性

`AvatarEntity` 暴露只读状态：

- `ImmigrationState`
- `ImmigrationId`
- `ImmigrationTargetSpace`
- `CurrentSpaceId`

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
