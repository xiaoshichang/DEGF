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
3. Gate 负责账号鉴权、客户端连接管理、消息转发，以及 Avatar 的动态路由和迁移期间的消息缓存。

### 分布式实体与通信

- `Entity` 以 `Guid` 唯一标识，提供属性描述和序列化能力；客户端和服务器分别派生 `ClientEntity`、`ServerEntity`。
- 实体统一通过 `UnInitializeEntity()` 结束生命周期，由 `UnInitialize()` 清理自身资源，再按逆序清理组件；重复调用不重复执行。
- 当前可跨 Game 迁移的实体是 `AvatarEntity`。同一角色的客户端与服务器 Avatar 通过属性同步和 RPC 通信。
- `NpcEntity`、`ServerSpaceEntity` 等属于不可迁移实体；`ServerStubEntity` 表示集群范围内全局唯一服务，如 OnlineStub、MatchStub、SpaceStub。
- 普通远端实体通过 `EntityMailBox` 固定寻址；Avatar 通过绑定 OwnerGate 的 `EntityProxy` 动态寻址。
- 实体 RPC 由 `[ServerRpc]`、`[ClientRpc]` 显式暴露，调用和分发代码由 `DE.Share.EntityRpcSG` 生成。
- `SystemCommand` 用于迁移、远程实体创建等框架操作，与实体 RPC 使用独立的消息和分发入口。
- `SpaceStub` 直接管理空间实例，按 SpaceNo 规划常驻空间，通过创建策略选择 Game 并发起远程创建；空间完成创建后主动报告。
- `SpaceEntityStubProxy` 表示 Space 在 Stub 上的代理，保存创建状态及统计；InitStub 一次发出全部创建请求，不使用待创建轮询。
- `SpaceStub.Init.cs` 处理启动和创建确认，`SpaceStub.Stat.cs` 处理负载统计。空间每分钟上报 UTC 时间和当前人数，不维护版本戳。
- 空间实体在服务端命名为 `ServerSpaceEntity`，客户端对应 `ClientSpaceEntity`；快照工厂负责映射两端类型名。

## 模块与依赖边界

### Client/Demo

Unity 工程包含框架、Demo 业务和双端共享源码。框架源码根目录为 `Client/Demo/Assets/DEFramework/Scripts`：

| 目录/程序集 | 职责 |
| --- | --- |
| `DE.Client.Foundation` | 应用架构、资源、日志、网络、UI、实体、鉴权和 GM 等客户端核心能力 |
| `DE.Client.Features` | 可复用的游戏功能扩展层，当前保留程序集说明 |
| `DE.Client.Editor/Editor` | 仅在 Unity Editor 编译的框架编辑器工具 |
| `DE.Share.Foundation` | 双端共享的基础类型、定点数和纯 C# 数学类型 |
| `DE.Share` | 双端共享的实体、组件、属性序列化和 RPC 定义 |
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
- `DE.Share.EntityPropertySG`、`DE.Share.EntityRpcSG`、`DE.Share.DataTableSG` 分别生成属性序列化、RPC 和配置表代码。
- `DE.Share.Tests`、`DE.Server.Tests` 为 xUnit 测试工程。

### 数据、配置与启动脚本

- `Data/Excel` 存放双端配置表。加载与缓存机制属于 `DE.Share.DataFramework`，框架与 Demo 的行定义分别放在各自数据工程中。
- 数据工程可依赖 DataFramework，不依赖实体或业务逻辑层；共享代码需兼容 Unity，不引入服务端专用 API 或 Unity 引擎依赖。
- `DataRuntime` 按单线程使用；后台预热必须完成后再访问数据或关闭运行时，每张表的 Provider 由工厂独立创建。
- `Server/Config/local-dev.json` 为本地集群配置，包含节点、日志、KCP、托管程序集、预加载程序集、数据目录和 MongoDB 设置。
- 配置中的托管 DLL 和 `dataRoot` 相对路径以配置文件所在目录为基准。默认托管 DLL 指向 Debug/net10.0 输出。
- 托管运行时启动时连接并检查 MongoDB；本地启动需可用的数据库，可用 `DEGF_MONGODB_URI` 覆盖连接字符串。
- `Server/Scripts/start_server_cluster.py`、`kill_server_cluster.py` 实现集群启动与强杀，当前提供 `.bat` 包装入口。
- 启动脚本支持 `--config` 和 `--exe`，默认使用本地配置和 Debug 引擎产物；集群日志默认写入 `Server/Scripts/logs`。

## 构建与验证

以下命令均从仓库根目录执行；默认使用 Debug。C++ 构建需要 CMake、MSVC 和 Windows SDK，C# 构建需要 .NET 10 SDK。

### C# 服务端

```powershell
dotnet build Server/Framework/Framework.sln -c Debug
dotnet test Server/Framework/Framework.sln -c Debug --no-build --no-restore
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
按改动范围运行现有测试；仅文档修改无需编译。

## 编码规范

1. 左花括号另起一行。
2. 目录名和文件名禁止中文。
3. 类名和函数名采用大写驼峰，修改已有类型时保持周边命名风格。
4. 单行最大长度为 160 个字符。
5. Entity 和 Component 的 `PreInitialize`、`Initialize`、`PostInitialize`、`UnInitialize` 等核心生命周期函数必须放在类型主文件中。
   不能放在功能拆分的 partial 文件中；功能逻辑可按现有目录结构拆分。
6. 初始化和清理遵循依赖顺序：先清理实体和节点，再关闭数据服务，最后注销程序集解析与异常处理、重置身份信息。
7. 异常处理放在需要恢复、补充上下文或跨语言边界的位置，避免给普通顺序逻辑添加冗余的 try 保护。

## 参考文档

- `Client/README.md`：客户端程序集职责与依赖。
- `Docs/ClientStartup.md`：业务注册与启动顺序。
- `Docs/DataFrameworkDesign.md`、`Docs/DataProjects.md`：数据框架契约与配置表接入。
- `Docs/RpcDesign.md`、`Docs/RpcAndSystemCommand.md`：RPC 与框架命令通道。
- `Docs/AvatarEntityImmigrationDesign.md`：Avatar 传送与跨 Game 迁移。
- `Docs/SharedMathematics.md`：共享数学类型与运算约定。

设计文档和历史计划可能保留旧命名或旧目录；涉及当前行为时结合源码、`.csproj` 和 `.asmdef` 核实。
