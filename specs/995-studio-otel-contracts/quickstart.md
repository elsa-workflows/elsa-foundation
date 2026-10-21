# Validation Guide: Studio OpenTelemetry Trace Inspection

This guide defines the later acceptance checks. **No tests, builds, or hosts were run in this planning checkpoint.** Root owns shared-machine scheduling, the isolated host run, integration, and final qualification.

## Focused behavior regression

Use the existing Studio pnpm workspace and the module's existing Vitest suite. First run the untouched baseline, then add the new assertions before changing `module.tsx` and run the same command to record a meaningful RED result. Only the new contract assertions should explain the failure.

```bash
cd Studio
pnpm install --offline --frozen-lockfile
pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry test
```

The shared backend-shaped fixtures should cover:

1. The real trace-search `{items, droppedCount}` envelope, nested detail, actual `Unset`/`Ok`/`Error` and resource-status enums, TimeSpan trace duration, span start/end duration, and supported filter names and `take` bound.
2. A trace with multiple resources/services, each span joined through its own `resourceId`, and an unresolved resource that remains explicitly unknown. Keep fixtures shared and teardown idiomatic.
3. Independent storage and resource-search success/failure; loading and successful-empty; 401/403 denial; list/storage/resource 404 unavailable; a different selected-detail 404 message for a trace that disappeared; and generic failures. Verify failed counts are not rendered as zero/healthy.
4. No summary `errorCount`, no per-row detail request, no invented trace total, and no unsupported `operation`/`service` query keys. A result bound and `droppedCount` must not be described as a total of all traces.
5. Metrics and OpenTelemetry Logs remain clearly unsupported and the Diagnostics handoff remains usable. A nested detail fixture with a sentinel log must keep that value out of both page content and the existing trace/span export; supported trace/span export remains available.

Assert requests and user-visible behavior at the module boundary using actual response fixtures; do not duplicate backend mapping logic in test helpers or assert on source text. Add a rendered 403 case separately from the existing backend authorization proof below.

## Focused checks after the correction

Run the module suite first and keep the expensive work serial. Root schedules these checks with the other active integration lane.

```bash
cd Studio
pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry test
pnpm -r --workspace-concurrency=1 typecheck
pnpm lint
pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry build
pnpm -r --workspace-concurrency=1 build
```

The module has its own `test` and `build` scripts; workspace `typecheck`, `lint`, and `build` are defined in `Studio/package.json`. The explicit recursive commands above preserve the workspace scripts' coverage while serializing their package work on the shared machine. Run the existing OpenTelemetry TestServer authorization cases separately from the rendered UI 403 test:

```bash
dotnet test Essentials/tests/Diagnostics/OpenTelemetry/Tests/Elsa.Diagnostics.OpenTelemetry.Tests.csproj \
  --filter 'FullyQualifiedName~OpenTelemetryAuthorizationTests|FullyQualifiedName~OpenTelemetryAuthorizationBoundaryTests'
```

These existing tests exercise authenticated principals against the actual TestServer query/storage/resource routes. They prove the backend permission boundary; they are not evidence of a denied browser journey in the normal host. Do not add optional Identity/admin composition just to create a denial principal. Preserve the existing focused suite's malformed-request and successful-read coverage.

## Normal-host trace proof

For the private host configuration, fresh-root, launch, standard Development sign-in, readiness, and guarded cleanup procedure, use the stable [Spec 993 composed Workbench guide](../993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison). Use a new private root and follow its normal profile only: the checked-in normal Workbench and Studio module compositions, fresh per-run Elsa/diagnostics databases and Data Protection key database, distinct per-run application name, empty private package directories, and isolated browser context. Do not use user data, copied personal browser state, or the anonymous baseline as proof of authenticated access. Never occupy or stop an unowned listener. Retain the canonical `elsa-3073.` root prefix and manifest purpose expected by the guide's preparation and cleanup guards; record the actual #3131 source/build and process ownership in a separate safe QA receipt.

The source-backed proof path is:

1. Confirm the normal Workbench shell includes `DiagnosticsOpenTelemetryEngineBridge` (`Essentials/apps/Elsa.Workbench/shells.json` and `Essentials/apps/Elsa.Workbench/OpenTelemetryEngineTracingBridge.cs`). Do not enable an optional collector/provider or inject a trace manually.
2. Launch the normal Workbench and Studio hosts from the private content roots as directed by the guide. In a fresh browser context, sign in through the standard Development UI using the disposable seeded Development principal in the copied normal shell. Do not record its username or credential value.
3. Reuse the source-backed `WriteLine` author/publish/execute path in `e2e-tests/Test-WorkflowFlow.ps1` and helpers in `e2e-tests/_ElsaCommon.ps1`: resolve the current activity head version, submit one harmless root activity, publish the returned version, execute with its returned source-reference ID, and wait for the returned workflow execution ID to reach a terminal state. Use only disposable non-sensitive literal input.
4. Correlate that execution through the existing trace search with `workflowInstanceId` and a bounded `take`; obtain the generated trace ID, then enter that ID into Studio's trace ID filter. Verify the real trace appears, open its nested detail, and inspect the bridge-produced `elsa.runtime.*` span and `elsa.workflow.execution_id` tag that matches the disposable execution. Verify service/resource joins, elapsed duration, status, and parent-child data in the page.
5. If persistence is asynchronous, use bounded retry for the supported query. A successful empty result stays empty; do not substitute a test fixture or manually inject data if the real trace is not yet available.

The source pointers above are grounded in the normal Workbench shell and bridge, `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Diagnostics/ActivitySourceWorkflowEngineTracer.cs`, the existing workflow-flow fixture/helpers, and the current OpenTelemetry API contracts. This proves the existing engine bridge path; it does not claim external OTLP ingestion.

## Safe evidence and cleanup

Record only the method, path, status, content type, response field/body shape, and synthetic disposable-run identifiers needed to correlate the proof. Do not save request/response headers, cookies, credentials, bearer material, arbitrary resource attribute values, or value-bearing failure payloads. Keep any browser evidence confined to the disposable trace and harmless literal workflow input; scrub it before sharing if it contains other values. Close the isolated browser, stop only the host processes started for the run, verify their owned ports are free, and delete only the exact private run root after validating its manifest/ownership guard.

## Root-owned final qualification

After focused checks and the normal-host happy trace proof, root serially coordinates the source module/workspace type, lint, and build checks; both Workbench and Studio .NET host builds in addition to client assets; the architecture restore and suite plus maps check; independent exact-head review/CI; and the resulting-main gate. The architecture commands from the [Spec 993 guide](../993-studio-diagnostics-index/quickstart.md#architecture-prerequisite) are:

```bash
bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Use the normal repository build-slot wrapper for every dotnet command. Coordinate all heavy local verification with root and do not run a second heavy local check concurrently. There is no product or test dependency on #3132; root owns the single serial integration lane and base reconciliation.

## Root normal-host checkpoint

The normal engine-bridge proof ran with host binaries from2381e6459 and final rebuilt OpenTelemetry CSS/Trace-heading assets, whose source/bundle hashes and served-byte comparison are retained in the external execution evidence. The harmless workflow completed; actual trace search/detail returned200 with14engine spans, all14resource joins and12parent-child links. Standard Development UI sign-in, More tools → Diagnostics → OpenTelemetry and the exact generated trace filter led to the real activity span and matching execution tag. Stored-data UI showed one resource and15spans, with no invented trace-total card.

Before/after laptop evidence at1440light and1280dark verifies the corrected long-tag wrapping and visible list Status. Final module26/26, build and CSS lint pass. Failed locator attempts, the first browser tool timeout and initial build-process failure are retained as failures, with successful bounded retries recorded separately. Owned browsers/hosts exited, both ports are free and guarded private-root cleanup completed. This is scoped happy-path/data/layout evidence; backend authorization21/21 remains separate from a denied browser journey, and final integrated gates/review/CI/main remain pending.
