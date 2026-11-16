# 客户端程序集

Unity 工程位于 `Demo`，框架源码位于 `Demo/Assets/DEFramework/Scripts`。

| 程序集 | 职责 | 依赖方向 |
| --- | --- | --- |
| `DE.Client.Foundation` | 与游戏类型无关的核心功能：应用架构、资源管理、日志、网络、UI、Entity、鉴权和 GM | 依赖双端共享程序集，不依赖 Features 或 Demo |
| `DE.Client.Features` | 某一类游戏可以复用的功能，例如移动控制、相机控制 | 依赖 Foundation 和双端共享程序集 |
| `DE.Client.Editor` | 框架的 Unity 编辑器工具 | 依赖 Foundation，仅在 Editor 编译 |
| `Demo.Client` | Demo 业务逻辑及启动注册 | 依赖 Foundation、Features 和共享程序集 |

原 `DE.Client` 的实现已全部迁入 `DE.Client.Foundation`，并已移除空的 `NewBehaviourScript` 模板脚本。
原 `DE.Client` 程序集改名为 `DE.Client.Features`，当前仅保留程序集说明，后续按需添加可复用功能。

迁移保留现有类型命名空间和脚本 `.meta` GUID。程序集边界由目录下的 `.asmdef` 决定。
Foundation 保留原程序集 GUID，Features 沿用原 Client 的程序集 GUID；引用核心类型的程序集需要直接引用 Foundation。

Demo 通过 `ApplicationRoot.RegisterGameplay` 注册业务程序集和 `GameInstance` 工厂，Foundation 不引用 Demo 类型。
新增游戏类型相关的通用功能放入 Features，具体游戏业务放入对应游戏的程序集。

在 Unity 中刷新资源并重新生成 IDE 项目文件后，可构建 `Demo/Demo.sln`。
启动注册的回归验证位于 Unity Test Runner 的 EditMode 测试 `Demo.Client.Tests.GameplayRegistrationTests`。

实体框架从 `DE.Client.Foundation/Entities/ClientEntity` 开始派生，组件从 `ClientEntityComponent` 派生。
客户端独立维护生命周期、组件集合和 EntitySerializer。
EntityPropertyAttribute、EntityComponentAttribute 和 EntityPropertyFlag 统一定义在 `DE.Share/Entities`，双端通过 `DE.Share.Entities` 使用。
服务器在 DE.Server.Entities 内实现对应类型，双端使用版本 4 的属性树协议。
实体是根节点，`Guid` 是根叶属性；`[EntityComponent]` 标记实体持有组件的属性，如 `TransformCmpt`，组件内再声明 `[EntityProperty]`。
节点路径使用各级属性名的 UTF-8 FNV-1a 64 位哈希；组件类名、装配顺序和继承层级不参与协议标识，继承属性平铺在所在节点。
组件通过 `AddComponent(component)` 装配；SG 生成组件引用到持有属性名哈希的路由，并串接基类路由，不在组件保存名称。
业务在 partial 类内的私有 `__` 字段上声明 `[EntityProperty]`，生命周期和回调保留在手写文件；SG 在 `.g.cs` 中生成访问器、强类型编解码和变化记录。
客户端不维护脏属性日志或生成 Patch，setter 只负责赋值和业务回调。
服务器由 `AutoSyncProperty` 控制追踪及自动同步，默认关闭，ServerAvatarEntity、ServerSpaceEntity 开启；日志由 `ServerEntity.AutoSyncProperties.cs` 按需创建。
不创建运行时属性 metadata、反射或读写委托；暂不支持数组、List、Dictionary 等容器属性，不支持的字段类型在编译期报错。
Profile 区分 Owner、Observer、Migration；Mode 区分 Snapshot、Patch。Owner 同时接收 ClientServer 和 AllClients，Observer 接收 AllClients。
Snapshot 恢复状态；服务器对开启自动同步的实体每次一次性同步全部待发送变化，成功后清空日志，不分 batch；保留跨组件顺序和同属性重复变化。服务器没有接收 Patch 的入口。
客户端依赖有序传输按包和记录的接收顺序应用，不维护同步基线，不去重或检查序号缺口；Owner 快照在开始加载空间时应用。
接收端先完整校验，再重新解析并直接赋值后备字段，不触发属性回调；业务通过 `OnPropertiesApplied` 读取有序变化记录。
Owner / Observer 跳过未知节点；`Guid` 必须存在，Migration 严格要求完整且兼容的服务端状态。
双端 Transform 组件直接操作 `Position` 和 `Rotation` 属性，使用共享数学类型，属性访问器由 SG 生成。
