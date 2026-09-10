# Data Table Policies Implementation Plan

> **For agentic workers:** Keep runtime, generated metadata and Unity analyzer synchronized; review the implementation against the current user requirements.

**Goal:** Row attributes define full/row loading and KeepAlive/Lru caching, with explicitly requested warmup for Full + KeepAlive tables.

**Architecture:** SG emits immutable policy metadata. DataTable owns row caching and sticky failure state; DataTableLoader scans providers and materializes the requested rows. DataRuntime remains static, serially accessed, root-once and registration-free.

**Tech Stack:** C# 9, .NET 10 server, Unity 2022.3, Roslyn 3.8, ExcelDataReader 3.8.

## Current design

- The user requested removal of the previously implemented shard strategy. Remove that option, its size property, metadata constructor parameter, runtime range calculation, generator parsing/validation/emission and Demo usage.
- `DataLoadPolicy.Full = 0`, `Row = 1`; `DataCachePolicy.KeepAlive = 0`, `Lru = 1`. SG validation follows these values; undefined load value 2 must be rejected.
- Attribute properties: `Load = Full`, `Cache = KeepAlive`, `CacheCapacity = 1024`. Capacity must be positive; enums must be defined. SG and runtime descriptions both validate these constraints.
- `DataTableDescribe` retains its default constructor and an overload with `(load, cache, cacheCapacity)` before columns.
- Full loads on first GetTable. Row creates only a handle on GetTable and constructs the requested row on a GetRow miss. Count always means total source rows; accessing Count before any read scans keys without constructing rows.
- Excel remains a sequential provider. Each miss scans keys to detect duplicate/malformed keys globally, but constructs only the selected rows. No random Excel I/O claim.
- LRU is per table and counts rows. Hits promote rows. Successful full batches insert newly prefetched rows at the least-recent end, preserve existing cached object identity/recency, and promote the requested row before eviction. Full + Lru re-scans/materializes the entire table on a miss. KeepAlive never evicts.
- Publish batches only after validation and reader disposal succeed. Any read failure poisons the whole table; GetTable/GetRow/Count rethrow the first exception without IO. A missing key alone does not poison the table. Eviction does not clear failure state.
- Shutdown rejects reentrant reads and detaches old handles from provider/root. Cached rows and known Count remain readable, but old handles cannot load missing rows. Returned row objects survive eviction. Reinitialize to obtain fresh state.
- `DataRuntime.Prewarm<TTable>()` accepts only Full + KeepAlive and reuses a loaded table. No registry or implicit bulk warmup.
- Prewarm is synchronous; callers choose direct invocation or await Task.Run during exclusive initialization. All operations remain serialized; finish warmup before any other access or shutdown. No locks/internal workers/parallel warmup API.
- Framework SpaceDataRow explicitly uses Full + KeepAlive; Demo ItemDataRow uses Row + Lru.

## Removal tasks

- [x] Observe a failing API contract probe: only Full/Row allowed, attribute and description expose no size property for the removed strategy.
- [x] Remove the strategy from runtime, metadata, SG and Demo; update active design and usage documentation.
- [x] Build Framework.sln, verify the API contract, four remaining policy combinations, row cache/failure lifecycle, and removed/invalid attribute diagnostics with temporary probes. Do not add test projects.
- [x] Synchronize the Unity analyzer, run existing tests and actual Unity sample/prewarm validation, and check the final diff for stale references.

## Verification commands

- `dotnet build Server/Framework/Framework.sln --no-restore -v minimal`
- `dotnet test Server/Framework/Framework.sln --no-build --no-restore -v minimal`
- `Server/Framework/sync_data_plugins.ps1 -Configuration Debug`
- Unity batchmode `Demo.Data.Editor.DataFrameworkValidation.Validate`: actual SpaceData.xlsx synchronous and caller-scheduled prewarm, cache reuse and reinitialization.

Temporary C# probes compile against the built runtime/SG without adding projects. Runtime checks cover Full/Row with both caches, LRU A/B/A/C/A/B at capacity 2, zero IO for Row handles, Count without materialization, sticky read/disposal failures, reentrant Shutdown rejection and caller-scheduled warmup. SG checks cover default/four valid combinations, undefined load value 2, invalid enum/capacity values, and source errors for removed attribute APIs.
## Removal verification completed

- Framework.sln build: 0 warnings, 0 errors.
- API probe: only Full/Row exist; removed size property absent from attribute and description.
- Runtime probe: 65 assertions passed for the four remaining combinations and cache/failure/lifecycle behavior.
- SG probe against the real runtime: default and four combinations compile; seven invalid settings rejected; removed enum member and size attribute do not compile.
- Existing tests: 6 shared and 3 server tests passed; no new test projects.
- Unity batchmode exited 0 and validated actual Excel reading, synchronous/caller-scheduled prewarm, cache reuse and reinitialization.
- Source/documentation search found no stale removed API references; git diff --check passed.
