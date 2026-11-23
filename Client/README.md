# 客户端程序集

Unity 工程位于 `Demo`，框架源码位于 `Demo/Assets/DEFramework/Scripts`。

| 程序集 | 职责 | 依赖方向 |
| --- | --- | --- |
| `DE.Client.Foundation` | 与游戏类型无关的核心功能：应用架构、资源管理、日志、网络、UI、Entity、鉴权和 GM | 依赖双端共享程序集，不依赖 Features 或 Demo |
| `DE.Client.Features` | 某一类游戏可以复用的功能，例如移动控制、相机控制 | 依赖 Foundation 和双端共享程序集 |
| `DE.Client.Editor` | 框架的 Unity 编辑器工具 | 依赖 Foundation，仅在 Editor 编译 |
| `Demo.Client` | Demo 业务逻辑及启动注册 | 依赖 Foundation、Features 和共享程序集 |
| `Demo.Editor` | Demo 几何体资源生成工具 | 仅依赖 Unity Editor 和 URP，不依赖游戏运行时 |

原 `DE.Client` 的实现已全部迁入 `DE.Client.Foundation`，并已移除空的 `NewBehaviourScript` 模板脚本。
原 `DE.Client` 程序集改名为 `DE.Client.Features`，当前仅保留程序集说明，后续按需添加可复用功能。

迁移保留现有类型命名空间和脚本 `.meta` GUID。程序集边界由目录下的 `.asmdef` 决定。
Foundation 保留原程序集 GUID，Features 沿用原 Client 的程序集 GUID；引用核心类型的程序集需要直接引用 Foundation。

Demo 通过 `ApplicationRoot.RegisterGameplay` 注册业务程序集和 `GameInstance` 工厂，Foundation 不引用 Demo 类型。
新增游戏类型相关的通用功能放入 Features，具体游戏业务放入对应游戏的程序集。

在 Unity 中刷新资源并重新生成 IDE 项目文件后，可构建 `Demo/Demo.sln`。
启动注册的回归验证位于 Unity Test Runner 的 EditMode 测试 `Demo.Client.Tests.GameplayRegistrationTests`。

## 3D URP 与 Demo 资源

项目默认使用 3D 模式和 `Assets/Settings/Renderer3D.asset`，由现有 `UniversalRP.asset` 引用。
Runtime 场景使用透视相机和有阴影的方向光；新场景模板为 `Lit 3D (URP)`。
原有 Renderer2D 保留为可选 Renderer，项目不再依赖 2D Feature 包。

`Assets/Demo/Asb` 下的资源按以下目录组织，Unity 中 1 单位对应 1 米，Y 向上、Z 为角色前方：

| 目录 | 内容 |
| --- | --- |
| `StaticSceneItem` | 共用材质、基础 Mesh，以及房屋、树、岩石、长椅、路灯、货箱、栅栏、摊位、喷泉和小桥 Prefab |
| `StaticScene` | 可供实例化的 `City.prefab`、`Outskirts.prefab` 静态地图 |
| `Character` | Warrior、Archer、Mage、Merchant 各占一个目录，分别包含自己的 Prefab、Controller、Animations、Materials 和 Meshes |

两张静态地图的地面均为 100 × 100 米，原点在地面中心，并提供 `SpawnPoint`。
SpaceNo 1 对应城市，SpaceNo 2 对应郊外，此处是资源命名约定；尚未接入服务器配置表和登录后的空间加载回调。
Space 的表现资源以地图 Prefab 提供，不生成对应的 `.unity` 文件，也不使用 Unity SceneManager 管理空间切换。
后续接入空间加载时，业务应通过 AssetManager 加载地图资源，再按空间生命周期实例化、销毁地图对象并释放加载句柄。
地图 Prefab 仅包含静态场景内容，相机和光照由客户端统一管理。
角色不预置在静态地图内，按需实例化。各角色采用一致的约 2 米高主体设计、胶囊身体、球形双手与方形双脚，配饰可能略高于头顶。

角色之间不共用 Mesh、材质、动画或 Controller，也不引用 StaticSceneItem 中的资源。
每个角色分别保存 Idle、Run、Hit（受击）、Jump、Death、Attack 六个动画；攻击分别表现剑击、拉弓、施法和挥动货包。
Animator 的 `Speed` 大于 0.1 时跑步，小于 0.1 时待机；`Attack`、`Hit`、`Jump`、`Death` 为 Trigger。
死亡状态没有出口；重生时由业务显式重置 Animator。动画不驱动实体根位移，Jump 只抬升 Visual，实际移动和碰撞由业务控制。

编辑器菜单 `DEGF/Demo/Generate Geometry Assets` 可重新生成上述资源并恢复 3D URP 配置。
执行前保存当前场景并退出 Play Mode；重新生成会覆盖生成器负责的资源内容，已有资源 `.meta` GUID 保留。
工具同时将场景与五种动作的预览输出到不纳入版本控制的 `Library/DemoGeometryPreview`。

实体框架从 `DE.Client.Foundation/Entities/ClientEntity` 开始派生，组件从 `ClientEntityComponent` 派生。
客户端独立维护生命周期、组件集合和 EntitySerializer。
EntityPropertyAttribute、EntityComponentAttribute 和 EntityPropertyFlag 统一定义在 `DE.Share/Entities`，双端通过 `DE.Share.Entities` 使用。
服务器在 DE.Server.Entities 内实现对应类型，双端使用版本 4 的属性树协议。
实体是根节点，`Guid` 是根叶属性；`[EntityComponent]` 标记实体持有组件的属性，如 `TransformCmpt`，组件内再声明 `[EntityProperty]`。
节点路径使用各级属性名的 UTF-8 FNV-1a 64 位哈希；组件类名、装配顺序和继承层级不参与协议标识，继承属性平铺在所在节点。
组件通过 `AddComponent(component)` 装配；SG 生成组件引用到持有属性名哈希的路由，并串接基类路由，不在组件保存名称。
业务在 partial 类内的私有 `__` 字段上声明 `[EntityProperty]`，生命周期和回调保留在手写文件；SG 在 `.g.cs` 中生成访问器、强类型编解码和服务端脏标记。
客户端不维护脏属性集合或生成 Patch，setter 只负责赋值和业务回调。
服务器由 `AutoSyncProperty` 控制追踪及自动同步，默认关闭，仅 ServerAvatarEntity 开启；脏集合由 `ServerEntity.AutoSyncProperties.cs` 按需创建。
各 ServerSpaceEntity 每 50 ms 驱动本空间成员 Avatar 同步；空间清理时停止定时器，Space 自身不自动同步，仍通过入场快照传递状态。
不创建运行时属性 metadata、反射或读写委托；暂不支持数组、List、Dictionary 等容器属性，不支持的字段类型在编译期报错。
Profile 区分 Owner、Observer、Migration；Mode 区分 Snapshot、Patch。Owner 同时接收 ClientServer 和 AllClients，Observer 接收 AllClients。
Snapshot 恢复状态；服务器 setter 仅按属性路径标脏，发送时由 SG 读取当前字段，每个属性只发送最新值，发送后清空脏集合，暂不处理发送失败或重试。
不记录中间值，不保证跨属性变化顺序；本地回调仍逐次执行。RPC 与属性同步不保证相互顺序，服务器没有接收 Patch 的入口。
客户端依赖有序传输按包和记录的接收顺序应用，不维护同步基线，不去重或检查序号缺口；Owner 快照在开始加载空间时应用。
接收端先完整校验，再重新解析并直接赋值后备字段，不触发属性回调；业务通过 `OnPropertiesApplied` 读取本次属性状态更新，不将其作为逐次变化事件。
Owner / Observer 跳过未知节点；`Guid` 必须存在，Migration 严格要求完整且兼容的服务端状态。
双端 Transform 组件直接操作 `Position` 和 `Rotation` 属性，使用共享数学类型，属性访问器由 SG 生成。
