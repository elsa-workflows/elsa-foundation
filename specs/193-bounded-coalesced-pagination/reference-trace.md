# Current-Head Coalesced Reference Trace (#2392)

**Status**: Pending host execution. The program lead reports shared machine load around 200 on 8 cores and has instructed workers to hold builds. This file records the approved reproducible reference definition and the exact missing evidence; it is not a captured trace and does not satisfy #2392's trace acceptance yet.

## Candidate and fixture

- Candidate SHA: `7b8e5d1304c198ce80dcc3d17aed5598245c7352`.
- Provider: PostgreSQL 16, in a uniquely named disposable container and database, once host execution is released.
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
| Workbench build command and result at candidate SHA | Pending: build held under shared-load instruction |
| Unique disposable PostgreSQL 16 container/database identity | Pending: container not started |
| Host effective persistence provider and connection reference | Pending: host not started |
| Trigger `traceparent` and timestamp | Pending: request not sent |
| Executable export `intrinsicKind: "Set"`, `activityContract` omitted-or-null | Pending: read after host start |
| HTTP status and body | Pending: request not sent |
| Instance id and terminal state | Pending: request not sent |
| Per-run `checkpointCadence`, `maxSegmentCheckpoints`, `inspectionGranularity` | Pending: detail read not requested |
| EF command output, split by trigger request and post-response work | Pending: logging capture not started |
| Cleanup of only owned host process and disposable container/database | Pending: resources not started |

The original workload is not a gate for this representative run. This trace, when captured, remains explicitly non-equivalent to the earlier custom-transform workload and cannot validate historical counts or latency. No source-level claim is substituted for the pending runtime evidence.
