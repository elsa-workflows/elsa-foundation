# Workload and reproduction plan (T01)

**Status:** bounded reference workload selected; historical original workflow/export is not a prerequisite. This is an evidence and run plan, not a claim that the historical counts have been reproduced.

## Decision and fixture identity

The owner clarified that the old custom document transform is not important to the investigation. The primary question is the normal HTTP-triggered runtime path, including endpoint startup and response delivery. Use the current-head fixture selected for #2392 as the deterministic representative and label it non-equivalent to the historical CLR transform.

| Property | Selected current-head reference (execution pending) |
|---|---|
| Candidate source | `7b8e5d1304c198ce80dcc3d17aed5598245c7352` (planning commit; parent source commit `bc94b1a3694e02acefcfd4d0229cd5624a8b3a18`) |
| Selected workflow fixture | `RuntimeDbPaging2392Reference` |
| Authored nodes | Root `Sequence` → synchronous `HttpEndpoint` → `SetVariable` compiler intrinsic → `WriteHttpResponse` |
| Trigger | `POST /workflows/http/runtime-db-paging-2392/transform` |
| Request | `Content-Type: application/json`; body `{"firstName":"Alice","lastName":"Smith"}` |
| Input binding and computation | `ParsedContent` is assigned to workflow-scope `content`; intrinsic `elsa.intrinsic.set@1` assigns `referenceText` from `getVariable('content').firstName + ' ' + getVariable('content').lastName` |
| Expected normal result | HTTP 200 with text body `Alice Smith`, then durable terminal state `Completed` |
| Planned host/provider | Current source Workbench composition; the #2392 lane will use an isolated disposable PostgreSQL 16 database |
| Static host setting | `WorkflowsRuntimeCheckpointPersistence.Mode=Coalesced`, `MaxSegmentCheckpoints=50`; per-run readback is pending |

The selected computation is a compiler intrinsic, not the historical CLR document-transform activity. The original export and transform source were not found in the supplied Downloads files or repository search. The user-approved scope removes those artifacts as a gating prerequisite. The findings report supplies the valid request body but not the original exact transformed output; the reference output above is therefore specific to this fixture.

The historical findings say the CLR transform had no `[ActivitySideEffectProfile]`; the compiled pinned contract was not supplied. Current compilation defaults an unannotated CLR activity to External, but that source rule does not establish the absent historical artifact's serialized profile. This investigation follows ADR 0032's pinned replay classification and per-run cadence readback, ADR 0020/0031's checkpoint, outbox and single-writer boundaries, ADR 0045's explicit value flow, and ADR 0073 D7's retirement of broad performance infrastructure and timing gates.

The #2392 lane owns the host build, fixture artifact and single current-head traced pre-fix capture. No #2392 run identity or measured result was observed during this T01 spike. This spike delivers the expected fixture identity and readback procedure; its completion does not depend on the sibling host run. T02/T05 must record the actual workflow execution ID, pinned profile and runtime readback before accepting that trace.

## What the historical packet establishes

The supplied report identifies image tag `elsaworkflows/elsa-foundation-host:sha-f97d7f6`, source commit `f97d7f614fd57115fd94916f13fa6c3e3ae7ef10`, .NET 10, Docker Desktop on macOS with 14 CPUs and 8 GB, and a sibling PostgreSQL container whose major version is not recorded. The image is absent from the current local image inventory. The historical findings describe a development shell without the checkpoint persistence feature, so its reported baseline was Immediate; adding the feature with Mode Coalesced produced the comparison run. The historical session did not read effective cadence back from the running instance.

The report records 533 versus 237 per-call `Microsoft.EntityFrameworkCore.Database.Command` events and 24 versus four checkpoint marker inserts under Immediate and Coalesced. Its section calls the counts “SQL statements,” but the retained source response correctly qualifies an EF `Executed DbCommand` log entry as a command execution: a command can contain multiple SQL statements and EF may batch statements. No raw structured logs, exact SQL statement census, immutable image digest, original export, or transform code were found locally. Treat the summarized counts and latency values as historical observations from the supplied report, not current evidence or exact reproduction targets. The unexplained valid-input 202 and untraced post-response activity remain separate questions.

The historical request/settings packet says:

- Valid request: `{"firstName":"Alice","lastName":"Smith"}`; expected HTTP 200 with a transformed document, whose exact bytes are not recorded.
- Fault control: omit `firstName`; this reportedly faulted and returned HTTP 202 with a `started` payload. Keep it outside the successful request sample.
- Diagnostic logging: JSON console formatter, scopes enabled, UTC timestamps, ASP.NET hosting diagnostics at Information, and a unique W3C `traceparent` for each request. Count event 20101 command log events by TraceId.
- Timing logging: `Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning`; do not mix its latencies with traced-run latencies.
- Historical sampling: 25 warm-ups, then 60 requests per comparable sequential configuration.
- Historical Coalesced setting: feature `WorkflowsRuntimeCheckpointPersistence`, Mode `Coalesced`; the old packet does not state an explicit cap, so the source default of 50 cannot be assumed to be the effective historical run setting.

The current source Workbench shell explicitly sets Coalesced/50 in [shells.json](../../../src/apps/Elsa.Workbench/shells.json#L187). That is current host configuration evidence, not a readback from the historical image or proof of the #2392 run's effective per-instance cadence. The instance detail route is [GET /runtime/workflows/instances/{workflowExecutionId}](../../../src/essentials/Workflows/Runtime/Api/Endpoints/Instances/Get/Endpoint.cs#L12); the resolver prefers its per-run stamp before falling back to authored and host cadence in [RuntimeCheckpointCadenceResolver.cs](../../../src/essentials/Workflows/Runtime/Services/Checkpoints/RuntimeCheckpointCadenceResolver.cs#L31). The executable export route is defined in [RouteConstants.cs](../../../src/essentials/Workflows/Publishing/Api/Constants/RouteConstants.cs#L32), and the CLR profile fallback is in [ExecutableNodeCompiler.cs](../../../src/essentials/Workflows/Publishing/Services/ExecutableNodeCompiler.cs#L357).

## Pinned contract and effective cadence evidence

Read the exact published executable artifact used by each run and retain its artifact ID/hash. The publishing API exports the executable closure from `GET /publishing/workflows/{versionId}/executable-export` (permission: `workflow-publishing.read`). Inspect the exported checkpoint cadence. For `elsa.intrinsic.set@1`, record the exported `IntrinsicKind=Set`; intrinsic nodes have no `ActivityContract`. At source SHA `7b8e5d1304c198ce80dcc3d17aed5598245c7352`, verify the source classification `WorkflowIntrinsicFusion.IsFusable(WorkflowIntrinsicKind.Set)`. For CLR activity nodes only, record each pinned `ActivityContract.SideEffectProfile`; the current `ExecutableNodeCompiler.ResolveSideEffectProfile` assigns External when a CLR activity type has no profile attribute.

For each current Immediate and Coalesced run, read `GET /runtime/workflows/instances/{workflowExecutionId}` (permission: `workflow-runtime.read`) and retain `checkpointCadence`, `maxSegmentCheckpoints`, and `inspectionGranularity`, along with workflow execution ID and pinned artifact ID. The endpoint's detail projection prefers the per-run cadence stamp and falls back to host configuration for legacy instances. This is the required effective readback; a shell file alone does not satisfy it. At the candidate source SHA, `Set` is classified as fusable; the published artifact's `IntrinsicKind=Set`, CLR activity profiles, and effective cadence remain to be captured.

The reported historical Immediate value remains a qualified source/report claim because the original running image and instance are unavailable for a live readback. Keep its identity separate from the current candidate: historical `f97d7f614fd57115fd94916f13fa6c3e3ae7ef10` versus current candidate `7b8e5d1304c198ce80dcc3d17aed5598245c7352`. The runtime source in this planning commit is inherited from parent `bc94b1a3694e02acefcfd4d0229cd5624a8b3a18`.

## Bounded controls

Use the selected four-node HTTP fixture as the base and keep the following controls finite:

1. **Sequential HTTP baseline:** one request at a time, same fixture, body, host/provider and exact artifact. Run an Immediate host control and the current Coalesced/50 host control. Each timing configuration gets 25 warm-ups followed by 60 measured requests.
2. **Sixteen-node straight-line scale:** exactly 16 authored nodes: root Sequence, synchronous HttpEndpoint, 13 sequential instances of the same deterministic `SetVariable` compiler intrinsic, and WriteHttpResponse. Each intrinsic writes the same workflow-scope `referenceText` from the unchanged `content` using the selected fixture expression. Reusing one target keeps variable cardinality constant while activity transitions increase. Use the same request and expected `Alice Smith` response. This is a selected reference scale fixture, not the historical workflow. For each intrinsic, record exported `IntrinsicKind=Set` and the source SHA's `WorkflowIntrinsicFusion.IsFusable(Set)` result; do not use `ActivityContract.SideEffectProfile` to classify an intrinsic. Record pinned `ActivityContract.SideEffectProfile` for CLR activity nodes in the fixture separately.
3. **Concurrency four:** use the four-node HTTP fixture and exactly 60 total measured requests across four concurrent clients per selected configuration, after 25 warm-ups. Do not interpret four clients as 60 requests each.
4. **Ordinary REST-start control:** first try `POST /runtime/workflows/executables/{artifactId}/execute` with workflow input `content`. Verify that the HTTP-triggered definition reaches the same computation, exposes `Alice Smith`, and completes terminally. If it remains waiting on the HttpEndpoint bookmark, it is not a matched control. In that case publish a companion definition that starts through the execute API, accepts the same `content`, runs the same SetVariable expression, exposes the computed output through the workflow detail read model, and reaches terminal state. Explicitly list the omitted HTTP trigger and response-transport work and any extra input/output intrinsic. Compare command categories and completion correctness; do not subtract its latency from the HTTP latency as if the paths were equivalent. If a terminal companion cannot be constructed without changing the computation or state work materially, defer that control with a named owner and revisit trigger instead of substituting a waiting run. Give a selected REST configuration 25 warm-ups and 60 total measured requests.

These are bounded controls, not a parameter sweep. No further payload, workflow-length, provider, host, or concurrency matrix is authorized by this work unit. The 30 ms figure remains an aspiration, not an acceptance budget.

## Capture procedure

The #2392 lane owns the planned single traced current-head pre-fix reference run. Its diagnostic run is single-request and separate from timing. For any later diagnostic capture, use JSON console logs with scopes and UTC timestamps, ASP.NET hosting diagnostics at Information, a unique W3C `traceparent`, and EF command logging enabled. Retain the response, TraceId, workflow execution ID, pinned artifact ID, and effective cadence readback. Count EF event 20101 as command executions. Do not report those counts as SQL statements unless raw SQL is separately parsed.

For timing runs, use `Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning` and the same built candidate, disposable PostgreSQL version, fixture and API route for comparable cases. Record exact settings, HTTP status/body correctness, 25 warm-ups, 60 timed requests, median, p95, sample variation, host load, and whether the run was cold or warm. Keep request-path response latency separate from total database work after the response. Do not infer causality for untraced follow-up commands from time windows alone.

Before running, record the source SHA, `dotnet --version`, Docker server version/architecture/CPU/memory, database image and major version, host composition, database freshness, and fixture/export identity. Current read-only preflight on this machine: macOS 26.6; .NET SDK 10.0.300; Docker 29.4.0, Linux/aarch64, 8 CPUs and 12,600,471,552 bytes assigned memory. Host reports 8 logical CPUs and 25,769,837,568 bytes physical memory. The historical packet instead reports Docker Desktop on macOS with 14 CPUs and 8 GB; it does not record the OS release. Do not merge these environment identities.

Only these PostgreSQL images/containers appeared in the read-only inventory: running user-owned `elsa-demo-pg` (PostgreSQL 16 Alpine) and `elsa-demo-postgres` (PostgreSQL 17), plus local PostgreSQL 16/17 images. Neither running container is an approved test target. The exact old Elsa image is not present; `elsa-foundation-host:local-2151` (local image ID `c406e5abc97d`) is a different local image tag and is not evidence for the selected source SHA. The #2392 lane will own a separate disposable PostgreSQL 16 instance.

## Reproduction commands

Use the #2392 lane's host and database lifecycle commands; do not build another host or target an existing container. Once its host is ready, the exact primary request is:

```sh
curl --fail-with-body -i \
  -H 'Content-Type: application/json' \
  --data-binary '{"firstName":"Alice","lastName":"Smith"}' \
  "$BASE_URL/workflows/http/runtime-db-paging-2392/transform"
```

For one diagnostic request, add a unique header of the form `traceparent: 00-<32-hex-trace-id>-<16-hex-span-id>-01`. Then read effective settings for the returned workflow execution:

```sh
curl --fail-with-body \
  -H "Authorization: Bearer $TOKEN" \
  "$BASE_URL/runtime/workflows/instances/$WORKFLOW_EXECUTION_ID"
```

Inspect the pinned executable closure using the selected published version ID:

```sh
curl --fail-with-body \
  -H "Authorization: Bearer $TOKEN" \
  -o executable-closure.json \
  "$BASE_URL/publishing/workflows/$VERSION_ID/executable-export"
```

The REST-start API request shape is `POST /runtime/workflows/executables/{artifactId}/execute` with JSON such as `{"inputs":{"content":{"firstName":"Alice","lastName":"Smith"}}}`. Confirm its actual terminal result and `Alice Smith` output before treating it as a control. Authentication values stay in the test harness; do not print or attach tokens, connection strings, raw environment dumps, or user data.

## Owned residuals and revisit triggers

| Residual | Owner | Revisit trigger |
|---|---|---|
| Actual #2392 workflow execution ID, exported intrinsic kind and CLR activity profiles, source-level intrinsic fusability proof, run-level cadence readback and trace metadata | #2392 capture lane, consumed by T02/T05 | Before T02/T05 accept the current-head reference trace; not a prerequisite for this T01 plan |
| Whether direct REST execute of the HTTP definition passes the HttpEndpoint or waits on its bookmark; whether a terminal companion can preserve the same computation/output | T02 execution owner, using this T01 plan as input | First single-request REST-start preflight; if no valid companion exists, revisit when a supported request-input binding can feed the intrinsic and expose its output without changing its computation |
| Historical raw EF logs, exact SQL text/count, immutable image digest, original executable/export and transform code | Original findings author / user if later supplied; not a current gate | Only if a historical exact-count comparison is explicitly requested |
| Historical effective instance cadence and authored profile readback | Unavailable with old running instance/export | Only if the old image/artifacts are recovered |
| Post-response command identity and settled-work attribution | T04 (#2389), using execution/work-item/checkpoint/outbox IDs | T04 execution on its owned host |
| Current machine load at each timing capture | Timing-run owner | Immediately before each timing batch |

No production database or existing service was started, stopped, changed, or inspected for secrets in this spike. No runtime code was changed and no measurement was run by T01. The T01 plan deliverable is complete; T02/T05 must capture the exported intrinsic kind, verify source-level intrinsic fusability and pinned CLR activity profiles, and read back per-run cadence before accepting the current-head trace.
