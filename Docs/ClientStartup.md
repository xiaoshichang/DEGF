# 客户端启动注册

Demo.Client 引用 DE.Client，通过 ApplicationRoot.RegisterGameplay 注册业务程序集和 GameInstance 工厂。框架不引用 Demo 类型。

## 启动顺序

1. SubsystemRegistration：ApplicationRoot 清空业务入口及实体类型注册，支持关闭 Domain Reload 后重复进入 Play Mode。
2. BeforeSceneLoad：DemoBootstrap 注册 typeof(DemoGameInstance).Assembly 和 () => new DemoGameInstance()。此时只保存工厂，不创建或初始化实例。
3. ApplicationRoot.Awake：确认注册完成，收集框架和业务程序集，初始化资源、UI、网络；扫描业务实体后初始化鉴权和 GM，最后创建 GameInstance 并执行 Init。

DemoBootstrap 不需要挂在 GameObject 上。场景保留 ApplicationRoot 组件，不再配置 AssemblyNameList。

当前支持注册一个业务程序集和一个 GameInstance 工厂。参数不能为空，同一运行会话中重复注册会报错；未注册或工厂返回 null 也会明确报错。

实体类型由 ClientEntityFactory 在鉴权初始化前从业务程序集发现，不再由 DemoGameInstance 手动注册。
DemoServerAvatarEntity 映射到普通 DemoClientAvatarEntity；登录只创建其派生类 DemoClientPlayerAvatarEntity，空间加载回调也只安装在本人上。
普通 Avatar 选择业务继承链的根类型，其唯一业务派生分支用于 PlayerAvatar；无派生时回退到普通 Avatar，多个分支直接报错。
Space 使用 DemoServerSpaceEntity / DemoClientSpaceEntity 对应；工厂将客户端类名中的 Client 替换为 Server，PlayerAvatar 分支不参与观察者映射。
反射发现的实体必须有公共无参构造函数；类型名映射冲突会在启动时报错。会话重置和鉴权清理会恢复框架默认类型。

## IL2CPP 与反射

GameInstance 入口使用显式工厂，不通过反射查找或创建 DemoGameInstance；其构造函数对编译器可见。
实体注册单独扫描已注册业务程序集，登录和可见实体工厂使用 Activator.CreateInstance，相关业务实体使用 Preserve 保留类型和默认构造函数。

Demo.Client 的 AlwaysLinkAssembly 标记确保程序集参与链接分析，不代表保留全部成员。GM 系统仍扫描注册程序集中的命令；当前 GameInstance 中的两个命令已添加 Preserve。后续新增仅通过反射调用的方法，也需要 Preserve 或 link.xml。

## 验证

Unity Test Runner 的 EditMode 下运行 Demo.Client.Tests.GameplayRegistrationTests，覆盖延迟创建、参数校验、重复注册、会话重置和 Demo 启动回调阶段。

这些测试不替代 iOS 的 IL2CPP 构建和真机启动验证。
