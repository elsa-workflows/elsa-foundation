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

With an access token containing the documented scopes, generate one caller-supplied `Idempotency-Key` per intended
associate-and-start or late-attach command. Reuse that exact key only to retry the same canonical request; use a new
key for another command. Both endpoints require the header:

```bash
start_operation_key="evidence-start-$(uuidgen)"
curl --fail-with-body -X POST "$ELSA_URL/execution-evidence/sessions/$SESSION_ID/workflows" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Idempotency-Key: $start_operation_key" \
  -H "Content-Type: application/json" \
  -d '{"workflowArtifactId":"workflow-artifact-id"}'

attach_operation_key="evidence-attach-$(uuidgen)"
curl --fail-with-body -X POST "$ELSA_URL/execution-evidence/sessions/$SESSION_ID/workflows/$WORKFLOW_EXECUTION_ID/attach" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Idempotency-Key: $attach_operation_key" \
  -H "Content-Type: application/json" \
  -d '{}'
```

1. `POST /execution-evidence/sessions` with optional bounded metadata-only correlation.
2. `POST /execution-evidence/sessions/{sessionId}/workflows` to associate-and-start, sending the required `Idempotency-Key`. The bridge reserves the association before Runtime admission and supplies opaque generic context before ordinary dispatch. Verify an admitted but uncommitted start is reported as `starting`, then becomes `active` only at the first committed checkpoint; authoritative start/checkpoint failure removes it.
3. Drive the ordinary workflow through the four v1 successful transitions. Query records and retain `nextCursor` exactly as returned. Verify each record’s stable `(workflowCheckpointOrder, checkpointOrdinal)` pair.
4. Start another workflow through ordinary Runtime APIs, let it commit unscoped work, then use `POST /execution-evidence/sessions/{sessionId}/workflows/{workflowExecutionId}/attach` with its own required `Idempotency-Key`. Verify the owner-scheduled attach response reports `effectiveFromWorkflowCheckpointOrder`; no earlier fact appears.
5. For both association endpoints, prove one-character and 256-character nonblank keys are accepted, while missing, blank, and 257-character keys return stable `400 invalid-request`. Simulate a lost acknowledgement after a committed response, then resend the exact canonical request with the same key and the same normalized caller access context, session, target, and request material: it returns the Runtime-authoritative prior result and creates no second mutation. Reuse that key with any of those normalized materials changed: it returns `409 idempotency-conflict` and creates no mutation.
6. Test a forced checkpoint failure, persistence skip, two competing attaches from different sessions, attach during an active owner drain, and completion freezing a reservation after Runtime commits but before Evidence finalizes. They respectively produce no association, one durable winner, or a frozen winner that reconciles into the frozen set.
7. `POST /execution-evidence/sessions/{sessionId}/complete`. The response freezes association and pending-reservation admission immediately. While a frozen workflow has no terminal cutoff, a start remains unresolved, or an intent is pending, the state stays incomplete.
8. Use query/wait with a bounded `pageSize`; send correlation key/value together and a valid order range. Verify a cursor resumes exactly at its last examined item after a filtered nonmatch and after timeout. Reusing it with another page size, scope, filter, session, or after deletion must fail.
9. Once every frozen workflow has a terminal cutoff and all matching generic outbox intents through it are Delivered, wait may return `completed-range-without-match`; only then may `DELETE /execution-evidence/sessions/{sessionId}` succeed.

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

### T001–T003 ADR collision evidence (2026-08-05)

| Inventory check | Before T001 | After T003 |
|---|---|---|
| ADR 0062 files | JavaScript binding grammar and the colliding proposed Evidence durability ADR both used 0062. | `0062-javascript-binding-grammar-is-pinned-at-publish.md` is the sole ADR 0062. |
| Evidence durability record | `0062-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md` | `0063-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md` |
| Non-generated Evidence durability ADR backlink | The Execution Evidence PRD linked the durability decision as ADR 0062. | The PRD and Evidence ADR backlink inventory link ADR 0063; plan/research already used the corrected future number. |
| Generated maps | No map change was authorized. | Deferred: generated maps remain untouched and must be refreshed only after explicit authorization and findings review. |

The repository-wide ADR-number/path search must retain JavaScript-only ADR 0062 references and the
historical source-path text in T001 and this table's before column; neither is an active Evidence
durability backlink.

### T007 #1133 exclusion dispositions (2026-08-05)

| ADR | Disposition | #1133 exclusion result | Reason |
|---|---|---|---|
| 0056 value capture | unchanged-and-why | PASS | This is the end-state #1136 value/sanitization/disposition decision. #1133 remains metadata-only and creates no value behavior. |
| 0058 negative claims | unchanged-and-why | PASS | Settled barriers, gap-free completeness, and definitive-negative semantics remain #1134 work; #1133 reports only the listed inconclusive/completion observations. |
| 0059 retention | unchanged-and-why | PASS | Whole-session retention cleanup and provider-conformance behavior remain #1137 work; #1133 adds neither TTL nor cleanup. |

These dispositions and the E2.1/ADR amendments enter the T008 Governance / Architecture-Review
Disposition Gate below as Draft/proposed material. Implementation-readiness review does not
constitute ratification.

### T008 Governance / Architecture-Review Disposition Gate (2026-08-05)

Review scope: the Execution Evidence glossary alignment, proposed E2.1 module row, and ADR 0052–0061
plus 0063 governance set.

| Review evidence | Disposition | Unresolved concerns | Ratification evidence accepted |
|---|---|---|---|
| Control-room Critical Constitution Review and Source-of-Truth Audit | PASS. The contracts-only Core, provider-neutral base, explicit InMemory leaf, API transport/inheritance boundary, Runtime isolation, and #1134–#1138 ownership are enforceable and live in their correct source-of-truth layers. | None. | None. |
| Independent architecture/dependency review | Initial BLOCKER: ADR 0063's consequence claimed the existing enricher/outbox seams were broadly sufficient and omitted #1133's required generic Runtime correctness work. The ADR was corrected to name the evidence-agnostic Prepare/Commit protocol, durable `Prepared` ledger/high-watermarks, replay-stable provenance/order, atomic state/context/outbox/marker CAS, and outbox checkpoint-order/status support across InMemory, coalescing, and Groundwork checkpoint stores, while retaining #1137's Evidence-provider boundary. Final disposition: PASS. | None after correction. | None. |

The T008 implementation-readiness gate passes with no unresolved concern. It is not a ratification
gate: the constitution remains Draft and the Execution Evidence ADRs remain proposed.
