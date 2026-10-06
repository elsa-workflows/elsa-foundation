# Coalesced HTTP reference before and after paging correction (#2392/#2393)

**Status**: Corrected pre-fix and bounded after-fix normal-host captures completed on 6 October 2026. Root and independent raw-stream QA accepted the query evidence. T05 PR #2436 is merged with post-merge CI/Maps passed; its initial unchanged-source CI failure remains preserved on #2293 without a causal repair claim. T06 implementation/host proof is local; final integration and PR gates remain pending. Timing is separate unfinished T02 work. The first capture is retained with its original limitations below.

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

## Corrected capture

The [corrected capture record](evidence/http-reference-2026-10-06-corrected.json), [sanitized events](evidence/http-reference-2026-10-06-corrected-events.jsonl) and [engine spans](evidence/http-reference-2026-10-06-engine-spans.json) retain the actual evidence. The host binary remains built at `5d28bd0cd3d76004988a01b0f0abd7af3af313d6`; the reviewed manual fixture ran at script HEAD `3c6bd3da83c49ea84c9823c8d0181b46c468f881`. The tracked delta was restricted to docs/specs/manual capture code, and the independent DLL hash matched before launch. The workload, payload and runtime source are unchanged.

- Owned PostgreSQL container: `elsa-runtime-db-2392-e1f2e52f06f7`, PostgreSQL 16.15, same image digest/resource configuration as the first capture.
- HTTP 200 `Alice Smith`; execution `147eTn6gBLT` Completed; artifact `artifact-bc9dfcd40653`, version `147eTWOLgLU`; Coalesced/50/boundary-level readback.
- POST TraceId `e5acc2e4ccc5b19b7771ec7d3ec1cd97`, request SpanId `4d76eb7a0bb77d49`, start `2026-10-06T01:21:56.478Z`, response finish `01:22:04.462Z`. No inspection GET shares this trace.
- The normal `GET /diagnostics/opentelemetry/traces/{traceId}` API (permission `Diagnostics:OpenTelemetry.Read`) exported 59 engine spans. Joining their parent links with EF scopes makes all 277 command events descendants of the POST request; no intermediate-parent gap remains in this captured trace. Export/readback calls use separate traces and are observer work.
- 197 other commands occur before response completion. Persisted OpenTelemetry diagnostics account for 80 commands: 40 before and 40 after the response. The API's engine-span ancestry establishes causality; timestamps only classify the response boundary.
- Nearest engine phase totals: request/no engine span 21, drain 88, dispatch 3, activity execution 106, checkpoint commit 59. These are parent-span categories, not exact repository-method identities. In particular, the drain category includes diagnostic writes.
- There are 66 SELECT command events touching durable-value state. Three marker INSERT command events agree with an independent owned-PostgreSQL snapshot of three immutable checkpoint rows for this execution. Stored base64 UTF-16 identifier fields were decoded and matched to the captured execution/commit IDs. The observer snapshot is outside the EF workload count. The 22 checkpoint-commit spans contain 19 Deferred and three Immediate decisions; logical decisions, SQL commands and immutable markers are distinct units.
- The owned host process, container/volumes and temporary content root were removed. The task-owned diagnostics copy, logs and API exports remain in the protected capture directory. Existing databases remained running.

This corrects capture attribution; it is not a runtime database-access reduction. The 277 POST descendants do not establish a complete settled-work total: commands lacking this TraceId, separate background consumers and diagnostic sinks still require T04's causal ledger. SQL statements and provider network round trips have not been counted. The cold, heavily loaded diagnostic response is not a comparable timing baseline.

Independent QA recomputed the curated event/span closure and count reconciliation. Root verified actual capture/configuration readbacks and the owned-PostgreSQL marker snapshot. The independent corrected-capture review did not inspect the private raw log or database; its acceptance must not be presented as doing so.

## After-fix normal-host comparison (#2393)

The [sanitized before/after census](evidence/http-after-census-2026-10-06.json) records the accepted raw-stream reconciliation. The after candidate is `c37b9d707011a01b494408019abb5707f5a02405`, Workbench DLL SHA-256 `30d632076fb90bb8d4060b359e433a0e428dfa78677a898f25639441e75d235e`; the wrapper-driven Workbench build passed with 101 warnings and zero errors. Only the shared page-merger production file changed from the runtime-equivalent pre-fix candidate. The manual fixture bytes are identical before and after (SHA-256 `17481d9502ded1a651c41cf5b217fba5a7b2c52009d935efa0915bfe5c9c51dc`). The authored graph, activity types, input expression and payload are equal; rebuilt pinned contract versions/fingerprints and publication identities are candidate-specific.

The after request returned HTTP 200 / `Alice Smith`; execution `147lC6RyDeV` and all four activities Completed. Artifact `artifact-c96f491a077d`, version `147lBwnrGCJ`, source reference `activation-ref:publication-147lBxvmhGZ`; actual readback is Coalesced/50/boundary-level. PostgreSQL 16.15 uses the same image ID and persistence composition as before, with a fresh uniquely named owned database. POST TraceId `0ecc4bc5d6689dff7a18319d9cf5e16a`, root SpanId `9e7b3fb1f04b5917`; inspection/engine-export calls and the later HTTP echo suite use separate traces.

| EF command observation | Before | After | Delta |
|---|---:|---:|---:|
| All request-root descendants | 277 | 194 | -83 |
| Leading SELECT commands | 246 | 163 | -83 |
| SELECT commands touching durable-value state | 66 | 8 | -58 |
| SELECT commands touching activity-execution state | 37 | 12 | -25 |
| Leading INSERT / UPDATE / DELETE commands | 9 / 9 / 13 | 9 / 9 / 13 | unchanged |
| Checkpoint-marker INSERT commands | 3 | 3 | unchanged |
| EF transaction / savepoint event groups | 13 / 6 | 13 / 6 | unchanged |

All 194 after commands close to the matching request root, with zero parent conflicts and zero missing `State.commandText` fields. Every other table-token touch count is unchanged. The entire observed -83 delta is SELECT-only and confined to the two tables served by the shared merger. This is consistent with the bounded page-buffer/exhaustion mechanism and its baseline-red/mutation regression proof; it remains a single before/after observation rather than exclusive causal or timing proof. Commands may contain multiple SQL statements; table-token touches can overlap. Provider round trips and exact repository-call ownership were not instrumented.

The after trace has 114 other-table commands before response and 80 diagnostics-table commands after it. Diagnostic total of 80 is unchanged, but its response split moves from 40 before/40 after to 0 before/80 after. Exported engine spans change 59→54 (drain 10→9, dispatch 17→14, activity 10→10, checkpoint 22→21), and nearest-phase ancestry redistributes. Do not infer phase-specific savings or stable segmentation. The before checkpoint-span decisions are 19 Deferred/3 Immediate; after 19 Deferred/2 Immediate. Export snapshot completeness is not independently established, and the after three marker INSERT commands do not prove three rows or a one-to-one relation with those two Immediate spans. No after PostgreSQL marker observer snapshot was captured. T02/T04 retain unresolved source/settled attribution.

The existing rebuilt-server HTTP echo suite passed 4/4 request-body, route, query and header cases. The complete runtime test project passed 2,012/2,012; restoring the exact old production source made both repeated-probe regressions fail again (7 and 4 calls versus expected 1). Root and independent Sol 5.6 High QA reviewed the implementation and independently streamed the raw before/after logs. After raw log: 115,875,911 bytes, SHA-256 `a5ae43f0c865d35d6bd3b31923d5be1a37143795d5524f8b778ecee4089965a`. Logs/exports remain protected outside Git. Root verified the owned process/container/content root and secrets were removed, with both existing user PostgreSQL containers still running.

This diagnostic run is not a latency measurement. Low-logging 25-warm-up/60-sample controls, representative concurrency/scale, final after timing, crash/recovery integration and later candidate dispositions remain program work, not passed evidence for this unit.

## First capture (preserved)

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
