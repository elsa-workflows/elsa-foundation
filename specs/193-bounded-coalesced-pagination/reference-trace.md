# Current-Head Coalesced Reference Trace (#2392)

**Status**: First normal-host capture completed on 6 October 2026. HTTP correctness and effective configuration passed; causal command accounting is not yet accepted. The retained logs reveal readback trace-header reuse and missing intermediate engine span parents. The scoped capture correction is reviewed, parsed and locally committed; its host run and retained span metadata are still required. Timing remains held under excessive shared load.

## Candidate and fixture

- Built candidate SHA: `5d28bd0cd3d76004988a01b0f0abd7af3af313d6`; runtime source matches merged main `43b3ef51882105916380ac8e539b49ad7b8a37b6`. Script checkout was the same SHA for the first capture.
- Provider: PostgreSQL 16.15, image `postgres:16-alpine`, in a uniquely named disposable container and database. Persisted diagnostics used a separately bound, task-owned SQLite database; legacy connection `Elsa` remained SQLite in the owned temporary root.
- Host: source-run `src/apps/Elsa.Workbench/Elsa.Workbench.csproj`; only the task-owned process on a verified free loopback port.
- Checkpoint configuration: Workbench Coalesced, `MaxSegmentCheckpoints=50`; record per-run `checkpointCadence`, `maxSegmentCheckpoints`, and `inspectionGranularity` from instance detail.
- Definition: `RuntimeDbPaging2392Reference`; root Sequence → synchronous POST HttpEndpoint → `SetVariable` (`elsa.intrinsic.set@1`, kind `Set`) → `WriteHttpResponse`.
- Route and payload: `POST /workflows/http/runtime-db-paging-2392/transform`, `{"firstName":"Alice","lastName":"Smith"}`.
- Expected response: HTTP 200, `Alice Smith`.
- Computation classification: compiler intrinsic, not the historical CLR transform. The published JSON export should record `intrinsicKind: "Set"`; `activityContract` may be omitted or null because `JsonPayloadSerializer.BuildOptions` uses `JsonIgnoreCondition.WhenWritingNull`. The compiler's in-memory `ActivityContract` is a separate null value. `WorkflowIntrinsicFusion.IsFusable(Set)` returns true in the primary-source graph at the candidate. A `SideEffectProfile` is not applicable to this intrinsic.
- Evidence read permissions: literal `workflow-publishing.read` for executable export and `workflow-runtime.read` for instance detail.

## Captured evidence

| Evidence | Status |
|---|---|
| Workbench build command and result at candidate SHA | Passed: wrapper-mediated `dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -m:1 --disable-build-servers --verbosity minimal`; 101 warnings, 0 errors |
| Supplied `HostCandidateSha` matched to the independent host build record, with script checkout recorded separately | Passed: both `5d28bd0…`; DLL SHA256 `05c8eb6ac3aac744cb4878678d90dfd27dce9b37d139422ec1dba0f0a38bb08d` |
| Unique disposable PostgreSQL 16 container/database identity | Captured: `elsa-runtime-db-2392-31380815b290`, database `runtime_db_2392`; image and container identities in the evidence record |
| Host effective persistence provider and connection reference | Captured: selected runtime resource PostgreSql / `ElsaRuntime2392`; diagnostics explicitly SQLite / `ElsaDiagnostics`; legacy `Elsa` retained as temporary SQLite |
| Trigger `traceparent` and timestamp | Captured: TraceId `5d5f2e25615468083b27efd93bea90e0`, POST SpanId `4813cde904ac27d1`, start `2026-10-06T01:06:29.489Z`, transport finish `01:06:31.365Z` |
| Executable export `intrinsicKind: "Set"`, `activityContract` omitted-or-null | Passed: Set intrinsic has omitted null contract; Sequence explicitly ReplaySafe; HttpEndpoint and WriteHttpResponse omit the default External profile, consistent with the pinned contract constructor and `WhenWritingDefault` attribute |
| HTTP status and body | Passed: HTTP 200, `Alice Smith` |
| Instance id and terminal state | Passed: `147dNKzfuw6`, Completed; published artifact `artifact-bc9dfcd40653`, version `147dN9dDKY1` |
| Per-run `checkpointCadence`, `maxSegmentCheckpoints`, `inspectionGranularity` | Passed: Coalesced, 50, boundary-level |
| EF command output, split by trigger request and post-response work | Partial: 288 same-trace command events; 130 reach the POST parent chain (80 touch diagnostic tables), 147 lack intermediate parents, 5 belong to instance listing and 6 to detail inspection. Do not report 288 or 277 as a causally established POST count |
| Cleanup of only owned host process and disposable container/database | Passed: owned process stopped, container/volumes and owned temporary content root removed; existing `elsa-demo-pg` and `elsa-demo-postgres` remained running |

The [sanitized first-capture record](evidence/http-reference-2026-10-06-first.json) retains identity, build/DLL proof, observed result, artifact/profile/cadence readback, qualified command counts, and hashes/sizes of retained local artifacts. [Sanitized events](evidence/http-reference-2026-10-06-first-events.jsonl) retain command text without parameter data and the trace/span anchors used to recalculate the counts. Raw logs and exports remain in the protected task capture directory outside Git. No sensitive parameter logging was enabled.

PowerShell persisted the POST's explicit traceparent on the shared session, so subsequent inspection GETs reused the TraceId with different request SpanIds. The reviewed correction uses a separate authenticated session for the trigger. The first capture also did not retain persisted engine spans before cleanup: timestamp proximity cannot substitute for their missing parent links. T02 owns request/caller accounting and T04 owns causal post-response/settled-work accounting. Known POST descendants continue after transport completion; no settled boundary is claimed here.

Setup failures were separate from this successful request. One activation attempt routed a PostgreSQL string named `Elsa` to a SQLite consumer and failed before any workflow trigger; another found the container ready before the target database existed. Both cleaned only owned resources. The successful attempt used a distinct runtime connection and required a successful `SELECT 1` against the target database. These are operator setup corrections, not causal repairs of an engine performance defect.

The single response duration is a cold diagnostic observation with EF/transaction logging and persisted diagnostics under heavy shared load. It is not a warm performance baseline or a before/after timing comparison. SQL statements and provider round trips have not been counted. The 13 same-trace transaction sequences include diagnostics and unresolved ancestry; they are not a logical-checkpoint count.

The original workload is not a gate for this representative run. This trace remains explicitly non-equivalent to the earlier custom-transform workload and cannot validate historical counts or latency. Source-level mechanisms do not substitute for the pending causal accounting evidence.
