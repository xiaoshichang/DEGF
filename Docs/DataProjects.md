# 数据配置工程

数据工程用于声明、描述和加载配置数据。逻辑工程引用数据工程，数据工程不引用 Foundation、实体或业务逻辑工程。

## 源码位置

| 工程 | 源码目录 | 工程管理 |
| --- | --- | --- |
| DE.Share.Data | Client/Demo/Assets/DEFramework/Scripts/DE.Share.Data | Unity asmdef；Server/Framework/DE.Share.Data 链接同一份源码 |
| Demo.Share.Data | Client/Demo/Assets/Demo/Scripts/Demo.Share.Data | Unity asmdef；Server/Framework/Demo.Share.Data 链接同一份源码 |

服务端解决方案包含以上两个共享数据工程。客户端工程由 Unity 根据 asmdef 生成，生成的 csproj 和 Demo.sln 不纳入版本控制。

## 引用规则

| 数据工程 | 允许使用的层级 | 数据工程自身的引用 |
| --- | --- | --- |
| DE.Share.Data | 所有层级 | 无运行时项目引用；ExcelDataReader 3.8.0 用于读取 xlsx |
| Demo.Share.Data | 客户端、服务端项目层 | DE.Share.Data |

DE.Share 和 DE.Share.Foundation 引用 DE.Share.Data。两端框架的 Foundation、主逻辑工程，以及客户端编辑器工程，显式引用 DE.Share.Data。Demo.Client 和 Demo.Server 还显式引用 Demo.Share.Data；框架层不引用项目层数据。

这些规则通过当前 csproj/asmdef 引用关系表达；增加新工程时也应遵循以上规则。Demo.Share.Data 关闭 autoReferenced，使用它的程序集需要显式声明引用。

Unity 中的两个数据程序集关闭引擎引用，使用普通 C# 实现数据类型和加载逻辑。服务端链接共享源码，沿用现有工程的 .NET 10 和 C# 9 配置；共享代码须兼容 Unity 的运行环境。AssemblyInfo.cs 提供程序集说明。

## 两端条件编译

- Unity：Assets/csc.rsp 定义 DE_CLIENT，供包括两个数据程序集在内的客户端 C# 脚本使用，不依赖当前选择的构建平台。
- Server：Server/Framework/Directory.Build.props 为目录下的 C# 工程统一追加 DE_SERVER，包括链接共享源码的数据工程，并保留已有编译符号。

Demo.Share.Data 中的配置类型只维护一份源码，按编译端选择数值。例如：

```csharp
namespace Demo.Share.Data
{
    public static class DemoValues
    {
#if DE_CLIENT
        public const int UpdateIntervalMilliseconds = 100;
#elif DE_SERVER
        public const int UpdateIntervalMilliseconds = 50;
#endif
    }
}
```

示例数值仅用于说明条件编译方式，实际配置由业务定义。两端共用的类型名称和同步字段应保持一致。更改 csc.rsp 后，需要让 Unity 重新编译脚本；重新导入一个 C# 脚本即可触发。

参考：[Unity 2022.3 自定义脚本符号](https://docs.unity3d.com/cn/2022.3/Manual/CustomScriptingSymbols.html)。

## 框架配置与项目配置

`SpaceDataRow` 位于 `DE.Share.Data/Space`，命名空间为 `DE.Share.Data`。SG 生成的 `SpaceDataTable` 也属于 DE.Share.Data 程序集，框架层可以直接读取空间配置，无需引用 Demo。

```csharp
namespace DE.Share.Data
{
    [DataTable("SpaceData", Load = DataLoadPolicy.Full, Cache = DataCachePolicy.KeepAlive)]
    public sealed partial class SpaceDataRow : DataRow
    {
        public string Name { get; private set; }

        public int MaxPlayers { get; private set; }
    }
}
```

逻辑表名统一以 `Data` 结尾，例如 `SpaceData`、`ItemData`；行类型和生成表类型分别使用 `SpaceDataRow`、`SpaceDataTable` 这样的名称。

项目自己的表在 Demo.Share.Data 中另外定义，示例为 `Item/ItemDataRow.cs`，声明 Row + Lru 策略（CacheCapacity = 1024），生成 `Demo.Share.Data.ItemDataTable`。它包含 Name、StackLimit 两列，不改变框架的 Space 配置定义。

`DataRow.Id` 使用 `DataTableKey`，目前只支持 int。`DataTableDescribe` 和 `DataColumnDescribe` 是框架定义的类型；SG 生成每张表的 `TableDescribe` 初始化代码，列顺序为 Id 在前，其余属性按 Ordinal 排序。表内部使用字典，公开按键查询统一使用 `GetRow`，不提供索引器、字典接口或 int 隐式转换。

## 读取配置表

例如从仓库根目录运行：

```csharp
using System.IO;
using DE.Share.Data;
using DE.Share.Data.DataProvider;

// 启动时初始化一次。
DataRuntime.Initialize(new ExcelDataProvider());
DataRuntime.SetRootDirectory(Path.GetFullPath("Data/Excel"));

// 其他位置直接通过静态入口获取。
SpaceDataTable spaces = DataRuntime.GetTable<SpaceDataTable>();
DataTableKey key = DataTableKey.FromInt32(1001);
SpaceDataRow row = spaces.GetRow(key);
```

仓库的 `Data/Excel/SpaceData.xlsx` 是可直接读取的示例，工作表名为 `SpaceData`。默认资源名和 Sheet 名均为 attribute 的表名；可以用 `[DataTable("SpaceData", Source = "World/SpaceData", Sheet = "SpaceData")]` 指定映射。Source 不含扩展名。第 1 行是列名，第 2 行开始是数据，列名默认匹配属性名，也可以用 `[DataColumn("DisplayName")]` 覆盖映射。

`DataRuntime` 是静态类，当前按单线程使用，不包含锁或多线程同步。启动时只需 Initialize 和设置根目录，各处直接调用 GetTable。无需逐表注册，SG 不再生成 Register 方法。首次 GetTable 自动创建表句柄；Full 同时加载所有行，Row 在 GetRow 未命中时加载对应批次，后续 GetTable 复用句柄。重复初始化会报错；应用结束时调用 `DataRuntime.Shutdown()` 清理 Provider、根目录和已加载表。Shutdown 可以重复调用，之后允许重新初始化；旧句柄仍可读取已缓存行和已知 Count，但不能再从数据源加载；重新初始化后需要重新 GetTable。

数据加载同步执行，行缓存遵循 Row attribute 策略。每次初始化后，根目录只能成功设置一次；再次调用 SetRootDirectory 会报错，即使传入相同目录或尚未加载数据。根目录设置失败时仍可重试。需要另一套目录或 Provider 时，先 Shutdown，再重新 Initialize 和设置根目录。加载期间不允许 Shutdown。缺失键时 GetRow 抛出含表名和主键的 KeyNotFoundException。任一批次或 Count 扫描失败时记录整张表的失败状态，不发布失败批次；后续 GetTable、GetRow 和 Count（包括此前已缓存行）直接重新抛出原错误，不再访问数据源，即使文件已修正也不重试。Shutdown 会清除失败状态，重新初始化后才能再次加载。

当前支持 xlsx 的基础数值、bool、string、enum 和可空值类型。非空整数不能缺失或溢出；超过 15 位的整数以文本保存。额外具名列忽略；空 Id 不会因其他列被忽略而跳过。公式只读取已保存结果，不执行计算。二进制 Provider 留作后续扩展。

## Row 策略与预热

```csharp
[DataTable("ItemData", Load = DataLoadPolicy.Row, Cache = DataCachePolicy.Lru,
    CacheCapacity = 1024)]
public sealed partial class ItemDataRow : DataRow
{
    public string Name { get; private set; }
    public int StackLimit { get; private set; }
}
```

- 加载：`Full` 全量（默认）、`Row` 逐行。
- 缓存：`KeepAlive` 不卸载（默认）、`Lru` 每张表按最近访问顺序淘汰行。
- `CacheCapacity` 默认 1024，是 Lru 缓存行数上限；缓存容量必须为正数，非法策略由 SG 报 DSG010。

`Count` 始终是表的总行数，首次读取可能扫描主键。Excel 仍需顺序扫描，但仅构建目标批次的 DataRow；缓存容量不限制读取批次的临时内存。Full + Lru 未命中会重新全量读取。淘汰仅释放缓存引用，已取到的行对象仍有效。

`SpaceDataRow` 已显式声明 Full + KeepAlive，可由调用方预热：

```csharp
// Initialize 和 SetRootDirectory 之后，同步预热。
SpaceDataTable spaces = DataRuntime.Prewarm<SpaceDataTable>();

// 或在异步初始化方法中调用，同一个预热接口由用户选择调度方式。
SpaceDataTable warmed = await System.Threading.Tasks.Task.Run(
    () => DataRuntime.Prewarm<SpaceDataTable>());
```

预热只允许 Full + KeepAlive；重复调用复用表，错误不会自动重试。运行时仍不提供并发访问保证：后台预热期间不能调用其他数据入口，必须等待 await 完成后再读取或 Shutdown；多表在一个任务中顺序预热。集群 `_InitDataRuntime()` 仍只初始化 Provider 和根目录，预热时机由用户决定。

## 集群启动配置

集群 JSON 顶层通过 `dataRoot` 指定数据目录。`Server/Config/local-dev.json` 已配置：

```json
"dataRoot": "../../Data/Excel"
```

相对路径以集群配置文件所在目录为基准，也支持绝对路径。该字段必填，目录必须存在；ManagedClusterConfig.Load 将它规范化为绝对路径。现有启动脚本和 native bridge 已传递配置文件路径，无需再增加命令行参数。

ManagedRuntimeState 的 `_InitDataRuntime()` 使用该路径初始化 ExcelDataProvider 并设置 DataRuntime 根目录，随后框架逻辑可直接调用 `DataRuntime.GetTable<SpaceDataTable>()`。框架表和项目自定义表都在首次访问时按策略自动加载，无需在启动代码中维护表清单。各节点进程拥有自己的静态 DataRuntime；节点退出或初始化失败时，清理本次启动创建的数据运行时状态。

## 构建与 Unity 接入

声明 DataRow 的服务端数据项目需要显式引用生成器。DE.Share.Data 和 Demo.Share.Data 均已接入：

```xml
<ProjectReference Include="..\DE.Share.DataTableSG\DE.Share.DataTableSG.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Unity 的 Plugins 目录包含匹配版本的 `DE.Share.DataTableSG.dll` 与 `ExcelDataReader.dll`。前者标记 RoslynAnalyzer 并禁用运行时平台加载；后者由 DE.Share.Data.asmdef 显式引用。ExcelDataReader 的许可证随 DLL 一同保存。服务端在打开 xlsx 前注册代码页支持，因为该库的配置构造函数会查询代码页 1252。

修改生成器或升级 ExcelDataReader 后，在仓库根目录执行：

```powershell
./Server/Framework/sync_data_plugins.ps1 -Configuration Debug
dotnet build Server/Framework/Framework.sln
dotnet test Server/Framework/Framework.sln --no-build --no-restore
```

同步脚本从还原结果定位包目录，复制 netstandard2.0 的 Excel DLL，并保留已有 Unity meta。不要修改 Unity 自动生成的 csproj 或把生成的 C# 文件写回 Assets。

Unity 菜单 `DEGF/Validate Data Tables` 会验证示例的真实读取。也可以使用项目对应的 Unity 编辑器执行：

```text
Unity.exe -batchmode -nographics -quit -projectPath <repository>/Client/Demo -executeMethod Demo.Data.Editor.DataFrameworkValidation.Validate -logFile <log-path>
```

详细契约见 [数据框架设计](DataFrameworkDesign.md)。
