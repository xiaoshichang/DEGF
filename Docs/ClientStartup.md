# 客户端启动注册

Demo.Client 引用 DE.Client，通过 ApplicationRoot.RegisterGameplay 注册业务程序集和 GameInstance 工厂。框架不引用 Demo 类型。

## 启动顺序

1. SubsystemRegistration：ApplicationRoot 清空静态注册信息，支持关闭 Domain Reload 后重复进入 Play Mode。
2. BeforeSceneLoad：DemoBootstrap 注册 typeof(DemoGameInstance).Assembly 和 () => new DemoGameInstance()。此时只保存工厂，不创建或初始化实例。
3. ApplicationRoot.Awake：确认注册完成，收集框架和业务程序集，初始化资源、UI、网络、鉴权和 GM 系统，最后调用工厂创建 GameInstance 并执行 Init。

DemoBootstrap 不需要挂在 GameObject 上。场景保留 ApplicationRoot 组件，不再配置 AssemblyNameList。

当前支持注册一个业务程序集和一个 GameInstance 工厂。参数不能为空，同一运行会话中重复注册会报错；未注册或工厂返回 null 也会明确报错。

## IL2CPP 与反射

入口不再使用 Assembly.Load、程序集类型扫描或 Activator.CreateInstance。显式构造调用使 DemoGameInstance 的构造函数对编译器可见。

Demo.Client 的 AlwaysLinkAssembly 标记确保程序集参与链接分析，不代表保留全部成员。GM 系统仍扫描注册程序集中的命令；当前 GameInstance 中的两个命令已添加 Preserve。后续新增仅通过反射调用的方法，也需要 Preserve 或 link.xml。

## 验证

Unity Test Runner 的 EditMode 下运行 Demo.Client.Tests.GameplayRegistrationTests，覆盖延迟创建、参数校验、重复注册、会话重置和 Demo 启动回调阶段。

这些测试不替代 iOS 的 IL2CPP 构建和真机启动验证。
