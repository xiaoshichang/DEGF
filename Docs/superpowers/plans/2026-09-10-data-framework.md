# Data Framework Implementation Plan

## Non-retryable load failure follow-up (2026-09-10)

The user changed failed-load behavior: each table type may attempt loading only once per initialized runtime. A failed attempt stores its ExceptionDispatchInfo; later GetTable calls rethrow the saved error without opening the provider or calling the loader again. Keep the failed table's logical name reserved to prevent bypass through an alias type. Shutdown clears failures together with table state. Root-setup validation failures still permit correcting the configuration. This supersedes retry behavior in the historical sections below.

- [x] Record failures in DataRuntime, rethrow them before new load attempts, and clear them at Shutdown.
- [x] Verify one provider call despite repeated GetTable after failure, original error preservation, isolation from other tables, duplicate-name rejection and clean initialization after Shutdown. Update current docs and run the available builds/Unity validation; add no test projects.

Non-retryable verification: full Framework.sln build passed with 0 warnings/errors; all 9 existing tests passed on the fresh build. The user's previously incomplete SpaceEntity statement is now complete, so the old build blocker no longer applies. Temporary in-memory checks confirmed one provider call across repeated failed requests, preserved exception identity/stack, unaffected healthy tables, blocked aliases and successful loading after Shutdown/reinitialization. Unity 2022.3.62f3c1 validation completed with exit code 0. No test project or fixture files were added.

## Lazy table creation follow-up (2026-09-10)

The user requested removal of explicit table Register calls. Startup now configures only DataRuntime's provider and root; GetTable<TTable>() automatically obtains the generated table factory, loads on first use and caches successful results.

- [x] Add an internal generic DataTableFactory<TTable> retaining immutable description and loader metadata. A generated static constructor binds the factory through a protected DataTable<TRow>.InitializeFactory helper; RuntimeHelpers.RunClassConstructor activates it on first access. No public registration, assembly scanning, reflected member lookup or public table constructor is required. Factory metadata survives Shutdown; provider/root and loaded tables do not.
- [x] Remove DataRuntime.RegisterTable and its registry. Reserve logical table names while loading, reject cross-type duplicates and recursive loads, and release failed reservations so retries work. Keep static single-thread use and one successful root assignment per initialization.
- [x] Remove generated Register() and both startup/Unity Register call sites. Update current docs; retain GetRow(DataTableKey) as the only row lookup API.
- [x] Rebuild/sync the generator, validate lazy creation/cache/failure retry/shutdown and separate data assemblies using temporary checks (no new test projects), run Unity validation, and review the implementation. Report the known unfinished SpaceEntity statement if it still blocks the full solution build.

Lazy-creation verification: data projects and SG build passed with 0 warnings/errors; the imported Unity analyzer hash matches the built DLL. Unity 2022.3.62f3c1 compiled and executed the existing validation without any Register call, exit code 0. Temporary .NET 10 checks passed for metadata before initialization, absence of registration/public constructors, no eager reads, core Space and Demo Item loading, cache identity, duplicate names/missing factories, surviving snapshots, reinitialization and retries after I/O, recursive-load and shutdown-during-load failures. Temporary fixtures were removed; no test project was added. Independent read-only review found no required fixes. The full solution build remains blocked solely by the preserved unfinished assignment in SpaceEntity.cs:16.

## Cluster startup follow-up (2026-09-10)

The user requested cluster-config data-root propagation and completion of ManagedRuntimeState._InitDataRuntime. Use the existing native-to-managed config-path handoff: no new native ABI field is needed. The top-level JSON dataRoot is required, accepts absolute paths and resolves relative paths against the cluster-config directory.

- [x] Add DataRoot parsing, normalization and directory validation to ManagedClusterConfig; configure local-dev.json with ../../Data/Excel.
- [x] Complete _InitDataRuntime with ExcelDataProvider, the resolved root and SpaceDataTable.Register(). Track ownership so normal shutdown and failed startup clear only this runtime's data initialization; preserve the user's current initialization guard and edits.
- [x] Update usage docs. Verify relative/absolute/missing/invalid roots without connecting to MongoDB, build the C# solution and report any unrelated existing compilation blocker. Keep the removed data test projects removed.

Cluster follow-up verification: compiled the actual ManagedClusterConfig and DatabaseConfig sources in a temporary PowerShell/.NET 10 process and checked the repository configuration, seven dataRoot cases (relative, absolute, missing, empty, whitespace, nonexistent, file instead of directory), and a relative config-file path. All checks passed and temporary fixtures were removed; no MongoDB connection was opened. Framework.sln build was attempted; the data projects and generators compiled, but the preserved unfinished SpaceDataTable assignment in SpaceEntity.cs:16 still blocks DE.Server and full startup verification.

> Follow-up (2026-09-10): At the user's request, the two newly added data test projects were removed, including their source files and solution entries. The completed steps and 135-test result below record the initial implementation verification before removal; those two test projects are no longer part of the repository. Existing shared/server tests remain.

> Naming follow-up (2026-09-10): Logical table names now use the Data suffix: SpaceData and ItemData. The sample workbook and sheet were renamed to SpaceData.xlsx and SpaceData. C# type names remain SpaceDataRow/SpaceDataTable and ItemDataRow/ItemDataTable. References to Space.xlsx below describe the original implementation verification before this rename.

## Static runtime follow-up (2026-09-10)

Root setup follow-up: removed the started field. SetRootDirectory now rejects any call after the first successful assignment, including the same path and calls before any table load. Failed setup can be retried; Shutdown clears the root and permits setup after reinitialization.

The user requested a static DataRuntime and no multithreading support. This supersedes the instance, disposal and concurrency design in the original implementation record below. No new test projects will be added.

- [x] Convert `DE.Share.Data/DataRuntime.cs` to a static class. Replace construction with `Initialize(IDataProvider)` and instance disposal with `Shutdown()`. Keep one provider, root, registration set and successful-table cache. Reject use before initialization and duplicate initialization. Preserve root freeze, duplicate registration, failed-load retry and recursive-load checks. Remove locks. Reject shutdown during a load to prevent reentrant reset; shutdown clears configuration and cached references so initialization can run again.
- [x] Change `DE.Share.DataTableSG/SourceEmitter.cs` to emit parameterless `Register()` calling `DataRuntime.RegisterTable<TTable>(TableDescribe, Load)`.
- [x] Update `Demo.Data.Editor/DataFrameworkValidation.cs` to use the static API, verify cached table identity and clean up in finally. Update current design and usage documentation.
- [x] Run `sync_data_plugins.ps1` to rebuild and copy the analyzer. Build Framework.sln, run the existing shared/server tests, and run Unity's existing validation entry point against SpaceData.xlsx. Check for obsolete instance call sites and matching analyzer DLL hashes.

Static follow-up verification: `dotnet build Server/Framework/Demo.Share.Data/Demo.Share.Data.csproj --no-restore -v minimal` passed for both data assemblies and SG with 0 warnings/errors. Unity 2022.3.62f3c1 compiled and executed the validation with exit code 0, checking actual Excel loading, cached identity, two initialization/shutdown cycles and surviving snapshots. The Unity analyzer hash matches the built generator, and current source/docs have no old instance call sites. The full solution build was attempted but is blocked by the user's unfinished `_dataRow = SpaceDataTable.` statement in `DE.Server/Entities/SpaceEntity/SpaceEntity.cs:16`, which was preserved. The existing 9 tests passed with `--no-build --no-restore`; the server test binaries came from the previous successful build, so this does not establish a fresh server build.

> For agentic workers: use subagent-driven-development for isolated generator work and reviews; implement the shared contracts and integration in this task. The user approved implementation on 2026-09-10.

**Goal:** Load SpaceData.xlsx through a replaceable provider and query SG-generated SpaceDataTable exclusively with GetRow(DataTableKey).

**Architecture:** Shared C# source remains under Unity Assets and is linked by server projects. Runtime owns registration, root directory and successful table snapshots; SG generates metadata, direct row materializers and table registration. Excel is an adapter behind IDataProvider/IDataTableReader.

**Tech Stack:** C# 9, .NET 10 server, Unity 2022.3, Roslyn 3.8 / netstandard2.0 generator, ExcelDataReader 3.8.0, xUnit.

## 1. Core contracts and runtime

Files: `Client/Demo/Assets/DEFramework/Scripts/DE.Share.Data/DataDescribe/*.cs`, `DataProvider/IDataProvider.cs`, `DataProvider/IDataTableReader.cs`, `DataTable.cs`, `IDataTable.cs`, `DataRuntime.cs`, `DataTableLoader.cs`, `DataLoadException.cs`; `Server/Framework/DE.Share.Data.Tests/{DE.Share.Data.Tests.csproj,CoreTests.cs}`.

- [x] Add behavioral tests for key equality/boundaries, GetRow and missing keys, immutable metadata, registration, single-load concurrency, failure retry, root freeze and disposal.
- [x] Run `dotnet test Server/Framework/DE.Share.Data.Tests/DE.Share.Data.Tests.csproj -v minimal`; observe missing-contract failure before implementation.
- [x] Implement the approved contracts. `DataTableKey.FromInt32(int)` and `AsInt32()` are the only integer conversions; no indexer, implicit conversion or public dictionary interface. `DataRow.Id` is DataTableKey.
- [x] Implement `DataTableLoader.LoadRows<TRow>(IDataProvider, string, DataTableDescribe, Func<IDataTableReader, TRow>)`, owning/disposal of readers and duplicate-key checks including both row locations.
- [x] Run core tests to green before integrating Excel or generated tables.

## 2. Source generator

Files: `Server/Framework/DE.Share.DataTableSG/{DE.Share.DataTableSG.csproj,DataTableGenerator.cs,RowModel.cs,RowValidator.cs,SourceEmitter.cs}` and `Server/Framework/DE.Share.DataTableSG.Tests/{DE.Share.DataTableSG.Tests.csproj,GeneratorTests.cs}`.

- [x] Test valid generation/compilation, metadata, name/column mapping, nullable scalar/enum properties, deterministic output, illegal targets/properties/names and public query restrictions.
- [x] Implement ISourceGenerator with symbol-based discovery of `[DE.Share.Data.DataTable]`, direct non-generic sealed partial DataRow types, ordered descriptors and private-setter materialization inside the partial row.
- [x] Generate sealed `SpaceDataTable : DataTable<SpaceDataRow>`, static `TableDescribe`, private constructor, `Register(DataRuntime)` and loader. Register calls `runtime.RegisterTable(TableDescribe, Load)`; loader uses `DataTableLoader.LoadRows` and passes the generated row factory.
- [x] Run generator tests to green; independently review specification compliance followed by code quality.

## 3. Excel adapter

Files: `Client/Demo/Assets/DEFramework/Scripts/DE.Share.Data/DataProvider/{ExcelDataProvider.cs,ExcelDataTableReader.cs,ExcelValueConverter.cs}`, `Server/Framework/DE.Share.Data/DE.Share.Data.csproj`, `Server/Framework/DE.Share.Data.Tests/{ExcelProviderTests.cs,XlsxFixture.cs}`.

- [x] Add real xlsx fixtures/tests (ZIP/XML fixture writer) for header reordering, strict conversion, nullable values, empty/malformed tables, source paths, sheet selection, duplicates, formula cached results, large integers and handle disposal.
- [x] Pin ExcelDataReader 3.8.0 and use CreateOpenXmlReader with explicit UTF-8 fallback encoding. Bind columns once, preserve cell locations, reject invalid values and close streams on all failure paths.
- [x] Run Excel tests to green and inspect generated-load compatibility with an in-memory provider.

## 4. Integration and verification

Files: `Client/Demo/Assets/DEFramework/Scripts/DE.Share.Data/Space/SpaceDataRow.cs`, `Client/Demo/Assets/Demo/Scripts/Demo.Share.Data/Item/ItemDataRow.cs`, both shared data csproj files, `Server/Framework/Framework.sln`, `Server/Framework/DE.Share.Data.Tests/GeneratedTableTests.cs`, `Server/Framework/Demo.Server/DemoSpaceEntity.cs`, Unity `.meta` and plugin assets, `Server/Framework/sync_data_plugins.ps1`, `Docs/DataProjects.md`, `Docs/DataFrameworkDesign.md`.

- [x] Add framework-owned SpaceDataRow, a separate Demo ItemDataRow, analyzer references and a sample Space.xlsx; test assembly ownership, generated metadata and `data.GetTable<SpaceDataTable>().GetRow(DataTableKey.FromInt32(1001))` end to end.
- [x] Adapt the existing DemoSpaceEntity constructor to forward SpaceInitializer; baseline build already fails CS1729 because its base constructor changed before this task.
- [x] Copy the matching netstandard Excel runtime DLL and SG DLL into Unity with correct import metadata and preserve dependency license. Add a reproducible sync script.
- [x] Run both new test projects, existing share/server tests and `dotnet build Server/Framework/Framework.sln`.
- [x] Verify shared runtime and emitted code using Unity's compiler/runtime when available; report actual Unity validation scope without substituting server success for it.
- [x] Review changes, update docs to implemented behavior and record test results. Leave changes uncommitted for user review; preserve pre-existing edits.

## Verification baseline

Existing tests: DE.Share.Tests 6 passed, DE.Server.Tests 3 passed. Full solution build has one pre-existing CS1729 in DemoSpaceEntity. Work in the current checkout to retain in-progress shared data project changes, on the created `codex/data-framework` branch.

## Final verification (2026-09-10)

- `dotnet build Server/Framework/Framework.sln --no-restore -v minimal`: passed, 0 warnings and 0 errors.
- `dotnet test Server/Framework/Framework.sln --no-build --no-restore -v minimal`: 135 passed (data runtime/Excel/integration 43; source generator 83; existing shared 6 and server 3); none skipped.
- Synced the final generator and ExcelDataReader 3.8.0 into Unity; the imported generator SHA-256 matches the built DLL.
- Unity 2022.3.62f3c1 batch mode compiled the updated project and executed `Demo.Data.Editor.DataFrameworkValidation.Validate`, exit code 0. The validation assembly references only DE.Share.Data and loaded the framework-owned Space table from `Data/Excel/Space.xlsx`: 2 rows, key 1001 returned MainCity with MaxPlayers 200. This verifies Unity Editor compilation and execution, not an IL2CPP player build.
- All data assets have Unity meta files; no duplicate GUIDs among the project's 199 meta files. The task's changes pass whitespace checks; an existing trailing-space line in the user's SpaceEntity edit was preserved.
- Review regression fixes cover named extra Excel error columns and extern row properties/accessors/constructors. Ownership tests ensure Space stays in DE.Share.Data and the separate Item example stays in Demo.Share.Data.
- Binary conversion/provider, configurable loading/cache policies, hot reload and async loading remain future work, as scoped in the approved design.
