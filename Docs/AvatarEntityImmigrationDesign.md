# ServerAvatarEntity Migration Design

本文描述 Avatar 在 Game 节点之间的迁移。目标以 `EntityMailBox` 指定 `ServerSpaceEntity`；
OwnerGate 是 Avatar 的 `EntityProxy.BindingGate`。Game 之间始终通过 Gate 转发，不建立 Game 直连。

## 架构与参考版本

迁移采用 KBEngine 2.5.12 的 Real / Ghost 交接方式：源对象转成 Ghost 并转发后续调用，
目标恢复实体并进入空间，OwnerGate 会合迁移开始和结束通知后切换动态路由。
成功结果删除源对象；目标收到交接结束通知后恢复为可再次传送的 Real。
成功迁移另加 Client → Avatar 输入排空确认：OwnerGate 缓存 Start 后的新客户端输入，
收到源端排空确认后才完成路由交接并释放缓存。这是 DEGF 的保序扩展，不是 KBEngine 的既有保证。

KBEngine 的 Cellapp 对应 DEGF 的 Game，Baseapp 上持有 cell 引用的部分对应 OwnerGate 的路由职责。
DEGF 使用自身的属性树快照、实体生命周期和 SystemCommand 封装；下表说明角色及中间标志的具体依据，
不意味着复刻 KBEngine 的网络格式或 Python 运行时。

所有参考链接固定到官方仓库 `2.5.12` 标签。新增的迁移中间状态只有 `Ghost`、`Teleporting`，
Gate 保留 `TeleportStart` / `TeleportStop` 两个 KBE 会合标志；另记录 DEGF 专用的 `SourceInputDrained` 完成条件和客户端输入 FIFO，
不增加 Avatar 角色或目标入场阶段。

| DEGF 状态或转换 | KBEngine 2.5.12 函数及源码行 | 对应行为 |
| --- | --- | --- |
| `Real` | `Entity::changeToGhost`，[3961–3984][kbe-ghost] | 转换前检查 `isReal()`，源 Game 执行权威逻辑 |
| `Real -> Ghost` | `Entity::changeToGhost`，[3961–4003][kbe-ghost] | 保存目标 realCell、建立转发路由、序列化并停止源活动 |
| 目标 `Teleporting` | `Cellapp::reqTeleportToCellApp`，[1972–1984][kbe-target] | 恢复后设置 `ENTITY_FLAGS_TELEPORT_START` 并发送迁移 End |
| `Teleporting -> Real` | `Cellapp::reqTeleportToCellAppOver`，[2095–2112][kbe-over] | 收到 Over 后清除目标传送标志 |
| 失败 `Ghost -> Real` | `Cellapp::reqTeleportToCellAppCB`，[2042–2088][kbe-failure] | 通知原路由、恢复原实体、执行失败回调 |
| 成功删除源 Ghost | `Cellapp::reqTeleportToCellAppCB`，[2035–2039][kbe-delete] | 成功响应后销毁源对象 |
| Gate `TeleportStart` | `Entity::onMigrationCellappStart`，[1565–1584][kbe-start] | Start 先到时等待 End；End 已到则开始释放客户端输出 |
| Gate `TeleportStop` | `Entity::onMigrationCellappEnd`，[1587–1612][kbe-end] | End 先到时缓存客户端输出，等待 Start |
| Gate 会合结束 | `Entity::onMigrationCellappOver`，[1616–1629][kbe-gate-over] | 向目标发 Over，并更新 cell 引用；DEGF 成功路径额外等待源输入排空 |
| Gate 输出排空（标记已清） | Start [1575–1578][kbe-start]、输出回调 [1638–1641][kbe-buffer-over] | 保留输出转发器至会合结束 |
| 目标成功回执条件 `EntryCompleted` | `Cellapp::reqTeleportToCellApp`，[1998–2012][kbe-success] | 完整入场和业务回调正常返回后才设置 success 并回复；此字段只记录回执事实，不增加协议阶段 |

标志值定义见 [`entity_macro.h:266–267`][kbe-flags]。
`Teleporting` 表示目标已能执行权威业务，但路由交接尚未结束；它不是未激活的候选对象。
源 Ghost 不执行收到的实体 RPC，不写权威属性，也不参与空间自动同步。
本次不迁移或自动暂停 DETimer、外部异步任务及其回调；上述角色约束不等于迁移任意进程内运行资源。

## 组件与公共入口

`ServerAvatarEntity` 装配组件并提供 MailBox、Proxy 及 RPC 能力。
`ServerAvatarImmigateComponent` 保存 `ImmigrationState`、`ImmigrationId` 和 `ImmigrationTargetSpace`；
`ServerAvatarSpaceComponent` 管理 `CurrentSpace`、`SpaceId` / `SpaceNo`、空间请求及进出回调；
Transform 组件操作共享数学类型的 `Position`、`Rotation` 属性。

统一入口为：

```csharp
TeleportErrorCode result = gameRuntimeState.Teleport(avatar, targetSpace.MailBox);
if (result != TeleportErrorCode.Success)
{
    // 调用方根据业务决定记录错误、提示玩家或执行恢复。
}
```

空间入场分为 `bool CanEnter` 预校验和 `void Enter` 正式入场。
普通业务拒绝必须在 CanEnter 完成；Enter 不返回失败，业务回调或不变量异常直接向外传播。
本地传送离开旧 Space 后，先对目标执行 CanEnter，通过才执行 Enter；
目标预校验失败时，尝试原 Space 的 CanEnter 和 Enter 以恢复原场景。
`EnterTargetSpaceFailed` / `RestoreSourceSpaceFailed` 保留为预校验拒绝的错误码，不表示 Enter 返回 false。
跨 Game 的 `Success` 只表示迁移请求已发起，最终结果由异步控制消息处理。
只有 `Real` 可以发起下一次本地或跨 Game 传送。成功目标收到 `MigrationOver` 后，
或者失败源恢复为 `Real` 且恢复处理函数结束后，都可以再次发起传送，不需要业务重置迁移生命周期。
请求仍指向当前空间时返回错误，不重发入场快照。

状态路径为：

```text
源：Real -> Ghost -> 删除源对象             （成功）
源：Real -> Ghost -> Real                   （失败恢复）
目标：恢复并注册 -> Teleporting -> Real     （收到 MigrationOver）
```

`MigrationId`、Avatar、源 Game、目标 Game、OwnerGate 和目标 Space 共同标识一次交接。
处理控制消息时必须校验这些字段、消息来源及当前角色；不匹配的旧消息不能推进新一次迁移。

## 控制协议

迁移控制消息通过独立于实体 RPC 的 `SystemCommandNtf`（0x0002000F）传递。
外层 `SystemCommandPayload` 使用 `Kind = AvatarMigration`，以 `TargetServerId` 指定最终 Game 或 OwnerGate。
OwnerGate 处理发给自己的命令，其余命令转发给指定 Game。

内部 `AvatarMigrationMessage` 为版本 **3** 的 UTF-8 JSON，包含命令、MigrationId、AvatarId、TargetSpaceId、
源 Game、目标 Game、OwnerGate、客户端会话、成功标记和错误文本；`TeleportToGame` 携带完整迁移快照。
本次只升级迁移内部协议；NativeBridge API、外层 SystemCommand 和普通 RPC 格式不变。
集群各节点必须同时使用迁移协议 v3，旧版迁移消息拒绝处理。

| 命令 | 方向 | 作用及 KBEngine 对应入口 |
| --- | --- | --- |
| `MigrationStart` | 源 Game → OwnerGate | 开始会合；`onMigrationCellappStart` |
| `TeleportToGame` | 源 Game → Gate → 目标 Game | 恢复并进入目标空间；`reqTeleportToCellApp` |
| `TeleportResult` | 目标 Game → Gate → 源 Game | 删除源 Ghost 或恢复源 Real；`reqTeleportToCellAppCB` |
| `MigrationEnd` | 目标 Game / 失败源 Game → OwnerGate | 宣告最终关联；`onMigrationCellappEnd` |
| `MigrationOver` | OwnerGate → 最终 Game | 结束交接标志；`reqTeleportToCellAppOver` |
| `SourceInputDrained` | 源 Game → OwnerGate | DEGF 保序扩展：成功源已处理完旧客户端输入并完成 Ghost 清理 |

## 正常交接

### 源 Game

源 Game 在本线程内完成最终属性同步、离开旧 Space、冻结属性写入、生成快照和转为 Ghost。
离场前同步 Owner 及现有 AOI 观察者；离场回调后的最终属性同步给 Owner，随后生成 Migration 快照。
快照保留属性同步序号，目标从该序号继续；不保存或回放属性每次赋值的中间值。

源发送 `MigrationStart` 给 OwnerGate，不等待开始确认，再发送 `TeleportToGame` 到目标 Game。
KBEngine 同样先发送开始通知，再通过 `changeToGhost` 生成数据并发起传送，
见 [`teleportRefEntityCall` / `onTeleportRefEntityCall:3561–3637`][kbe-send]。
DEGF 同样先发 Start，在当前处理函数内完成快照准备和角色转换，再发 TeleportToGame，
不增加开始确认或独立目标激活阶段。

Gate 收到 Start 后，将 Client → Avatar 的新输入按接收顺序存入本次迁移的 `ClientInputs` 队列，直到交接完成。
Server → Avatar Proxy 仍按当前路由发送，不纳入客户端输入的严格保序契约。
源 Ghost 把调用转为指定目标 Game 的 Server MailBox RPC，经同一 Gate 转发给目标。
Ghost 转发走服务器 RPC 中继入口，不进入客户端输入队列，也不等待新输入放行。
`TeleportToGame` 先于后续 Ghost 转发进入这条有序连接；目标在处理后续调用前已经完成恢复和入场。
Ghost 角色转发而不执行调用的依据为 [`Cellapp::onEntityCall:1335–1355`][kbe-forward]。

### 目标 Game

目标在一个串行的 `TeleportToGame` 处理函数中：

1. 验证目标 Space、OwnerGate、会话、Avatar Guid，以及本地无同 Guid 的活动对象。
2. 构造 Avatar，通过 `InitializeEntity` 恢复身份、属性快照及组件状态。
3. 完成 CanEnter 预校验；新建或验证失败时，在发布 End 前清理失败对象，返回失败 TeleportResult。
4. 注册到 `Entities` / `Avatars`，绑定相同的 OwnerGate 和会话，设置 `Teleporting`。
5. 向 OwnerGate 发送 `MigrationEnd`。
6. 调用 void Enter，完整设置空间关系、加入成员表和 AOI、同步脏属性，再执行 `OnAfterEnterSpace`。
7. Enter 正常返回且 End 发送成功后，向源 Game 发送成功 `TeleportResult`。

创建和恢复对应 [`Cellapp::reqTeleportToCellApp:1938–2015`][kbe-create]；
正常路径与其 [1979–2012 行][kbe-enter-order] 一样，按 End、void 入场、成功回调响应的顺序执行。
所有普通失败在 End 发布前完成预校验。End 发布后，业务回调及不变量异常直接外抛，
不能转换为失败回滚或伪造成功响应；当前没有跨节点异常恢复协议。
若 End 的本地发送返回 false，仍在当前处理函数中完成 Enter，但不发成功 TeleportResult，
保留目标 Teleporting 和源 Ghost，不删除源对象；当前没有自动重试。
Game 单线程串行分发消息；即使 Gate 已处理 End 并发来 Over 或后续 RPC，
这些消息也只会在当前恢复和入场处理结束后执行，不增加独立 Prepare、Activate 或入场 ACK。

### OwnerGate 与源清理

Start 先到时设置 `TeleportStart`；End 到达后清除标志。
End 先到时设置 `TeleportStop`，等待 Start，并暂存目标发往 Client 的输出；Start 到达后清除会合标志。
成功路径还需等待 `SourceInputDrained` 才释放输出并完成交接；失败路径仍只会合 Start / End。
客户端输出缓冲仍仅用于 Start / End 逆序会合，与客户端输入 FIFO 分开。
依据为 [`onMigrationCellappStart` / `End` / `Over:1565–1629`][kbe-rendezvous]。

源收到成功 `TeleportResult` 后注销并清理源 Ghost，不等待额外的提交或目标完成确认。
随后向 OwnerGate 发送 `SourceInputDrained`；Gate 收齐 Start、成功 End 和排空确认后，
先释放逆序会合产生的客户端输出，再发送 `MigrationOver`，按 FIFO 发送缓存的客户端输入，最后切换关联并移除迁移上下文。
上下文移除前，新客户端输入始终入队；单线程分发保证队列释放过程中不会被后续直发输入插队。
注销按 Guid 和对象身份同时校验，避免旧消息误删新对象。
源删除后保留短期 Avatar 转发路由，供仍指向旧 Game 的在途 MailBox 调用使用；缓存期限为 5 秒，
对应 [`GhostManager::addRoute` / `getRoute` / `checkRoute:101–135`][kbe-route]。
实体不存在时先查这条路由再转发，依据为 [`Cellapp::onEntityCall:1310–1332`][kbe-missing]。
该缓存不改变远端持有的 MailBox；长期可迁移引用仍应使用 `EntityProxy`。

### 成功迁移的客户端输入顺序

不修改客户端 RPC 格式，不给每条 RPC 增加序号，也不在目标重排。
OwnerGate 收到 Start 后停止向源发送新客户端输入；此前已发出的旧输入与后续 `TeleportResult`
共用同一 Gate → 源 Game 会话。源 Game 顺序处理这些消息，因此收到成功结果时，旧输入已经在源执行或经 Ghost 转发。
Ghost 转发与随后的 `SourceInputDrained` 共用源 Game → OwnerGate 会话；Gate 收到确认时，旧输入已先行转发到目标。
这些转发与缓存新输入又共用 Gate → 目标 Game 会话，最终调用顺序为：

```text
Gate → 源 Game：Start 前已发出的旧输入 → TeleportResult
源 Game → Gate：旧输入的 Ghost 转发 → SourceInputDrained
Gate → 目标 Game：旧输入转发 → MigrationOver → 缓存新输入 → 后续正常输入
```

已在源执行的输入构成快照之前的前缀；目标依次执行旧输入后缀和新输入。
目标不必先执行完旧输入才通知 Gate，只需同一目标会话保持顺序发送和分发。
当前内网 RPC 与 SystemCommand 虽然分发入口独立，但按节点共用 `InnerNetwork.Send` 的同一会话，接收端同步串行分发。
开始缓存也覆盖 End 先到的会合情况；排空确认不能单独创建迁移上下文。
重复确认按完整迁移身份幂等处理，旧迁移确认不能释放新迁移的输入。

本契约仅保证成功跨 Game 迁移、客户端会话和内网会话连续有效、消息发送成功时，同一客户端 RPC 的调用顺序；
不扩展为多个服务器发送方的全局顺序、RPC 与属性同步的顺序，或异步业务任务的完成顺序。
缺少成功结果、源排空确认或发送失败时不绕过屏障；未发送的输入继续留在队列，当前没有自动重试或超时放行。

## 失败与消息语义

目标在恢复或 CanEnter 预校验失败时，在发布 End 前清理已创建的失败目标并返回失败 `TeleportResult`。
正式 Enter 的异常不进入这条普通拒绝路径。
源收到失败结果后，使用保存的 Migration 快照恢复源属性、变为 `Real`、解除写入冻结，
向 OwnerGate 发送失败 `MigrationEnd`，再在同一处理函数内调用 void Enter 重新进入原 Space，最终关联仍是源 Game。
源迁移上下文保留到恢复回调结束，阻止回调内同步重入 Teleport；该保护不增加协议中间状态。
恢复入场的业务回调及不变量异常同样外抛，不吞异常、不报告虚假恢复成功。
后续 RPC 在该处理函数正常返回后执行，届时源已完成重新入场。
源恢复参考 [`reqTeleportToCellAppCB:2042–2088`][kbe-failure]，
其中通过 `changeToReal` 还原实体，见 [`Entity::changeToReal:4007–4023`][kbe-real]。

正常交接通过 Ghost 处理旧客户端输入，通过 Gate 输入 FIFO 和源排空确认保护成功迁移的客户端输入顺序。
失败会合不等待 `SourceInputDrained`，缓存的新客户端输入在失败 Over 后按 FIFO 发送回源 Game。
本次不修复失败期间旧 RPC 的丢失或顺序：已经转发到目标的旧输入不进入 Gate 输入 FIFO，也不会被召回。
Gate 不冻结服务器发送方的输入，也不把输入和输出放入统一 FIFO；协议不提供跨连接全局顺序、应用级 RPC 确认或去重。
目标拒绝后，已经从 Ghost 转发到目标的在途 RPC 可能丢失：目标没有该实体及可用转发路由时记录错误并丢弃，
不会自动回送给源或在回滚后重放。这与 KBEngine 的目标拒绝及实体缺失分支一致，
见 [`reqTeleportToCellApp:1900–1935`][kbe-reject] 和 [`onEntityCall:1310–1332`][kbe-missing]。
因此不能承诺任意失败、断线或节点故障下所有已被 Gate 接受的 RPC 无损。

迁移控制发送失败会记录上下文并按当前可恢复的本地阶段处理；没有迁移重试定时器、自动重发、
迁移租约、持久化事务、跨进程容灾或客户端完成 ACK；源输入排空确认仅用于成功路径的顺序屏障。
连接失败不能仅凭本地发送返回值证明对端是否已执行。
不把这些情况扩展为新的迁移状态；排障应检查两端实体角色、Gate 会合标志和 MigrationId。

## 属性、Space 与生命周期

迁移快照使用 `EntitySerializer.SerializeSnapshot(avatar, EntitySerializationProfile.Migration)`。
实体属性树协议仍为 v4：Migration 选择服务端属性并要求完整、类型兼容的快照；
Guid 为根属性，组件节点按实体持有属性名的 UTF-8 FNV-1a 64 位哈希定位，组件类名不参与协议标识。
反序列化完整验证后直接写后备字段，不触发 `OnBeforeXChange` / `OnAfterXChange`。

构造函数不组装组件、不调用虚方法、不注册实体。
`PreInitialize` 构造并挂载组件、准备字段；随后恢复身份和属性；`Initialize` 处理依赖；`PostInitialize` 收尾。
目标完成初始化后调用 `RegisterLocalEntity`，同时维护 `Entities` / `Avatars`。
清理统一经 `UnInitializeEntity()` 的三个清理阶段执行，容忍部分初始化。

`CurrentSpace` 为当前空间引用，不进入快照；离场后为 null，持久化的 `SpaceId` 保留最后场景记录。
目标空间取自请求的 `TargetSpaceId`。源 Ghost 已离开旧空间，目标 `Teleporting` 正常进入目标空间，
无需等 Gate 会合后才注册 AOIAgent 或执行入场回调。
`Real` 和 `Teleporting` 的空间成员允许属性自动同步；Ghost 不能写权威属性或执行自动同步。
目标及失败源入场前恢复正常属性写入；在 `OnAfterEnterSpace` 前显式同步到 Owner 和 AOI 观察者。
`OnAfterEnterSpace` 检查脏属性为空，生成并发送一次入场快照，不隐式补同步、不处理通知失败或重试。

客户端入场继续使用空间 Observer、Owner 和可见实体 Observer 的 `EntitySerializer` 快照，
不新增迁移专用场景编解码器、客户端就绪 RPC 或资源加载确认。
AOI 不维护迁移完成状态，客户端仅在加载器设置前保留待加载场景及 AOI 变化。
正常登录仍先发创建响应，再绑定会话并入场，保证 LoginRsp 在 Avatar RPC 之前；不增加登录阶段 RPC 缓存。

[kbe-ghost]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/entity.cpp#L3961-L4003
[kbe-target]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L1972-L1984
[kbe-over]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L2095-L2112
[kbe-failure]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L2042-L2088
[kbe-delete]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L2035-L2039
[kbe-start]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/baseapp/entity.cpp#L1565-L1584
[kbe-end]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/baseapp/entity.cpp#L1587-L1612
[kbe-gate-over]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/baseapp/entity.cpp#L1616-L1629
[kbe-flags]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/lib/entitydef/entity_macro.h#L266-L267
[kbe-send]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/entity.cpp#L3561-L3637
[kbe-forward]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L1335-L1355
[kbe-create]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L1938-L2015
[kbe-enter-order]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L1979-L2012
[kbe-rendezvous]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/baseapp/entity.cpp#L1565-L1629
[kbe-route]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/ghost_manager.cpp#L101-L135
[kbe-missing]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L1310-L1332
[kbe-real]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/entity.cpp#L4007-L4023
[kbe-reject]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L1900-L1935
[kbe-buffer-over]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/baseapp/entity.cpp#L1638-L1641
[kbe-success]: https://github.com/kbengine/kbengine/blob/2.5.12/kbe/src/server/cellapp/cellapp.cpp#L1998-L2012
