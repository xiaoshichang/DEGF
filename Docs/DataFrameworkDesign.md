# 数据框架设计

日期：2026-09-10。状态：已确认并实现首版；第 10、11 节描述后续扩展，具体用法见 DataProjects.md。

## 1. 设计结论

采用 **C# 定义数据行 + SG 生成描述和数据表 + 可替换的数据源 + 静态运行时管理器**。

首版提供 `.xlsx` 整表读取，以 `DataTableKey` 抽象表主键，当前只支持封装 int；首次请求表时同步加载，加载成功后在当前 `DataRuntime` 中保留表实例。`DataTableKey` 这一层是用户确认的设计要求。

用户维护 `SpaceDataRow`，生成器生成 `SpaceDataTable`。后者内部持有 `Dictionary<DataTableKey, SpaceDataRow>`，对外统一通过 `GetRow(DataTableKey key)` 查询。公开 API 不暴露索引器或字典接口。配置行不继承 Entity，配置主键不承担分布式实体 Guid 或运行时空间实例 ID 的职责。

后续增加二进制 Provider，以及由 attribute 指定的加载、缓存策略；业务代码仍然使用同一种 `SpaceDataTable`。

### 方案比较

| 方案 | 优点 | 代价 | 选择 |
| --- | --- | --- | --- |
| C# 行模型 + SG | 保留用户定义类型；编译期校验；生成直接赋值代码 | 需要维护独立生成器 | 推荐 |
| C# 行模型 + 运行时反射 | 初始实现较少 | 成员约束延迟到运行时；还需处理裁剪和 AOT 的成员保留 | 不作为首版方案 |
| Excel 定义结构并生成所有 C# 类型 | 类型与表格结构集中维护 | 改变用户手写 SpaceDataRow 的工作方式；类型生成依赖文件导出流程 | 不适合本次需求 |

## 2. 模块与依赖

共享运行时源码放在已有的 `Client/Demo/Assets/DEFramework/Scripts/DE.Share.Data`。服务端 `Server/Framework/DE.Share.Data` 链接同一份源码；不要只在服务端目录添加实现。

```text
DE.Share.Data/
    DataDescribe/
        DataTableKey.cs
        DataRow.cs
        DataTableDescribe.cs
        DataColumnDescribe.cs
        DataValueKind.cs
        DataTableAttribute.cs
        DataColumnAttribute.cs
    DataProvider/
        IDataProvider.cs
        IDataTableReader.cs
        ExcelDataProvider.cs
        ExcelDataTableReader.cs
        ExcelValueConverter.cs
    Space/
        SpaceDataRow.cs
    DataTable.cs
    DataTableFactory.cs
    DataTableLoader.cs
    IDataTable.cs
    DataRuntime.cs
    DataLoadException.cs

Demo.Share.Data/
    Item/
        ItemDataRow.cs

Server/Framework/
    DE.Share.DataTableSG/
        DE.Share.DataTableSG.csproj
        DataTableGenerator.cs
```

公共入口 `DataTableKey`、`DataRow`、`DataTable<TRow>`、`IDataTable`、`DataRuntime` 和 attributes 使用 `DE.Share.Data` 命名空间。描述类型使用 `DE.Share.Data.DataDescribe`；Provider 类型使用 `DE.Share.Data.DataProvider`。目录与命名空间可以有不同粒度。数据源契约命名为 `IDataProvider`，避免类型名与 `DataProvider` 子命名空间冲突。

`Demo.Share.Data` 引用 `DE.Share.Data`；两个数据程序集不依赖 Foundation、Entity、客户端或服务端业务逻辑。Excel 解析库是底层第三方依赖，不改变项目之间的依赖方向。SG 是编译期 analyzer，不作为运行时程序集引用。

`SpaceDataRow` 是框架空间配置，位于 `DE.Share.Data/Space`，命名空间为 `DE.Share.Data`；生成的 `SpaceDataTable` 也编入框架数据程序集，框架逻辑可以直接读取。项目示例另定义 `Demo.Share.Data.ItemDataRow`，生成 `ItemDataTable`，框架层仍不引用 `Demo.Share.Data`。

## 3. 数据描述模型

`DataDescribe` 表达“有哪些列、列是什么类型、如何定位数据源”，不持有文件句柄、已加载行或缓存状态。

| 类型 | 内容 |
| --- | --- |
| `DataTableKey` | 表主键的不可变值对象；首版只封装 int |
| `DataRow` | 所有配置行的公共基类，声明主键 `Id` |
| `DataTableDescribe` | 逻辑表名、无扩展名的相对资源名、工作表名、行类型标识、有序列描述 |
| `DataColumnDescribe` | C# 属性名、来源列名、规范类型、可空性、是否主键；枚举还包括底层类型及成员定义 |
| `DataValueKind` | 框架支持的标量类型标识，不使用 Excel 专有类型 |
| `DataTableAttribute` | 标记需要生成表的行类，并声明表名及可选资源映射 |
| `DataColumnAttribute` | 可选的 Excel 列名映射；未标记时使用 C# 属性名 |

`DataTableDescribe` 和 `DataColumnDescribe` 是框架定义的通用类型；SG 生成各表的 `TableDescribe` 初始化代码。描述保持不可变，列集合不能向外暴露可修改的数组或 List。枚举类型由 SG 写入描述，构造函数据此建立只读的枚举名称和值列表，不反射扫描或写入行属性。首版列顺序固定为 `Id` 在前，其余按 C# 属性名的 Ordinal 顺序排列。Excel 的物理列位置通过表头绑定，不依赖代码声明顺序。

逻辑表名统一以 `Data` 结尾，例如 `SpaceData`、`ItemData`。`[DataTable("SpaceData")]` 的默认值为：逻辑表名 `SpaceData`，资源名 `SpaceData`，工作表名 `SpaceData`。可通过 `Source = "World/SpaceData"` 和 `Sheet = "SpaceData"` 覆盖资源与工作表映射。资源名不包含 `.xlsx` 或二进制扩展名；由 Provider 决定文件格式。C# 类型仍命名为 `SpaceDataRow`、`SpaceDataTable`。

## 4. DataTableKey、DataRow 与用户定义

### DataTableKey

将 `DataTableKey` 设计为 `readonly struct`，实现 `IEquatable<DataTableKey>`，用值对象提供主键抽象。首版内部仅保存一个 int，不使用 object 存储，也不为每个主键分配引用对象。

| 契约 | 语义 |
| --- | --- |
| `FromInt32(int value)` | 构造一个 int 主键 |
| `AsInt32()` | 读取 int 值；将来遇到其他键类型时应报类型错误，不自动转换 |
| `Equals`、`GetHashCode`、`==`、`!=` | 首版按封装的 int 值进行相等与哈希计算 |
| `default(DataTableKey)` | 表示 int 类型的零，与 `FromInt32(0)` 相等 |

不提供 int 与 Key 之间的隐式转换；调用方先通过 `FromInt32` 明确构造主键，再交给 `GetRow`。不接受 string、long、Guid 或复合键。未来扩展时在内部增加键类型区分，相等与哈希同时考虑类型和值，例如 int 1 与字符串 "1" 不相等；`DataRow.Id` 与 `GetRow` 参数类型保持 `DataTableKey`。

数据描述仍记录 Id 的底层存储类型为 Int32、不可空、主键。Reader 读取 int，生成的工厂调用 `DataTableKey.FromInt32(reader.GetInt32(0))`；Provider 不需要理解字典键对象。首版不允许把 DataTableKey 声明为普通数据列或可空主键。

### DataRow

基类：

```csharp
namespace DE.Share.Data
{
    public abstract class DataRow
    {
        public DataTableKey Id { get; protected set; }
    }
}
```

框架的 Space 配置定义：

```csharp
namespace DE.Share.Data
{
    [DataTable("SpaceData")]
    public sealed partial class SpaceDataRow : DataRow
    {
        public string Name { get; private set; }

        public int MaxPlayers { get; private set; }
    }
}
```

首版要求行类直接继承 `DataRow`，是顶层、非泛型的 `public sealed partial class`，并具有无参构造函数，隐式构造函数也可以。构造函数可为 private，因为生成的行工厂位于同一个 partial 类型内。

行类自行声明的所有 public 实例属性均视为数据列，要求使用 public getter、private setter 的自动属性。不支持 public setter、计算属性、静态数据属性、字段映射或嵌套对象。禁止重新声明 `Id`。这些约束在编译期报错，避免遗漏成员后悄悄加载默认值。

`Id` 封装的 int 不强制大于零；首版只要求底层值是合法的 int 且在表内唯一。业务如需正数约束，可在以后增加业务数据校验。

SG 在 `SpaceDataRow` 的 partial 部分生成内部静态行工厂，通过直接赋值填充 `Id` 与私有 setter。业务代码获得的是只读行；字段只支持标量，因此也不会通过可变集合修改缓存内容。首版不依赖 `init`、`required` 或反射写入属性。

## 5. DataProvider：屏蔽文件格式

抽象接口：

```csharp
public interface IDataProvider
{
    IDataTableReader OpenTable(
        string rootDirectory,
        DataTableDescribe describe);
}

public interface IDataTableReader : System.IDisposable
{
    string SourcePath { get; }
    string SheetName { get; }
    long RowNumber { get; }

    bool Read();
    bool IsNull(int columnIndex);
    int GetInt32(int columnIndex);
    uint GetUInt32(int columnIndex);
    long GetInt64(int columnIndex);
    ulong GetUInt64(int columnIndex);
    float GetSingle(int columnIndex);
    double GetDouble(int columnIndex);
    bool GetBoolean(int columnIndex);
    string GetString(int columnIndex);
    TEnum GetEnum<TEnum>(int columnIndex) where TEnum : struct, System.Enum;
}
```

这里的 `columnIndex` 是描述中的逻辑列序号。Excel Reader 在打开表时把逻辑列映射到表头位置；未来的二进制 Reader 映射到二进制字段位置。SG 只根据描述选择强类型 getter。

Getter 必须按目标类型做校验和转换，不能简单透传 Excel 库的同名 getter。缺失值、溢出和格式错误统一带上文件、工作表、实际行号、列名、目标类型上下文；原始异常作为 InnerException 保留。可空值由生成的代码先调用 `IsNull`。

Provider 只创建独立 Reader，不缓存表或持有共享游标。`DataRuntime` 传入本次固定的根目录，Provider 无需访问进程级静态路径。Excel Reader 拥有文件流；生成的表加载方法使用 `using`，成功或失败都关闭 Reader 和流。打开 Reader 过程中失败时，也必须关闭已经创建的流。

首版使用 ExcelDataReader 3.8.0 的底层 Reader API，不引入 `AsDataSet()`。其官方说明列出了 `.xlsx` 支持和 .NET Standard 2.0 目标；Unity 兼容性通过本项目实际编译和运行验收。[ExcelDataReader 官方说明](https://github.com/ExcelDataReader/ExcelDataReader)

框架首版明确支持 `.xlsx`，不开放 `.xls`、CSV、加密工作簿或 Excel 写入功能。库支持更多格式不等于框架首版自动承诺这些格式。服务端通过包引用接入；Unity 导入同为 3.8.0 的 netstandard2.0 运行时 DLL，通过 sync_data_plugins.ps1 维护同步。

## 6. 数据源根目录

`DataRuntime.SetRootDirectory(string rootDirectory)` 是统一设置入口：

1. 要求非空的本地目录路径，转换为绝对路径并检查目录存在；相对目录基于调用时的工作目录解析。
2. 在 `DataRuntime.Initialize` 之后、首次 GetTable 之前调用；根目录只能成功设置一次。设置失败时不占用这次机会，可以更正路径后重试；未设置目录时加载会明确报错。
3. 设置成功后不允许再次设置，即使传入相同目录。直接根据根目录是否为 null 判断，无需额外的 started 状态。要更换目录或 Provider，先 `Shutdown`，再重新 Initialize 和设置根目录，避免缓存混用。
4. 资源名必须是相对名称；拒绝绝对资源路径、扩展名和 `..` 路径段，组合并规范化后的路径必须位于根目录下。

例如根目录为 `D:/GameData`、资源名为 `World/SpaceData`，Excel Provider 打开 `D:/GameData/World/SpaceData.xlsx`，再选择描述中的工作表。

同一进程使用一套静态配置，当前不同时管理多个根目录。本接口不处理 URL、压缩包内部的 Unity StreamingAssets 或远程下载。各端启动代码负责准备可访问的本地目录，共享数据程序集不引用 Unity 路径 API。

服务端从集群 JSON 顶层的 `dataRoot` 读取目录，例如 `Server/Config/local-dev.json` 配置为 `../../Data/Excel`。ManagedClusterConfig.Load 要求该字段非空、目录存在，并以配置文件所在目录为基准将相对路径转成绝对路径，再交给 `_InitDataRuntime()`。该方法仅初始化 ExcelDataProvider 和设置根目录；数据表在首次 GetTable 时自动加载。启动失败或退出时清理本次启动创建的数据运行时。native bridge 沿用现有配置文件路径传递，不增加 ABI 字段。

## 7. DataTableSG 与数据表

新增独立工程 `DE.Share.DataTableSG`，首版沿用现有两个 SG 的 `netstandard2.0`、C# 9、`Microsoft.CodeAnalysis.CSharp 3.8.0` 和 `ISourceGenerator` 方式。当前 Unity 工程版本为 2022.3；该版本官方生成器说明要求 .NET Standard 2.0 和 Roslyn 3.8。[Unity 2022.3 生成器说明](https://docs.unity.cn/Manual/roslyn-analyzers.html)

SG 扫描当前编译程序集内标记 `[DataTable]` 的行类，使用 Roslyn 符号识别继承和 attributes，不根据短名称做文本匹配。引用的其他程序集中的行类不重复生成。

`SpaceDataRow` 必须以 `DataRow` 结尾，去掉该后缀后追加 `DataTable`，生成相同命名空间下的 `SpaceDataTable`。SG 生成：

| 产物 | 职责 |
| --- | --- |
| `SpaceDataRow` 的 partial 行工厂 | 读取强类型值并直接赋值 |
| `SpaceDataTable.TableDescribe` | 静态不可变表描述 |
| `SpaceDataTable` | 继承 `DataTable<SpaceDataRow>` 的具体表，提供 GetRow 查询 |
| 内部加载方法 | 创建 Reader、逐行构建临时字典、检查重复主键、完整成功后返回表 |
| `SpaceDataTable` 静态构造函数 | 自动为内部泛型工厂绑定表描述和强类型加载委托，不读取文件 |

`DataTable<TRow>` 的行类型约束为 `where TRow : DataRow`，实现非泛型契约 `IDataTable`；后者暴露 `Describe` 和 `Count`，供 Runtime 约束表类型。泛型基类保存私有字典，并提供唯一的按键查询入口：

```csharp
public TRow GetRow(DataTableKey key);
```

它不实现公开的字典接口，不暴露索引器、Dictionary、TryGetValue、ContainsKey、Keys、Values 或字典枚举入口。获取具体数据行统一调用 `GetRow`，不得通过转换为接口绕开这一约定。

具体表是 sealed，构造函数不公开。泛型基类对传入的字典做防御复制，避免持有原字典的调用方改变表内容；没有向业务代码暴露 Add 或 Remove 的入口。`GetRow` 命中时返回已加载的只读行，缺失键时抛出包含逻辑表名和主键的 `KeyNotFoundException`，不返回默认配置。首版不另外提供按键查询重载或遍历接口。

SG 不再生成 Register 方法。`DataTable<TRow>` 的 protected InitializeFactory 方法仅用于生成的静态构造函数，绑定内部 `DataTableFactory<TTable>`。首次 GetTable 使用 RuntimeHelpers.RunClassConstructor 确保静态工厂已经准备好，之后通过强类型委托创建表；不反射查找成员或扫描程序集。访问 TableDescribe 也可能提前准备工厂，但不会打开数据源。工厂只保存不可变描述和加载方法，不保存 Provider、根目录或数据行。

SG 的编译错误至少覆盖：错误基类、非 partial/sealed 类型、嵌套或泛型类型、缺少无参构造、非法属性、隐藏 Id、不支持的类型、重复映射列名、非法资源名，以及生成类型或成员名称冲突。不同源文件的生成输出按完整类型名稳定排序，hint name 使用不产生全名编码碰撞的规则；冲突必须报诊断，不能覆盖其他产物。

服务端在声明数据行的 csproj 上添加 analyzer ProjectReference；Unity 将构建的 SG DLL 导入现有 Plugins 目录，并沿用 `RoslynAnalyzer` 标签及禁用运行时平台加载的配置。生成代码不写回 Assets，不手改 Unity 自动生成的 csproj。SG 不读取 Excel 文件，编译不要求数据根目录存在。

## 8. DataRuntime 与加载流程

`DataRuntime` 是静态类，统一持有 Provider、根目录、已占用表名及表缓存。各处通过静态方法读取数据，无需传递实例或逐表注册。当前按单线程使用，不实现锁或多线程同步，也不反射扫描程序集。

核心入口：

```csharp
public static void Initialize(IDataProvider provider);

public static void SetRootDirectory(string rootDirectory);

public static TTable GetTable<TTable>()
    where TTable : class, IDataTable;

public static void Shutdown();
```

这些是 `DataRuntime` 成员签名，不是独立可编译代码。启动时先 Initialize，重复初始化或未初始化就配置、读取都会报错。GetTable 自动获取 SG 为该类型准备的工厂，加载并缓存表。缺少生成工厂时明确报错。不同程序集中的表使用同一入口，跨类型相同逻辑表名在请求第二种表时直接报错。同一类型再次请求返回缓存。

```csharp
using DE.Share.Data;
using DE.Share.Data.DataProvider;

// 启动时执行一次。
DataRuntime.Initialize(new ExcelDataProvider());
DataRuntime.SetRootDirectory(configRootDirectory);

// 任意业务位置读取配置。
SpaceDataTable spaces = DataRuntime.GetTable<SpaceDataTable>();
DataTableKey key = DataTableKey.FromInt32(1001);
SpaceDataRow space = spaces.GetRow(key);

// 使用 space 中的配置创建或初始化业务对象。

// 在应用或该套配置的生命周期结束时调用。
DataRuntime.Shutdown();
```

一次 `GetTable<SpaceDataTable>()` 的处理顺序：

1. 检查 Runtime 已初始化、根目录已配置。
2. 已有缓存则直接返回同一表实例。
3. 自动准备该类型的工厂，检查并预留逻辑表名，调用加载委托，由 Provider 打开对应文件并绑定列。
4. 生成的行工厂构建 `SpaceDataRow`，逐条加入临时字典。
5. 完整读取并校验成功后，构造 `SpaceDataTable`，写入缓存并返回。

首版同步加载，同一张表在成功加载后始终返回缓存实例。调用方在同一线程使用所有入口，当前不提供并发访问保证。递归加载同一张表会明确报错；暂不实现异步加载或取消。

加载失败不发布部分表，记录该表类型的异常及原始异常上下文，保留已经占用的逻辑表名。当前运行周期内不可重试：后续 `GetTable` 直接重新抛出记录的错误，不再调用工厂或打开数据源，也不能通过同名的另一种表类型绕过失败状态。其他表仍可正常加载。空表在表头合法时成功，缺失文件或 Sheet 不能当作空表返回。`Shutdown` 清空表名占用、已加载表和失败状态，并重置 Provider、根目录，允许随后重新初始化。类型工厂的描述和加载方法跨 Shutdown 保留，不依赖静态构造函数再次执行。重复 Shutdown 安全；加载期间调用会报错，避免重入清理。未重新初始化前不能使用其他入口；已经交给调用方的表仍是有效的只读内存快照，不会被就地清空。

## 9. Excel 约定与类型校验

首版采用以下明确约定：

| 项目 | 规则 |
| --- | --- |
| 主键 | Excel Id 列保存 int；读取后封装为 DataTableKey，重复检查使用其值相等语义 |
| 工作表 | 精确匹配描述中的 Sheet 名，不默认退回第一个 Sheet |
| 第 1 行 | 列名，例如 `Id`、`Name`、`MaxPlayers` |
| 第 2 行起 | 数据行；不另设类型行或注释行 |
| 列名 | Ordinal 区分大小写；不自动裁剪空格，重复非空表头报错 |
| 必需列 | 所有描述列必须出现；无列名但存在数据的列报错 |
| 额外列 | 有名称但不在当前描述中的列忽略，便于附加说明或两端各用部分列 |
| 空行 | 所有单元格都空时跳过；其他列有值而 Id 为空时必须报错 |
| 类型 | `int`、`uint`、`long`、`ulong`、`float`、`double`、`bool`、`string`、枚举及可空值类型 |
| 空单元格 | 可空值类型得到 null；string 得到空串；其他非可空值类型报错 |
| 整数转换 | 必须是整数且在目标范围内；禁止截断小数或溢出回绕 |
| 数值文本 | 使用 InvariantCulture；不依赖 Windows 或 Unity 当前区域设置 |
| 浮点数 | 拒绝 NaN、Infinity 和目标类型范围之外的值 |
| 布尔值 | 允许 Excel 布尔值或文本 true/false，文本忽略大小写；不接受 0/1 |
| 枚举 | 名称区分大小写，或底层整数的已定义值；首版不支持 Flags 组合 |
| 重复 Id | 整表加载失败，错误包含重复值及前后两条记录的位置 |
| 大整数 | 超过 15 位十进制有效数字的整数要求以文本保存；拒绝读取该范围的数值单元格，避免沿用已经丢失精度的值 |
| 公式 | 不计算公式；只消费文件中已保存的结果，不保证缓存结果新鲜，出表前须重算并保存 |

字符串保留原始空格，不按单元格显示格式转换内容。合并单元格、日期对象、数组、列表、字典和复杂对象不作为首版数据类型；错误值不能静默变成 0、false 或空字符串。

示例 `SpaceData.xlsx` 的 `SpaceData` 工作表：

| Id | Name | MaxPlayers |
| --- | --- | --- |
| 1001 | MainCity | 200 |
| 1002 | Arena | 10 |

## 10. 后续 attribute 策略

`DataTableAttribute` 首版只承载表与资源映射。当前固定语义为按需加载、成功后常驻当前 Runtime；不要提前暴露尚未实现的策略参数，避免配置被静默忽略。

未来可在同一 attribute 上增加以下参数，示例仅表示扩展方向，不属于首版 API：

```csharp
[DataTable("SpaceData",
    Load = DataLoadPolicy.Preload,
    Cache = DataCachePolicy.KeepAlive)]
public sealed partial class SpaceDataRow : DataRow
{
    // 业务配置属性。
}
```

加载策略回答“何时加载”，缓存策略回答“加载后的实例保留多久”。SG 将策略写入描述，`DataRuntime` 解释并执行，Provider 和生成的字典不各自维护策略。

后续如果增加 Preload 策略，需要额外定义可发现的预加载表清单及触发入口；当前按需模型不枚举尚未访问的表。按需表保持首次 Get 时加载。缓存先考虑 `KeepAlive` 与 `None`，有实际需求再增加限量淘汰。预加载配合 None 没有保留效果，应明确拒绝该组合。

缓存移除仅代表管理器释放引用；调用方持有的旧表仍可用。`GetRow` 保持统一的按键查询入口，具体缓存策略不另行暴露字典访问方式。自动刷新、文件监视、逐行淘汰和热更新一致性协议不包含在本次设计中。

## 11. 后续二进制数据源

后续增加 `BinaryDataProvider` 和独立 Excel 导出工具，复用 `DataTableDescribe` 的列模型和首版类型校验规则。导出工具在运行时读取 Excel；SG 始终只负责从 C# 生成结构与读取代码。

未来二进制文件需要记录 magic、格式版本、逻辑表标识、schema 指纹和行数，读取时先验证再构建字典。schema 指纹基于稳定规范：有序列名、类型、可空性、主键与枚举定义；不得使用进程相关的 `GetHashCode`、程序集构建版本或 Excel 物理列位置。格式规范和写入器在该阶段实现，首版不添加空壳 Provider。

二进制 Reader 按相同的逻辑列序号提供强类型 getter，生成的表加载方法无需更换。Excel 单元格转换只在导出时执行，运行时直接解码相应类型。不同编译端若通过条件编译得到不同描述，应分别导出匹配的二进制文件；单一文件兼容多种 schema 不在首版承诺中。

## 12. 实现顺序与验收

实现顺序为：描述与只读表契约 → Excel Provider 与严格转换 → DataTableSG → Runtime 按需创建与缓存 → 双端接入及 SpaceData 示例。每一部分都围绕同一条“读取 SpaceData.xlsx 并通过 GetRow(key) 查询”的链路验收。

| 验收范围 | 必须验证的行为 |
| --- | --- |
| DataTableKey | 相同值的相等/哈希一致；不同值可区分；零和默认值一致；int 边界值通过 GetRow 可查询；不接受其他键类型或隐式转换 |
| 查询入口 | GetRow 命中返回对应行，未命中错误包含表名和主键；索引器访问、传入原始 int，以及转换为公开字典接口均不能编译 |
| SG | 示例行能生成并编译；生成成员的访问权限正确；非法类型、重名和重复列映射给出编译诊断；重复运行产物稳定 |
| Excel | 真实 xlsx 的表头乱序、字符串、整数、空值、额外列；缺 Sheet/列、重复表头/Id、错误类型、溢出和大整数文本 |
| Runtime | 静态入口重复请求返回缓存实例；加载失败后直接报错，不再次加载；失败不发布半表；Shutdown 后可重新初始化，旧缓存和失败状态不混入新配置 |
| 根目录 | 未配置报错；成功设置后禁止再次设置，失败可重试；Shutdown 后可重新设置；拒绝越界资源名；文件路径与 Sheet 报错可定位 |
| 资源释放 | 成功和失败后均能重新打开文件；Shutdown 后未重新初始化的调用报错，已返回快照仍可查询 |
| Provider 替换 | 测试内存 Provider 通过同一 Reader 契约生成相同表，确认加载器不依赖 Excel 专有对象 |
| 服务端 | 构建实际 `Server/Framework/Framework.sln`；首版实现时已验证数据模块和 SG，按用户要求暂不保留新增的两个测试工程 |
| Unity | 导入运行时依赖和 SG 后由 Unity 实际编译，并读取同一份示例 xlsx；有 IL2CPP 发布需求时在目标构建中验证，服务端通过不能替代该验收 |

本次设计与已有 `Docs/DataProjects.md` 的共享源码和引用规则衔接。当前工作区已经存在的数据工程调整属于设计依据，本文不要求重置或覆盖这些改动。
