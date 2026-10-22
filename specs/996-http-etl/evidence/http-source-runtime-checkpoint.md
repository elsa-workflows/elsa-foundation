# HTTP source runtime and pagination checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), single draft
[PR #3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156). This completes the bounded decoding and
pagination work in T007 and implements the source-acquisition portion of T009 plus transport dependencies of
T017. It does not enable connector execution in the worker or complete those latter tasks.

## Implemented boundary

- A request builder uses the exact admitted source binding and shard seeds. Token pagination preserves unrelated
  query parameters, replaces exactly one named parameter and rejects ambiguous encoded duplicates. Next links
  resolve against the current page, obey the pinned origin/path policy and retain a bounded canonical absolute
  continuation. Seed/current cycles are rejected before another GET; the existing durable request history rejects
  historical cycles. Raw response bytes remain unchanged in the cache.
- The transport performs real bounded GET and JSON POST requests, with per-attempt deadlines, request-local
  authentication, disabled redirects/cookies/proxies, and no automatic response draining after application bounds
  are reached. GET retains only complete bounded 200 bodies; POST retains the exact body/effect header and returns
  safe response metadata for durable interpretation. Caller cancellation remains cancellation. Approved custom
  authentication schemes may add bounded header/query fields while authored request state remains protected.
  Shared header policy keeps binding approval and runtime transport consistent.
- Source acquisition decodes only the admitted artifact, reads the exact cache before and after permit allocation,
  and dispatches only a newly applied permit. Existing attached pages never refetch. Cache publication before SQL
  capture is recoverable by a successor using the original attempt, fence and receipt time. A predecessor that
  loses its lease can publish its response but cannot attach it; the successor can reconcile it without another
  GET. Missing/corrupt attached bodies fail without silently refetching. An unavailable approved storage root is
  an error; the preparation/host must initialize it before acquisition.
- Complete malformed 200 responses retain their timely receipt timestamp in the source journal. Parsing or journal
  I/O after attempt expiry does not turn a known invalid page into an uncertain retry. The store validates UTC,
  the original attempt/deadline window and reconciliation time; current ownership and exact permit checks remain.
  Untimed legacy outcomes retain their original expiry behavior. Pre-dispatch metadata headroom covers both a
  timed invalid receipt and a Retry-After outcome at the exact budget boundary.

## T007 acceptance map

| Criterion | Executable evidence |
| --- | --- |
| JSON Pointer syntax, escapes and canonical array indices | `EtlJsonPointerTests`, compiler/reader tests |
| Exact numeric/null/missing values, duplicates, UTF-8, depth, record/page limits and empty pages | `EtlJsonPageReaderTests` |
| Token escaping/replacement, next-link resolution, endpoint policy, normalized size, seed/current cycles | `EtlHttpRequestBuilderTests` |
| Durable page ceilings, empty-page progression and historical loops | `EtlConnectorHttpPageStoreTests`, `EtlConnectorPageHistoryTests` |
| Real token/relative-next-link paging and reopening original cached bytes | `Source_acquisition_*` tests in `EtlHttpSourceAcquisitionTests` |

## Review and validation

Four bounded root/independent review passes corrected ambiguous query handling, normalized seed cycles,
replayed-permit dispatch, cache reconciliation ordering, invalid-page timing, automatic response cleanup,
cancellation handling and caller/admission limit divergence. A proposed extra body copy was withdrawn after
checking that the owned `ByteArrayContent` buffer is not publicly mutable; content replacement remains explicitly
rejected. Test setup corrections covered nonconstant defaults/imports, valid enclosing retry/page limits, and
creation of the approved cache directory. No runtime invariant was relaxed to satisfy a fixture.

The focused suite passed 70 tests before the final small admission-derived-pipeline and teardown corrections;
the full restored-source ETL suite then passed **908 tests, zero failures/skips**, including those corrections.

| Final gate | Result |
| --- | --- |
| `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore` | 908 passed, 0 failed, 0 skipped |
| `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore` | 584 passed, 0 failed, 0 skipped |
| Maps generator `all`, `solution-filters`, `check`, `solution-filters-check` | Passed; regenerated files and findings reports are byte-identical |
| `dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-restore` | Passed, 0 errors; existing package/analyzer warnings remain |
| `pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1` | Passed on an owned rebuilt Workbench with isolated storage; persisted file Sequence and Flowchart each continued |
| Relative evidence links and `git diff --check` | Passed before commit |

Each Workbench file run retained two source partitions, two accepted records, one rejected record and one filtered
record, with two main JSONL chunks and one error chunk. The owned process was stopped afterward. This is v1 file
compatibility evidence, not an HTTP worker/native continuation proof. Fresh-head CI remains pending after push.

Three separate regression controls removed cached-page reconciliation, timely invalid-receipt acceptance and
redirect refusal. Each failed its intended test; SHA-256/byte comparisons confirmed exact restoration before final
verification. Tests use real loopback HTTP and SQLite, including separate EF contexts for successor ownership.
They do not constitute two-process worker recovery proof.

## Remaining acceptance and limits

The existing worker still reports connector execution unavailable. T009 must compose typed preparation/batches,
retained route bodies, HTTP/file delivery and acceptance with the shared heartbeat/capacity/terminal infrastructure.
HTTP-to-JSONL, HTTP-to-HTTP, CSV-to-HTTP, mixed routes, full native Sequence/Flowchart continuation, Studio,
observed host telemetry, two-process interruption and frozen old-worker resume remain owed. T017 is not complete
merely because transport tests pass. The full task count is **6/22**, with T007 complete.

Source cache recovery here uses controlled interruption points and replacement stores, not stopped-process proof.
Lease expiry is not physical termination; external effects remain at least once. Historical receipt/HTTP timeout
causes remain unproved. The durable task runner and workflow-scoped pause remain post-program follow-up.
