# Table-Owned Provider Implementation Plan

> **For agentic workers:** Use subagent-driven-development for the isolated Excel provider task; integrate runtime and generated factories in the current task. Review the completed contract and implementation.

**Goal:** Each table owns an independent provider; remove DataTableLoader and reuse one Excel file/reader per table until failure or shutdown.

**Architecture:** DataRuntime stores `Func<IDataProvider>`, not a provider instance. The generated table factory passes that delegate to DataTable, which creates and owns its provider, lazily opens a reader, scans/reset it, and disposes the provider. ExcelDataProvider owns its workbook reader and file; data readers expose Reset rather than IDisposable.

**Tech Stack:** C# 9, .NET 10, Unity 2022.3, ExcelDataReader 3.8, Roslyn 3.8.

## Contract

- `DataRuntime.Initialize(Func<IDataProvider> providerFactory)`; each invocation must produce a new independently owned provider. Startup becomes `Initialize(() => new ExcelDataProvider())`.
- `IDataProvider : IDisposable` retains `OpenTable(string rootDirectory, DataTableDescribe describe)`. It returns a provider-owned reader. The provider binds to one source/description; it cannot be rebound. OpenTable is called once by each table. Provider Dispose is idempotent.
- `IDataTableReader` no longer inherits IDisposable; adds `void Reset()` to position before the first data row of the selected sheet. Initial OpenTable returns this same position.
- ExcelDataProvider keeps one ExcelDataTableReader and underlying workbook/file alive. Repeated OpenTable with the same binding returns the same cursor without reopening; another binding is rejected. Failed open is remembered and not retried. Dispose invalidates reads and releases the entire resource chain.
- ExcelDataTableReader.Reset uses the underlying reader's Reset, selects the original worksheet and skips its header; reuse validated immutable column mapping. Retain contextual errors and reset current-row state/row numbers. No all-cell mirror or random-access claim.
- Move DataTableLoader scanning, count and duplicate-key checks into private DataTable methods. Keep atomic batch publication; scans do not dispose readers. DataTable owns provider creation/release, retained reader and per-table failure state.
- Full still loads during GetTable, Row opens only on Count/GetRow. Lru eviction triggers another scan/reset, never another file open. KeepAlive and Full+KeepAlive prewarm behavior remain.
- On any open/reset/scan/materialization failure, record the first exception and immediately release the provider. A cleanup failure must not mask the original read error. Later accesses throw the same saved error and do no IO.
- Shutdown rejects active scans and shutdown reentry, attempts to detach/dispose every table even if one Dispose fails, clears runtime state, then reports cleanup errors. Old cached rows and known Count remain usable; old handles cannot reopen after shutdown.
- Preserve static single-threaded runtime, one successful root assignment, lazy generated factories, no registration/indexers, no shard strategy. The previously discussed non-generic DataTable redesign is outside this change.

## Tasks

- [x] Add failing contract and lifetime probes without new test projects: runtime factory overload, provider IDisposable, reader Reset, no DataTableLoader, distinct providers, single OpenTable, reset counts, cleanup on failure and shutdown.
- [x] Implement provider/reader contracts and Excel lifetime/reset behavior in the canonical shared DataProvider sources.
- [x] Change Runtime, DataTable and DataTableFactory to pass provider factories, move scanning into DataTable, and remove DataTableLoader.cs plus its Unity meta.
- [x] Update SG factory/constructor signatures and both server/Unity initialization call sites; update current design/use documentation.
- [x] Build Framework.sln; run existing tests and in-memory lifetime/policy probes. Validate a real multi-sheet xlsx with repeated Reset, Lru reread and file handle release.
- [x] Sync the SG plugin and validate actual Unity Excel/prewarm/reader reuse. Review cleanup/error paths and check the final diff.

## Verification

Use provider/reader counters to verify distinct instances per table, no IO on Row handle creation, one OpenTable across Count and repeated Lru misses, Reset before rescans, complete row counts and global duplicate-key checks. Capture exceptions and ensure later GetTable/GetRow/Count preserve the first failure with no retry. Fault provider disposal and verify all tables are still detached and runtime can reinitialize. For real Excel, read multiple passes including a non-first sheet, check row numbers and strict conversions, and verify the file remains held during table lifetime and can be opened exclusively after Shutdown/failure. Existing Unity validation must continue to verify synchronous and caller-scheduled Prewarm against SpaceData.xlsx. No new tests projects or speculative binary provider.

### Results (2026-09-10)

- Framework solution build: 0 warnings, 0 errors. Existing tests: 6 shared and 3 server passed.
- Temporary contract probe passed; temporary runtime lifetime/policy probe passed 73 assertions. No test projects were added.
- Real Excel provider probe passed 10 groups, including non-first worksheet resets, file ownership, sticky failures and cleanup fault injection.
- Synced SG plugin matches the built DLL hash. Unity 2022.3 batch validation exited 0 and verified both prewarm paths plus Row/Lru reader reuse against SpaceData.xlsx.
- Review exposed server teardown skipping managed fields, Current and native callback reset after a provider disposal failure. The fix always resets that state and preserves the original initialization exception when cleanup also fails. A temporary server probe reproduced 6 failures before the fix; all 11 checks passed afterward.
- Final whitespace check passed.
