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
