# DEGF 项目

本项目是基于分布式实体（Distributed Entity）设计的游戏开发框架。
客户端采用 Unity 和 C#；服务器通过 nethost/hostfxr 托管 .NET，C++ 实现底层引擎，C# 实现实体框架和游戏业务。
当前 Unity 版本为 `2022.3.62f3c1`，以 `Client/Demo/ProjectSettings/ProjectVersion.txt` 为准。
服务端运行时工程使用 .NET 10，共享工程和 Source Generator 使用 C# 9；生成器目标框架为 `netstandard2.0`。

## 架构设计

### 服务器集群

每个集群包含一个 GM 节点、若干 Game 节点和若干 Gate 节点，均运行同一个 `DEServer` 程序，通过配置和节点 ID 区分职责。

1. GM 控制集群启动、关闭、节点状态、统计、Stub 分配和 GM 指令分发。
2. Game 承载 C# 实体和业务逻辑。Game 之间不直连，跨 Game 通信必须经过 Gate 转发。
3. Gate 负责账号鉴权、客户端连接管理、消息转发，以及 Avatar 动态路由、迁移期间的客户端输入缓存和 Start / End 逆序时的客户端输出缓存。

集群启动先由 GM 向全部 Game / Gate 下发 Stub 分配表；Game 收到后连接全部 Gate，完成内网注册后上报 `GameNetworkReadyNtf`。
GM 等全部 Game 网络就绪、全部 Gate 确认分配表后，发送 `AllNodeReadyNtf` 启动 Stub 初始化，保证跨 Game 创建空间时路由已可用。
全部 Game 的 Stub 就绪并上报 `GameReadyNtf` 后，GM 才发送 `OpenGateNtf` 开放客户端连接。

### 分布式实体与通信

- `ServerEntity`、`ClientEntity` 分别作为双端实体继承根，以 `Guid` 唯一标识，各自实现生命周期、组件管理、属性描述和序列化。
- 实体统一通过 `UnInitializeEntity()` 结束生命周期，依次执行 `PreUnInitialize()`、`UnInitialize()`、`PostUnInitialize()`；组件清理由对应的 base 阶段逐阶段逆序执行，重复或重入清理无效。
- Avatar 分为 `ServerAvatarEntity`、`ClientAvatarEntity`，Npc 分为 `ServerNpcEntity`、`ClientNpcEntity`。同一角色的双端 Avatar 通过属性同步和 RPC 通信。
  Demo 双端分别使用 `DemoServerAvatarEntity`、`DemoClientAvatarEntity`；`DemoClientPlayerAvatarEntity` 继承后者，仅代表当前登录玩家。
  服务器从业务程序集反射发现服务端实体；客户端在鉴权初始化前扫描已注册的业务程序集，不再由 GameInstance 手动注册实体。
  客户端选择业务 Avatar 继承链的根类型作为普通 Avatar，再选择其唯一业务派生分支作为 PlayerAvatar；无派生类型时回退到普通 Avatar。
  服务器类型名通过将客户端类名中的 `Client` 替换为 `Server` 建立对应，PlayerAvatar 分支不注册为观察者实体。
  Space 使用 `DemoServerSpaceEntity`、`DemoClientSpaceEntity` 对应；空间加载回调仅由 PlayerAvatar 安装，反射创建的客户端业务实体需添加 Preserve。
- `ServerAvatarImmigateComponent` 封装 Avatar 的 `Real` / `Ghost` / `Teleporting` 迁移角色，依据 KBEngine 2.5.12；
  `ServerAvatarSpaceComponent` 封装空间出入、请求及 `SpaceId` / `SpaceNo`；Transform 组件封装位置和旋转。
  源迁移时转成 Ghost，把收到的 RPC 按指定目标 Game 的 MailBox 经 Gate 转发；目标恢复并预校验后按 MigrationEnd、void Enter、TeleportResult 顺序交接。
  OwnerGate 会合 MigrationStart / MigrationEnd；成功路径另等源 SourceInputDrained，再发送 MigrationOver、按 FIFO 释放客户端输入并切换关联。
  目标从 Teleporting 变为 Real；源收到成功 TeleportResult 后删除 Ghost，并经同一 OwnerGate 会话发送排空确认。
  失败源恢复快照及 Real，向 Gate 发送失败 MigrationEnd 后重新入原 Space；源上下文保留到恢复回调结束，阻止同步重入。
  成功或失败恢复为 Real 且本次处理结束后均可再次传送。
  Gate 从 Start 起缓存新客户端输入，旧输入继续经 Ghost 转发；复用 TeleportResult 排空源输入，成功迁移时保证同一客户端 RPC 调用保序。
  该保证依赖会话连续有效、顺序发送和串行分发，是 DEGF 扩展；不修改 KBE Avatar 角色，不覆盖异步任务完成顺序。
  Server → Avatar 输入不冻结，输入与输出分队列；失败路径只会合 Start / End 并向源释放新输入，已转发的旧 RPC 仍可能丢失或乱序。
  源对象删除后保留 5 秒转发路由处理旧 MailBox；不增加迁移重试定时器、自动重发、应用级 RPC ACK 或容灾协议。
  迁移内部协议 v3 使用 MigrationStart、TeleportToGame、TeleportResult、MigrationEnd、MigrationOver、SourceInputDrained，集群节点需同版；外层消息不变。
  各状态及 Gate 会合标志见 `Docs/AvatarEntityImmigrationDesign.md` 的 KBEngine 固定版本源码表。
- `EntityPropertyAttribute`、`EntityComponentAttribute` 和 `EntityPropertyFlag` 统一由 `DE.Share/Entities` 定义，命名空间为 `DE.Share.Entities`；实体是属性树根，`Guid` 是根属性，标注 `[EntityComponent]` 的组件持有属性构成子节点。
  组件通过 `AddComponent(component)` 装配；SG 根据 `[EntityComponent]` 持有属性生成组件实例到属性名哈希的引用路由，继承类继续调用基类路由。
  继承链中的属性平铺在当前节点，组件类名不参与协议标识，组件不保存持有属性名称。
  节点及叶属性均以属性名的 UTF-8 FNV-1a 64 位哈希定位，不分配递增 ID；不同组件允许同名属性，SG 检查同节点继承链内的哈希冲突。
  业务通过 partial 类型内私有 `__` 字段声明属性；生命周期和回调保留在手写文件，SG 在 `.g.cs` 中生成强类型访问、编解码和脏标记代码。
  不使用运行时 metadata、反射或读写委托；手写属性及自动属性上的 `[EntityProperty]` 会产生编译错误。
  暂不支持数组、List、Dictionary 等容器 EntityProperty；不支持的字段类型在编译期报错。
  服务器以 `AutoSyncProperty` 控制是否记录脏属性并自动同步，默认关闭；ServerAvatarEntity 开启，ServerSpaceEntity 关闭。
  自动同步标记、脏属性集合、写入检查和发送目标封装在 `ServerEntity.AutoSyncProperties.cs`；脏集合按需创建。
  `ServerSpaceEntity.AutoSyncProperties.cs` 维护每个空间的 50 ms 定时器，只驱动本空间成员 Avatar 的自动同步。
  定时器在空间 PostInitialize 启动、PreUnInitialize 停止；未入空间的实体不参与周期同步，Space 自身仍可生成快照。
  服务端编解码哈希、读写器、上下文、协议入口及应用通知位于 `DE.Server/Entities/Serialization`，保持 `DE.Server.Entities` 命名空间。
  共享标记不绑定双端运行时；SG 根据声明类型的继承关系选择对应运行时生成代码。
  GameRuntimeState 不维护全局属性同步定时器；组件通过生成代码直接通知 OwnerEntity。
  客户端不维护脏属性集合或生成 Patch，setter 仅负责赋值及业务回调。
- 属性树协议采用 v4，Profile（Owner、Observer、Migration）与 Mode（Snapshot、Patch）独立。
  Owner 包含 ClientServer 和 AllClients，Observer 包含 AllClients；Migration 包含服务端状态，拒绝未知或缺失的已选属性。
  Snapshot 使用递归 TLV；Owner / Observer 可跳过未知节点，缺失可选属性保留当前值，`Guid` 必须存在。
  Patch 由服务器生成并同步到客户端；setter 只标脏，按完整属性路径去重，发送时由 SG 读取当前字段，每个属性仅发送最终值。
  本地属性回调仍按实际赋值执行；网络不记录中间值，不保证跨属性变化顺序，序号仅用于诊断和迁移延续。
  接收端完整验证后直接写后备字段，不触发 `OnBeforeXChange` / `OnAfterXChange`，随后通过 `OnPropertiesApplied` 通知业务。
  客户端依赖有序传输按接收顺序应用 Patch，不维护同步基线，不去重或检查序号缺口；服务器没有接收 Patch 的入口。
  空间周期同步处理 `AutoSyncProperty` 开启且为 Real 或 Teleporting 的成员 Avatar，发给 Owner 及 AOI 观察者；Ghost 不同步，不扫描全局实体或 Stub。
  每次同步一次性发送全部脏属性当前值，发送后清空脏标记，暂不处理发送失败或重试；不分 batch，属性写入和编解码按单线程顺序调用。
  普通 RPC 与属性同步不保证相互顺序，不增加通用强制刷新或跨通道排序机制；入场流程在生成入场快照前显式同步脏属性。
  迁移快照前同步脏属性并冻结源实体属性写入，目标从快照序号继续；回滚后恢复写入。单值上限 1 MiB、包上限 4 MiB、脏属性按单个 4 MiB 包预算约束，最多 65535 个路径。
  组件类重命名不影响标识，继承逻辑由生成方法调用基类生成方法串接。
- `ServerAvatarEntity` 支持跨 Game 迁移；`ServerStubEntity` 表示集群范围内全局唯一服务，如 OnlineStub、MatchStub、SpaceStub。
- 初始化职责：`PreInitialize` 初始化实体属性和字段，构造组件并准备组件属性和字段，随后恢复身份及属性快照；`Initialize` 处理组件依赖初始化；`PostInitialize` 处理收尾逻辑。
- 清理职责：`PreUnInitialize` 逆序撤销收尾逻辑并停止活动；`UnInitialize` 逆序解除组件依赖；`PostUnInitialize` 逆序清理属性、字段及组件挂载。实体清理入口顺序执行三个阶段，不捕获异常；任一阶段异常直接向外传播并中断后续阶段。
- `ServerEntityComponent`、`ClientEntityComponent` 不维护 `LifecycleState` 或 `IsInitialized`，生命周期入口直接调用对应阶段，不做组件状态检查。
- 所有生命周期函数需有明确职责注释；派生类调用对应 base 方法，清理需容忍部分初始化状态。
- 组件只允许在实体 `PreInitialize` 中添加，添加时立即执行组件 `PreInitialize`。组件后续五个阶段由 `ServerEntity` / `ClientEntity` 对应生命周期函数逐阶段调用；派生类调用对应的 base 方法。
- `ServerEntity`、`ClientEntity` 分别管理 `ServerEntityComponent`、`ClientEntityComponent`，组件集合分别在双方模块内实现。
- `ServerAvatarTransformComponent`、`ClientAvatarTransformComponent` 直接操作 `Position` 和 `Rotation` 属性，使用共享数学类型。
- 普通远端实体通过 `EntityMailBox` 固定寻址；Avatar 通过绑定 OwnerGate 的 `EntityProxy` 动态寻址。
- 实体 RPC 由 `[ServerRpc]`、`[ClientRpc]` 显式暴露，调用和分发代码由 `DE.Share.EntityRpcSG` 生成。
- `SystemCommand` 用于迁移、远程实体创建等框架操作，与实体 RPC 使用独立的消息和分发入口。
- `SpaceStub` 直接管理空间实例，按 SpaceNo 规划常驻空间，通过创建策略选择 Game 并发起远程创建；空间注册后由创建命令处理流程报告 SpaceStub。
- `SpaceEntityStubProxy` 表示 Space 在 Stub 上的代理，保存创建状态及统计；InitStub 一次发出全部创建请求，不使用待创建轮询。
- `SpaceStub.Init.cs` 处理启动和创建确认，`SpaceStub.Stat.cs` 处理负载统计。空间每分钟上报 UTC 时间和当前人数，不维护版本戳。
- `ServerSpaceAvatarManageComponent` 管理空间 Avatar 成员和进出，`ServerSpaceStatisticsComponent` 在组件初始化时启动每分钟人数上报，并在清理时注销定时器；实体分别通过 `AvatarManageCmpt`、`StatisticsCmpt` 引用。
- 空间实体在服务端命名为 `ServerSpaceEntity`，客户端对应 `ClientSpaceEntity`；`ClientEntityFactory` 映射双端类型名。
- 服务器入场成功后调用客户端 `OnEnterSpace` RPC，传递空间、Owner 和可见实体的 `EntitySerializer` 数据，不定义场景专用快照或编解码器。
- Game 发出创建响应后才执行会话绑定回调并主动入场；Gate 按连接顺序发送 LoginRsp 和后续 Avatar RPC，确保客户端先创建 Avatar。
- 当前不处理客户端重连和旧会话的在途 RPC；Gate 不增加登录阶段的 RPC 缓存，服务器不通过重新绑定重发当前场景状态。
- 不等待客户端就绪 RPC；客户端仅在加载器设置前保留待加载场景和 AOI 变化，不缓存 Avatar 创建前的 RPC。
- 服务器 Avatar 使用 `OnBeforeEnterSpace` / `OnAfterEnterSpace`、`OnBeforeLeaveSpace` / `OnAfterLeaveSpace` 回调。
  普通及迁移入场均在设置空间属性、加入成员表和 AOI 后，调用 `OnAfterEnterSpace` 前强制同步脏属性到 Owner 和 AOI 观察者。
  `OnAfterEnterSpace` 检查没有待同步的脏属性，非空时抛出异常；检查通过后生成快照并发送一次入场通知，不额外同步或清空脏属性。
  暂不处理入场通知发送失败，不维护待通知队列或重试。
- 空间以 bool CanEnter 预校验普通失败，void Enter 正式入场；业务回调及不变量异常外抛，不转成普通失败或假成功。
  本地传送离场后目标 CanEnter 拒绝时，尝试原 Space 的 CanEnter 和 Enter 恢复；相关入场失败错误码表示预校验拒绝。
- 迁移目标在同一 TeleportToGame 处理函数内完成恢复、预校验、注册，依次发送 MigrationEnd、执行 void Enter、发送成功 TeleportResult；
  End 发布后的 Enter 异常外抛，不转换成失败回滚；End 本地发送返回 false 时仍执行 Enter，但不发成功结果，保留目标 Teleporting 和源 Ghost。
  目标收到 MigrationOver 前为 Teleporting，可执行业务及属性同步但不能再次传送；AOI 不维护迁移完成状态。
- `Teleport` 返回 `TeleportErrorCode`，由调用方记录错误或提示玩家；跨 Game 的 Success 仅表示迁移发起成功，后续结果由异步协议处理。
- AOI 模型位于 `DE.Server/AOI`；Avatar 使用 `Entities/Avatar/ServerAvatarAOIAgentComponent`，Npc 暂不装配 AOI 组件；空间组件位于 `Entities/Space`。公共状态、移动和离场逻辑封装在 `AOIAgent` 中；模型通过 `AgentEventEnter`、`AgentEventLeave` 报告可见性变化。

## 模块与依赖边界

### Client/Demo

Unity 工程包含框架、Demo 业务和双端共享源码。框架源码根目录为 `Client/Demo/Assets/DEFramework/Scripts`：

| 目录/程序集 | 职责 |
| --- | --- |
| `DE.Client.Foundation` | 应用架构、资源、日志、网络、UI、实体、鉴权和 GM 等客户端核心能力 |
| `DE.Client.Features` | 可复用的游戏功能扩展层，当前保留程序集说明 |
| `DE.Client.Editor/Editor` | 仅在 Unity Editor 编译的框架编辑器工具 |
| `DE.Share.Foundation` | 双端共享的基础类型、定点数和纯 C# 数学类型 |
| `DE.Share` | 双端共享的属性标记、消息和 RPC 定义 |
| `DE.Share.DataFramework` | 配置表描述、加载、缓存、Provider 和 DataRuntime |
| `DE.Share.Data` | 框架配置行定义，如 Space、Npc |

- Demo 源码位于 `Client/Demo/Assets/Demo/Scripts`，包含 `Demo.Client` 和 `Demo.Share.Data`。
- Foundation 不依赖 Features 或 Demo；Features 可依赖 Foundation；框架层不引用 Demo 业务或 Demo 数据类型。
- Demo 通过 `ApplicationRoot.RegisterGameplay` 注册业务程序集和 `GameInstance` 工厂，框架不直接创建 Demo 类型。
- 程序集边界和引用由 `.asmdef` 管理。两个共享数据程序集关闭自动引用，使用方应显式添加引用。
- `Assets/csc.rsp` 定义 `DE_CLIENT`；服务端通过 `Server/Framework/Directory.Build.props` 统一定义 `DE_SERVER`。
- `Demo.sln` 和客户端 `.csproj` 由 Unity 生成，不纳入版本控制；调整程序集后先在 Unity 刷新资源并重新生成项目文件。
- 移动资源时保留 `.meta` GUID；新增资源需包含相应 `.meta`，避免破坏场景、脚本和程序集引用。

### Server/Engine

- C++17 CMake 工程，目标程序为 `DEServer`。
- `src` 包含节点、网络、HTTP/Telnet、配置、日志、定时器和托管运行时桥接；`3rd` 管理本地第三方依赖。
- C++/C# 桥接两端分别位于 `src/managed` 和 `Server/Framework/DE.Server/NativeBridge`。
- `build_engine.bat` 默认构建 Debug，输出目录为 `build/engine`；Windows 可执行文件位于 `build/engine/src/Debug/DEServer.exe`。
- `build_test.bat` 在独立的 `build/test` 目录构建 `engine_smoke_tests`，并运行 CTest。

### Server/Framework

- `Framework.sln` 包含服务端框架、Demo、共享源码链接工程、生成器和测试工程。
- `DE.Server.Foundation` 为服务端基础层；`DE.Server` 实现实体框架、数据库、鉴权和 NativeBridge；`Demo.Server` 实现 Demo 业务。
- `DE.Share.Foundation`、`DE.Share`、`DE.Share.DataFramework`、`DE.Share.Data` 链接 Unity 框架目录中的共享源码。
- `Demo.Share.Data` 链接 Unity Demo 目录中的共享数据源码。修改共享类型时直接修改这些源文件，不维护服务端副本。
- `DE.Share.EntityPropertySG`、`DE.Share.EntityRpcSG`、`DE.Share.DataTableSG` 分别生成实体属性访问及回调、RPC 和配置表代码。
  属性生成器同时生成双端各类的强类型序列化和反序列化方法，不进行跨组件属性重名检查。
- `DE.Share.Tests` 保留共享层测试工程；当前阶段仅做编译验证，不新增或运行测试。

### 数据、配置与启动脚本

- `Data/Excel` 存放双端配置表。加载与缓存机制属于 `DE.Share.DataFramework`，框架与 Demo 的行定义分别放在各自数据工程中。
- 数据工程可依赖 DataFramework，不依赖实体或业务逻辑层；共享代码需兼容 Unity，不引入服务端专用 API 或 Unity 引擎依赖。
- `DataRuntime` 按单线程使用；后台预热必须完成后再访问数据或关闭运行时，每张表的 Provider 由工厂独立创建。
- `Server/Config/local-dev.json` 为本地集群配置，包含节点、日志、KCP、托管程序集、预加载程序集、数据目录和 MongoDB 设置。
- 配置中的托管 DLL 和 `dataRoot` 相对路径以配置文件所在目录为基准。默认托管 DLL 指向 Debug/net10.0 输出。
- `database.enabled` 默认 `true`，托管运行时启动时连接并检查 MongoDB，可用 `DEGF_MONGODB_URI` 覆盖连接字符串。
  本地配置暂设为 `false`，用于无 DB 联调：跳过数据库参数校验、初始化和账号存在性查询，仍保留账号、密码非空检查与 Gate 路由校验。
  关闭时 `DatabaseService` 为 null，各节点启动日志明确提示无 DB 开发模式；不能验证数据库读写或账号存在性，恢复 DB 时将其设回 `true`。
- `Server/Scripts/start_server_cluster.py`、`kill_server_cluster.py` 实现集群启动与强杀，当前提供 `.bat` 包装入口。
- 启动脚本支持 `--config` 和 `--exe`，默认使用本地配置和 Debug 引擎产物；集群日志默认写入 `Server/Scripts/logs`。

## 构建与验证

以下命令均从仓库根目录执行；默认使用 Debug。C++ 构建需要 CMake、MSVC 和 Windows SDK，C# 构建需要 .NET 10 SDK。

### C# 服务端

```powershell
dotnet build Server/Framework/Framework.sln -c Debug
```

也可使用 `Server/Framework/build_framework.bat Debug`。局部修改可先构建受影响的 `.csproj`。

### C++ 引擎

```powershell
cmd /d /c "call Server\Engine\build_engine.bat Debug <nul"
cmd /d /c "call Server\Engine\build_test.bat Debug <nul"
```

批处理末尾有 `pause`，自动执行时用 `<nul` 避免等待键盘输入。需要 Release 时将参数改为 `Release`。

### Unity 与生成器

- 客户端 C# 编译检查可使用 Unity 生成后的 `dotnet build Client/Demo/Demo.sln -c Debug`。
- 启动注册验证在 Unity Test Runner 的 EditMode 中运行 `Demo.Client.Tests.GameplayRegistrationTests`。
- 修改生成器时同步更新 `Client/Demo/Assets/Plugins` 中对应的分析器 DLL，保留现有 `.meta` 配置。
- 修改 DataTableSG 或升级 ExcelDataReader 后，可执行以下同步脚本：

```powershell
./Server/Framework/sync_data_plugins.ps1 -Configuration Debug
```

该脚本只同步 `DE.Share.DataTableSG.dll` 和 `ExcelDataReader.dll`，其他生成器需单独同步。
不要手改生成的 C# 文件或 Unity 自动生成的工程文件。
共享源码修改需验证客户端与服务端；NativeBridge API 表、结构布局或消息协议修改需同步检查 C++ 和 C# 两端，并重新构建。
当前阶段仅按改动范围编译验证，不新增或运行测试；仅文档修改无需编译。

## 编码规范

1. 左花括号另起一行。
2. 目录名和文件名禁止中文。
3. 类名和函数名采用大写驼峰，修改已有类型时保持周边命名风格。
4. 单行最大长度为 160 个字符。
5. Entity 和 Component 的 `PreInitialize`、`Initialize`、`PostInitialize`、`PreUnInitialize`、`UnInitialize`、`PostUnInitialize` 六个核心生命周期函数必须放在类型主文件中。
   不能放在功能拆分的 partial 文件中；功能逻辑可按现有目录结构拆分。
6. 初始化和清理遵循依赖顺序：先清理实体和节点，再关闭数据服务，最后注销程序集解析与异常处理、重置身份信息。
7. 异常处理放在需要恢复、补充上下文或跨语言边界的位置，避免给普通顺序逻辑添加冗余的 try 保护。

## 工作文档约定

- plan（实施计划）和 spec（任务规格）仅在当前会话中使用，不写入仓库、不提交、不存档。
- 不因技能或工作流程要求创建 plan / spec 文件；需要长期保留的架构、契约和使用说明，直接更新现有正式文档。

## 参考文档

- `Client/README.md`：客户端程序集职责与依赖。
- `Docs/ClientStartup.md`：业务注册与启动顺序。
- `Docs/DataFrameworkDesign.md`、`Docs/DataProjects.md`：数据框架契约与配置表接入。
- `Docs/RpcDesign.md`、`Docs/RpcAndSystemCommand.md`：RPC 与框架命令通道。
- `Docs/AvatarEntityImmigrationDesign.md`：Avatar 传送与跨 Game 迁移。
- `Docs/SharedMathematics.md`：共享数学类型与运算约定。

设计文档和历史计划可能保留旧命名或旧目录；涉及当前行为时结合源码、`.csproj` 和 `.asmdef` 核实。
