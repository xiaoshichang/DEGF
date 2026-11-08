# Space 基本管理设计

## 目标和现状

贯通常驻 Space 创建、集群索引、Avatar 场景组件、按记录入场和客户端 Loading 入口。
AOI 第一版使用同一 Space 内全量可见，接口允许后续替换空间查询策略。

现有 SpaceStub 仅延时就绪；ServerSpaceEntity.Enter/Leave 已维护成员；GameServerRuntimeState.Teleport
已实现同 Game 传送和经 Gate 的跨 Game 迁移。客户端在 LoginRsp 后才构造本人 Avatar，尚无 ClientSpaceEntity 或 Loading 管理。
复用现有迁移协议、属性组件和序列化机制。

## 方案选择

1. 推荐 SpaceStub 统一规划和索引，各 Game 承载实例：数量来源唯一，跨节点查询直接返回 EntityMailBox，需要创建及确认消息。
2. 各 Game 自行按规则创建并上报：节点启动简单，但数量分配、幂等和集群就绪判断分散。
3. 全部 Space 放到 SpaceStub 所在 Game：实现较少，但不满足分布承载的目标。

## 配置和常驻实例

- SpaceNo 是配置行 Id；SpaceId 是实例 Entity Guid。现有表示配置编号的 SpaceID 应统一为 SpaceNo，并调整内部引用。
- SpaceDataRow 和 Data/Excel/SpaceData.xlsx 增加非负整数 PersistentCount；零表示不创建，负数使配置校验失败。
- SpaceStub 直接保存 SpaceEntityStubProxy，不再使用 SpaceRegistry；按 SpaceNo 和 PersistentCount 生成计划，预分配实例 Guid。
- 代理代表 Space 在 Stub 上的对象，保存 Planned、Creating、Created、Failed 创建状态和失败原因，以及人数统计。
- ISpaceCreationStrategy 选择宿主 Game，默认 RoundRobinSpaceCreationStrategy 按有序 Game 列表轮询分配；与入场分配策略独立。
- 保存 SpaceId、SpaceNo、EntityMailBox 和创建状态，并建立编号索引。
- 宿主使用发现的 SpaceType 构造和注册 Space，返回确认；跨 Game 消息经 Gate 转发。
- InitStub 在规划完成后同步遍历全部代理，一次性发出所有 Remote Create 请求，不使用 CreatePendingSpaces、轮询或重试定时器。
- 发送前设为 Creating，收到创建完成报告后设为 Created；发送或本地处理失败、发起请求抛异常时设为 Failed，并继续发出其他请求。
- 同一实例的重复创建幂等处理，编号或实体类型冲突明确失败；远程请求发出后保持 Creating，直到收到创建完成报告。
- 全部请求发出且全部实例确认后调用 OnReady；空计划直接就绪，创建失败或尚未确认时保持未就绪。
- 创建使用通用 CreateEntity SystemCommand，携带实体类型和构造参数。Game 只负责通用构造、初始化和注册。
- Space 完成初始化和注册后，通过 ServerStubCaller 向 SpaceStub.ReportSpaceCreated 报告自身 Mailbox 和场景编号。
- 重复的创建命令复用已有实例并重发创建确认，不重复启动统计定时器；创建确认与人数统计分别处理。
- Space 创建完成时上报一次统计，之后每 60 秒向 SpaceStub.ReportSpaceStat 上报 UTC Unix 毫秒时间和当前人数，不维护版本戳。
- Stub 只接受已确认实例的统计，校验编号和宿主，忽略时间早于已保存报告的统计；成员变化不再立即上报。
- SpaceStub 主文件保留 InitStub 等生命周期和分配入口，Init、Stat 两个 partial 子文件分别处理启动流程和负载统计。
- 分配请求调用 Stub RPC，结果通过 Avatar Mailbox RPC 返回。
- 不再使用 SpaceManagement 系统命令或 GameServerRuntimeState.Space 专用协议、计时器与分发逻辑。
- 实体通过 UnInitializeEntity 统一反初始化，UnInitialize 释放定时器和索引，随后逆序清理组件，不提供实体专用 Shutdown。
- 反初始化包含尚未注册的迁移候选 Avatar；重复清理幂等，初始化失败也会清理已启动的部分。
- 本次不增加节点故障自动补建或动态扩缩容。

## Avatar 场景组件

双端 Avatar 默认挂载 SpaceComponent，组件只保存私有的实体引用并提供操作，不描述序列化属性。
SpaceId、SpaceNo、Position、Rotation 的序列化字段直接定义在双端 Avatar 上，组件通过实体接口操作这些属性。
变换使用 DE.Share.Foundation.Mathematics 中的 Vector3 和 Quaternion，不依赖 Unity，默认位置为原点，旋转为单位四元数。
字段支持持久化、迁移及所需的本人和其他客户端同步。

Entity 和 Component 均提供 PreInitialize、Initialize、PostInitialize 三阶段。
构造函数只组装组件和保留输入；创建方在构造完成后调用 InitializeEntity，依次执行实体 PreInitialize、恢复属性、
实体 Initialize（按挂载顺序执行每个组件的三阶段）、实体 PostInitialize。完成后才能注册实体或序列化。
已有实体的快照更新只恢复属性，不重复初始化。属性生成器仅接受 Entity，序列化不再遍历组件属性。

SpaceId 保存最后一次进入的场景实例，作为持久化和登录恢复记录；退出时保留该值及变换。
CurrentSpace 是仅服务端的运行时引用，由 Space.Enter/Leave 设置和清空；需要场景 ID 时直接读取其 Guid，不另设 ID 属性。
不再保存客户端 Avatar 是否已构造完成的标志，也不在迁移控制消息中传递该状态。
客户端创建 Avatar 后发送 OnSpaceClientReady，直接触发场景恢复或当前场景快照重发。
进入成功后更新 SpaceId 记录，保持成员表与运行时引用一致。迁移回滚恢复源场景记录及变换。

## 创建、恢复和入场

自动恢复场景由客户端事件驱动：客户端完成 LoginRsp 反序列化及本人 Avatar 绑定后发送 OnSpaceClientReady，
服务器再按持久化记录发起场景解析，或为已在场景的 Avatar 重发快照。已有请求或迁移进行中时忽略重复通知。
业务主动调用入场接口不依赖就绪标志；迁移提交和本地入场完成后发送场景快照。
登录完成前客户端不处理 Avatar RPC，登录后的 OnSpaceClientReady 会重同步已进入的场景，或由进行中的入场流程完成同步。
不将现有 LoginStub 注册完成回调误认为客户端已经就绪。

### Avatar 的两类入场请求

Avatar 提供两个语义明确的入口，不能通过一个可空 SpaceId 参数隐式切换行为：

| 入口 | 请求参数 | SpaceStub 职责 | 失败行为 |
| --- | --- | --- | --- |
| RequestEnterSpaceByNo(int spaceNo) | 仅指定 SpaceNo，不指定 SpaceId | 收集该编号的可用实例及负载信息，通过分配策略选择实例 | 无合适候选时返回分配失败 |
| RequestEnterSpaceById(Guid spaceId) | 明确指定 SpaceId，无需调用方补充 SpaceNo | 精确查询实例的 SpaceNo 和 EntityMailBox，不调用分配策略 | 实例不存在或不可进入时失败，不自动换实例 |

按编号请求用于让 SpaceStub 分配合适实例；策略允许参考人数、容量等负载信息，首版仍使用已约定的随机实现，后续可替换为负载策略。
按实例请求用于明确进入某个 Space，不能退化为同编号重新分配。SpaceNo 以 Stub 的实例记录为准。
两类请求均带内部 RequestId，用于匹配异步响应、拒绝过期结果及幂等重试。
解析成功后统一交给 Teleport；请求受理与最终入场成功分别处理，只有实际进入成功才发起客户端 Loading。
新 Avatar 请求在上次迁移已 Completed 且需要再次跨 Game 时，显式记录并重置已完成的迁移生命周期。
Teleport 自身保留终态检查，不隐式重置；Failed 状态仍沿用既有协议的限制。

### 可替换的实例分配策略

仅按 SpaceNo 请求通过 ISpaceAllocationStrategy 选择，不在 Stub 中写死分配算法；按 SpaceId 请求绕过该策略。
策略接收分配上下文（SpaceNo、AvatarId）以及该编号下已经创建就绪的候选实例和负载描述，返回一个候选或明确的分配失败。
候选负载描述包含当前 Avatar 数量、配置容量等信息，由宿主 Space 上报到 Stub；后续分配策略可使用这些数据，无需改变 Avatar 请求入口。
SpaceStub 负责候选筛选及返回结果校验；策略不能返回其他编号、未就绪或不在候选集中的实例。

第一版提供 RandomSpaceAllocationStrategy，在候选中等概率随机选择；无候选时失败，只有一个候选时直接返回。
随机源可注入，便于验证选择边界及重试行为。默认使用随机策略，提供策略注册/替换入口，允许后续按 SpaceNo
覆盖默认策略，例如改为人数优先，不修改 Avatar 请求入口、Teleport 和客户端协议。

同一分配请求的重复处理复用已选结果，不因消息重试重新随机；新分配请求才重新执行策略。
已有有效 SpaceId 的恢复直接返回原实例，不经过随机选择。
这里的实例分配与启动时选择 Space 宿主 Game 是两个职责；宿主放置通过创建策略选择，默认轮询。

登录恢复使用上述两个入口：

1. 记录了 SpaceId 时，调用按 SpaceId 的入口恢复原实例，并保留记录的变换。
2. 指定实例已经失效时，本次请求明确失败；业务若要回退到同编号分配，必须另外发起按 SpaceNo 的新请求。
3. 仅有 SpaceNo 记录时，调用按 SpaceNo 的入口分配实例，并保留记录的变换。
4. 无记录的新 Avatar 选择编号最小且 PersistentCount 大于零的场景，调用按 SpaceNo 的入口，使用默认变换。
5. 指定编号无合适实例或没有默认场景时明确失败，不静默切换编号。

返回的 EntityMailBox 交给现有 Teleport；本地进入或跨 Game 迁移均复用现有流程。
重复客户端通知、过期解析响应须幂等处理。已在场景的重连客户端重发场景快照，不能重复 Enter。

## AOI 和客户端 RPC

Space 在构造时挂载 SpaceAoiComponent，提供实体进入、退出和指定观察者可见实体查询接口，面向 ServerEntity，允许后续接入 NPC。
默认组件返回同一 Space 中所有其他可见实体，排除自身及其他场景；可通过 SpaceInitializer.AoiComponent 传入派生组件。
构造函数不再调用虚方法创建 AOI，组件的生命周期由 Entity.Initialize 统一执行。

本人入场 RPC 包含：

- SpaceNo，用于选择静态资源。
- Space Entity 广播快照，用于构造 ClientSpaceEntity。
- 可见实体快照列表，每项包含 EntityId、实体实际类名和广播数据。

调整身份和场景组件的广播属性，确保客户端能重建，同时不泄露 ServerOnly 属性。
共享二进制快照编码校验数量、长度、重复 Id 和类型。
出现/消失通知同步给已有观察者，使全量 AOI 对双向成员变化生效。
迁移入场快照和目标场景其他观察者的出现通知统一在 Gate 提交路由、目标完成迁移后发布，
避免回滚产生候选 Avatar 残留。源场景的退出及回滚恢复须保持观察者成员关系一致。

## 客户端 Loading

新增 ClientSpaceEntity 和场景上下文，保存编号、Space Entity 和可见实体字典。
类型工厂按实体实际类名创建客户端实体，再应用广播快照；Demo 注册 DemoSpace 与 DemoAvatar 类型，由类型本身确定名称。
不维护独立的客户端类型标识或实体虚属性。框架的 ServerSpaceEntity 在客户端对应 ClientSpaceEntity。

Avatar 入场 RPC 校验数据后触发 Loading 请求，上下文包含三类入场数据和本人场景组件。
提供开始、完成、失败接口或事件，由业务加载器按 SpaceNo 加载静态资源。
加载完成后提交新 ClientSpaceEntity 并释放旧上下文；加载期间按序缓存出现/消失通知。
每次 Loading 使用序号，防止过期异步回调覆盖新场景。
Demo 接入该入口；当前表未定义静态资源路径，框架不硬编码 Unity 场景文件名。

## 验证

遵循用户补充要求，本次不新增测试用例；使用现有测试、构建及代码检查验证，并明确未实测的部分。

- 配置新列、零值和负值；多编号多 Game 数量和分布；全部请求同步发起、发送失败后继续、确认后就绪和重复确认。
- Entity 根属性的持久化、迁移和双类客户端序列化；退出保留记录，迁移回滚恢复变换。
- 两类 Avatar 请求：按编号调用策略、按 Id 精确查询且不调用策略；指定 Id 失效不自动改选；仅记录编号及新角色使用按编号入口。
- 负载描述上报和传入策略；默认随机策略可替换为基于负载的策略，不改变 Avatar 请求及传送流程。
- 原实例恢复、显式按编号重新分配、默认场景、无实例失败、本地与跨 Game 入场。
- 分配策略可替换且支持编号覆盖；随机选择仅返回合法候选，覆盖零/单/多个候选；同一请求重试复用结果，原实例恢复不随机。
- 客户端就绪前无 Loading、重复就绪和重连无重复成员。
- AOI 排除自身及其他场景、出现/消失和迁移回滚无残留。
- 客户端快照校验、类型构造、加载期间增量通知及过期异步回调。
- 构建 Server/Framework/Framework.sln 并运行相关 .NET 测试；构建可用的 Unity 客户端工程。
- 实际 Unity 资源加载及多进程集群运行与自动化验证分开报告。

## 默认约定

新 Avatar 选择编号最小的常驻场景，通过可替换策略分配该编号的具体实例；默认策略为随机选择。
位置为原点，旋转为单位四元数。
第一版 Loading 提供业务加载入口，不凭空增加资源路径或指定某个 Unity 场景。
