# Quickstart: Execution Evidence Foundation validation

This is the implementation-validation guide for the Draft plan. It does not claim that the projects, endpoints, or feature IDs already exist.

## 1. Compose the host explicitly

After implementation, add direct server references and feature-catalog assemblies for the base, InMemory, and API assemblies. In the selected shell configure all three feature IDs:

```json
{
  "WorkflowsExecutionEvidence": {},
  "WorkflowsExecutionEvidenceInMemory": {},
  "WorkflowsExecutionEvidenceApi": {}
}
```

`WorkflowsExecutionEvidenceApi` references Core/base only; it does not reference InMemory. A separate **in-process** composition test composes none of these features and proves ordinary Runtime execution creates no Evidence service, intent, serialized payload, or allocation path. Do not call an absent-server e2e a proof unless an explicitly configured separate absent shell is actually launched.

## 2. Exercise session, ordering, and association paths

With an access token containing the documented scopes:

1. `POST /execution-evidence/sessions` with optional bounded metadata-only correlation.
2. `POST /execution-evidence/sessions/{sessionId}/workflows` to associate-and-start. The bridge reserves the association before Runtime admission and supplies opaque generic context before ordinary dispatch. Verify an admitted but uncommitted start is reported as `starting`, then becomes `active` only at the first committed checkpoint; authoritative start/checkpoint failure removes it.
3. Drive the ordinary workflow through the four v1 successful transitions. Query records and retain `nextCursor` exactly as returned. Verify each record’s stable `(workflowCheckpointOrder, checkpointOrdinal)` pair.
4. Start another workflow through ordinary Runtime APIs, let it commit unscoped work, then use `POST /execution-evidence/sessions/{sessionId}/workflows/{workflowExecutionId}/attach`. Verify the owner-scheduled attach response reports `effectiveFromWorkflowCheckpointOrder`; no earlier fact appears.
5. Test a forced checkpoint failure, persistence skip, uncertain client retry with the same operation key, two competing attaches from different sessions, attach during an active owner drain, and completion freezing a reservation after Runtime commits but before Evidence finalizes. They respectively produce no association, one durable winner, or a frozen winner that reconciles into the frozen set.
6. `POST /execution-evidence/sessions/{sessionId}/complete`. The response freezes association and pending-reservation admission immediately. While a frozen workflow has no terminal cutoff, a start remains unresolved, or an intent is pending, the state stays incomplete.
7. Use query/wait with a bounded `pageSize`; send correlation key/value together and a valid order range. Verify a cursor resumes exactly at its last examined item after a filtered nonmatch and after timeout. Reusing it with another page size, scope, filter, session, or after deletion must fail.
8. Once every frozen workflow has a terminal cutoff and all matching generic outbox intents through it are Delivered, wait may return `completed-range-without-match`; only then may `DELETE /execution-evidence/sessions/{sessionId}` succeed.

A timeout is inconclusive. A process restart is outside the InMemory provider’s completeness claim. `FailedFinal` and `Cancelled` are terminal integrity failures, not successful completion.

## 3. Run focused verification

```bash
dotnet test tests/Elsa/Workflows/ExecutionEvidence/Core/Tests/Elsa.Workflows.ExecutionEvidence.Core.Tests.csproj
dotnet test tests/Elsa/Workflows/ExecutionEvidence/Tests/Elsa.Workflows.ExecutionEvidence.Tests.csproj
dotnet test tests/Elsa/Workflows/ExecutionEvidence/InMemory/Tests/Elsa.Workflows.ExecutionEvidence.InMemory.Tests.csproj
dotnet test tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/Elsa.Workflows.ExecutionEvidence.Api.Tests.csproj
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet test benchmarks/Elsa/Workflows/ExecutionEvidence/Benchmarks/Elsa.Workflows.ExecutionEvidence.Benchmarks.csproj --filter "FullyQualifiedName~ExecutionEvidence" --logger "console;verbosity=detailed"
```

Runtime tests use the checked-in 28-caller/22-file inventory as a parameterized no-bypass matrix. They cover direct handlers, both checkpoint pipeline middlewares, incident/bookmark/alteration/activity-parent paths, and synthetic coalescing flush; durable `Prepared` reservations/high-watermarks before enrichment containing canonical raw checkpoint, pre-enrichment state changes, requested context mutation, and stable source/operation identity; crash recovery that verifies input, reattaches stored provenance/order, and reruns deterministic enrichers without source redrive; duplicate-after-fold receipts/compaction; non-contiguous skipped/failed internal orders; and immediate override for non-empty/mutating context or enriched post-commit work. They prove that reservation alone does not attach context or expose checkpoint/outbox/evidence, and separately cover the four baseline fact producers. Benchmark reservation storage size as well as allocation/throughput overhead. API tests cover authorization/tenant access, disjoint typed versus registered-unknown envelopes, correlation-pair/range validation, bounded scan/timeout cursor advancement, all lifecycle outcomes, and malformed/mismatched cursors.

## 4. Run the enabled-composition backend e2e

The e2e suite is a black-box check of the **enabled** server composition. It must run against a freshly deployed SQLite schema, not a TestServer or an in-process absence harness.

1. Stop the server launched for a prior run and verify port `5095` is free. Do not apply schema changes while that server is running.
2. Build the server, then remove the exact prior SQLite artifacts under `src/Apps/Elsa.Server/`: `elsa-groundwork.db`, its `-wal`/`-shm` sidecars, `elsa.sqlite.db`, its sidecars, and `*.schema.lock`. This is the required fresh-database cleanup after a rebuild; do not broaden deletion outside that directory.
3. Deploy the enabled reference-composition schema while the server remains stopped:

```bash
dotnet build src/Apps/Elsa.Server/Elsa.Server.csproj
dotnet tool run groundwork -- apply \
  --manifest-assembly src/Apps/Elsa.Server/bin/Debug/net10.0/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithDiagnosticsDeploymentSchema \
  --provider sqlite \
  --connection 'Data Source=src/Apps/Elsa.Server/elsa-groundwork.db' \
  --output json \
  --safe
```

4. In a dedicated terminal, launch the enabled server and leave it running:

```bash
dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj --launch-profile http
```

5. In a second terminal, wait for readiness before running the suite. A successful response from `http://localhost:5095/` is the readiness gate; do not substitute a fixed sleep. Then, on Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File e2e-tests/execution-evidence/Test-ExecutionEvidenceFoundation.ps1
```

6. The suite exercises enabled composition, authorize/open/associate/query/wait/complete/delete, start/attach failure reconciliation, late-attach effective order, terminal cutoffs, six-status delivery/integrity, and an ordinary workflow route. On completion or failure, stop the dedicated server process. Preserve the database only when it is needed to diagnose the failure; otherwise the next run begins with the fresh cleanup above.

Treat a failing e2e test as a signal: rebuild current main with a fresh database, rerun, and reconcile a real regression versus a stale test before changing either side.

## 5. Finish documentation, maps, and governance

Before implementation review:

1. Resolve the ADR number collision first: preserve JavaScript binding grammar as ADR 0062, rename the still-proposed Execution Evidence durability ADR to 0063, and update its path, links, Execution Evidence PRD reference, plan/research references, and generated maps.
2. Submit/review the E2.1 and Execution Evidence ADR 0052–0061 plus 0063 amendments; do not call draft/proposed boundaries ratified before review.
3. Add the domain README, `EXTENSION_POINTS.md`, root catalog entry, host-composition example, and a clear process-local/no-restart disclaimer.
4. Reinspect `docs/maps/manifest.json`. The planning baseline’s v1 map check is stale (`package-map.md`, `spec-status-map.md`, `maps-v1-findings.md`), so do not use it as current evidence.
5. After explicit map-refresh authorization, run the narrow relevant map generators, review generated findings, then run:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Record benchmark source revision, command, hardware/host configuration, workload shape, throughput, and allocations for module absent, enabled-unscoped, and enabled-scoped metadata-only modes. Do not invent a regression threshold in this slice.
