# 数据配置工程

数据工程用于声明配置数据类型。逻辑工程引用数据工程，数据工程不引用 Foundation、实体或业务逻辑工程。

## 源码位置

| 工程 | 源码目录 | 工程管理 |
| --- | --- | --- |
| DE.Share.Data | Client/Demo/Assets/DEFramework/Scripts/DE.Share.Data | Unity asmdef；Server/Framework/DE.Share.Data 链接同一份源码 |
| Demo.Server.Data | Server/Framework/Demo.Server.Data | Framework.sln |
| Demo.Client.Data | Client/Demo/Assets/Demo/Scripts/Demo.Client.Data | Unity asmdef |

服务端解决方案只包含共享数据和服务端项目数据两个新工程。客户端工程由 Unity 根据 asmdef 生成，生成的 csproj 和 Demo.sln 不纳入版本控制。

## 引用规则

| 数据工程 | 允许使用的层级 | 数据工程自身的引用 |
| --- | --- | --- |
| DE.Share.Data | 所有层级 | 无 |
| Demo.Server.Data | 服务端项目层 | DE.Share.Data |
| Demo.Client.Data | 客户端项目层 | DE.Share.Data |

DE.Share 和 DE.Share.Foundation 引用共享数据。两端框架的 Foundation、主逻辑工程，以及客户端编辑器工程，显式引用共享数据。Demo 逻辑工程还显式引用本端 Demo 数据。

这些规则通过当前 csproj/asmdef 引用关系表达；增加新工程时也应遵循以上规则。客户端专用数据程序集关闭 autoReferenced，使用它们的程序集需要显式声明引用。

Unity 中的两个数据程序集关闭引擎引用，仅声明普通 C# 数据类型。服务端链接共享源码，沿用现有工程的 .NET 10 和 C# 9 配置；共享代码须兼容 Unity 的运行环境。AssemblyInfo.cs 提供程序集说明，使暂未添加配置类型的程序集也能参与 Unity 编译。
