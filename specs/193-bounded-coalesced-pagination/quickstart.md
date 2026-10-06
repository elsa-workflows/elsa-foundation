# Quickstart: Bounded Coalesced Runtime-Store Page Merging

This guide defines the deterministic regression proof for #2393 and the normal-host reference capture owned by #2392. The behavioral details are in [the merge contract](contracts/coalesced-page-merge.md); the exact fixture and current execution state are recorded in [reference-trace.md](reference-trace.md).

## Deterministic paging proof

The focused project is:

```text
tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
```

The pre-fix and post-fix command set is intentionally narrow:

```bash
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj \
  --filter 'FullyQualifiedName~Coalesced_activity_pages_merge_overlay_without_traversing_the_inner_collection|FullyQualifiedName~Coalesced_page_merger' \
  --no-restore --verbosity minimal
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj \
  --no-restore --verbosity minimal
```

Before #2393, the newly added bounded-call test must fail only on its expected repeated inner-read assertion; capture that result before production edits. After #2393, the same targeted test and the whole focused project must pass. The whole-project command is necessary because the merger is shared by several coalesced runtime stores. A mutation that restores one-row refetch behavior must make the call-count assertion fail; restore the intended source and rerun the focused project green. Do not infer behavioral improvement from a test that passes on both source versions.

Required vectors:

1. 128 sorted overlay upserts with an empty durable store, output limit 7: one terminal empty-source probe at most for that merge call.
2. Overlay identities before one durable candidate with a page limit that emits the candidate: exactly one inner page read for that candidate in the merge call, including has-next handling.
3. A page ending before a fetched visible candidate: the first call returns a token positioned before the bounded batch; the next call can replay that batch and emits the candidate once.
4. Interleaved durable rows, an equal-identity overlay replacement, a tombstone, and overlay-only rows traversed to completion with limit 2: exact expected ordinal sequence with no duplicates or omissions.
5. Existing valid opaque token, malformed/wrong-binding token, request cancellation, empty-terminal exhaustion, and preservation of the `RuntimeStorePage<T>` constructor rejection for an empty page with a continuation.

Count provider reads by cursor position. Each position may be read at most once in one merge call. A request may need multiple filtered pages to prove that no later visible row exists: the durable `a`–`l` vector with tombstones `c`–`l` uses six distinct positions at page size 2. For the limit-2 interleaved traversal, allow up to two positions on the first call and up to two on the second (replay of the initial position and the following position). A later-call replay is allowed by the payload-free continuation design.

## Current-head HTTP reference capture (#2392)

This is a representative reference, not a reproduction of the historical custom CLR transform or its reported command counts. The first built/captured source is `5d28bd0cd3d76004988a01b0f0abd7af3af313d6`; runtime source matches merged main `43b3ef51882105916380ac8e539b49ad7b8a37b6`. Root approved this four-node definition:

1. Root `Sequence`.
2. `HttpEndpoint`, POST, `CanStartWorkflow=true`, `ResponseMode=Sync`, route `/workflows/http/runtime-db-paging-2392/transform`; capture `ParsedContent` as workflow variable `content`.
3. `SetVariable` compiler intrinsic `elsa.intrinsic.set@1`, kind `Set`, assigning `referenceText` to the JavaScript expression `getVariable('content').firstName + ' ' + getVariable('content').lastName`.
4. `WriteHttpResponse` with status 200, `text/plain`, and body `referenceText`.

Request body is `{"firstName":"Alice","lastName":"Smith"}`. The expected response is HTTP 200 with body `Alice Smith`. The fixture uses a compiler intrinsic, not a historical CLR transform. Its executable export should show `intrinsicKind: "Set"`; `activityContract` may be omitted or null because `JsonPayloadSerializer.BuildOptions` uses `JsonIgnoreCondition.WhenWritingNull`. This is a serialization detail: the compiler's in-memory `ActivityContract` value is separately null. `WorkflowIntrinsicFusion.IsFusable(Set)` currently returns true. There is no activity `SideEffectProfile` to report for this intrinsic. CLR activities remain subject to their pinned side-effect profiles. Record the intrinsic classification because fusion changes activity-hop and cadence accounting.

Use only an isolated current-head Workbench host and a uniquely named, disposable PostgreSQL 16 container and database. Never point this fixture at `elsa-demo-pg`, `elsa-demo-postgres`, or any other existing database. Keep credentials in a protected temporary service/environment file; do not place credentials or connection strings in arguments, tracked files, or trace output. Do not enable sensitive-data logging. Launch and stop only the Workbench process owned for this capture, on a verified free loopback port. Reuse `e2e-tests/_ElsaCommon.ps1` for login, workflow submission/publication, and detail reads; this fixture does not create a host or database lifecycle harness.

The approved trace procedure is:

1. Build only `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` at the exact candidate after the program lead confirms shared load is acceptable. Use the repository build-slot wrapper; do not bypass it.
2. Configure an owned runtime resource as `Provider=PostgreSql`, named connection `ElsaRuntime2392`, and the temporary default shell's `Configuration.Elsa.Persistence.DefaultResource` to that resource. Supply its protected `ConnectionStrings__ElsaRuntime2392`. Retain legacy `Elsa` as SQLite in the temporary content root because an unenrolled SQLite consumer can still use that name. Bind `DiagnosticsOpenTelemetryEntityFrameworkCore` and `DiagnosticsStructuredLogsEntityFrameworkCore` explicitly to an owned SQLite diagnostics resource (`ElsaDiagnostics`); remove inline diagnostics connection overrides from the temporary shell copy. Record this provider split. Preserve Workbench Coalesced settings (`MaxSegmentCheckpoints=50`) and capture the effective instance readback rather than relying on configuration text. Require a successful `SELECT 1` against the selected PostgreSQL database before launching Workbench; container readiness alone is insufficient.
3. Enable `Microsoft.EntityFrameworkCore.Database.Command=Information` and transaction Debug for this owned process only. Use JSON scopes, UTC timestamps with an explicit format, and ASP.NET hosting diagnostics. Do not enable parameter/sensitive logging. Supply a unique W3C `traceparent` on a separate authenticated trigger session; authoring and readback use their own request traces. Retain engine span parentage before owned-root cleanup. Persisted diagnostic SQL must be reported separately from runtime PostgreSQL access.
4. Run `pwsh -NoProfile -File ./e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1 -HostCandidateSha 5d28bd0cd3d76004988a01b0f0abd7af3af313d6 -BaseUrl http://127.0.0.1:<owned-port>`. For an after capture, supply that host's exact source SHA. The script reports its own checkout separately as `scriptHead`; `hostCandidateInput` is operator-supplied and must match the independently retained build record before the trace is accepted. If only capture scripts/docs changed after the build, verify runtime/build input equivalence and record both SHAs. This manually invoked capture stays outside the global `Test-*.ps1` HTTP suite. It reuses the normal host APIs, triggers the HTTP endpoint synchronously, filters instances with the public `definitionId` query against the unique published source definition, rejects ambiguous matches, and reads the detail endpoint.
5. Grant only the literal read permissions needed for evidence: `workflow-publishing.read` for `GET /publishing/workflows/{versionId}/executable-export`, and `workflow-runtime.read` for `GET /runtime/workflows/instances/{id}`. Capture the exported `intrinsicKind: "Set"` and whether `activityContract` is omitted or null; for the run capture `checkpointCadence`, `maxSegmentCheckpoints`, and `inspectionGranularity` from instance detail.
6. Record response, terminal state, exact SHA, provider/container identity, non-secret settings, traceparent, host logs and the effective per-run values in `reference-trace.md`. Separate trigger/request SQL from work after the response. The shared host is currently heavily loaded, so report no timing interpretation. Use this same definition, payload, provider, and settings for the after comparison.

Current e2e prerequisites and safety rules are in [`e2e-tests/README.md`](../../e2e-tests/README.md). The first narrow build and isolated host capture passed correctness/effective-configuration assertions; causal accounting remains pending as detailed in [reference-trace.md](reference-trace.md). Heavy shared load still excludes timing interpretation. Captured evidence must not be inferred from this procedure or from a successful setup retry.

## Expected report shape

Keep [reference-trace.md](reference-trace.md) limited to the exact command and trace capture, observed output/state, effective configuration and pending items. Do not turn request timing into a CI threshold. The REST-start control is planned separately by #2385; an ordinary REST start of this HTTP-triggered workflow is not an equivalent control if it suspends at the HTTP bookmark.
