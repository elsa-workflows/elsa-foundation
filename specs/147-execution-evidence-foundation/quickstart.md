# Quickstart: Execution Evidence Foundation validation

This is the implementation-validation guide for the approved Path A recovery-authority and durable scheduler-continuation plan amendments. Both amendments passed independent review and control-room approval; their numbered RED artifacts and production implementations retain the separate review gates below. The approved T029 scope allocation keeps generic continuation in T029, and T029e is complete as a separate bounded Runtime live-carrier resolution. The observer-only T029b reopening passed independent review and control-room approval, T029c is authorized to resume, and T029d remains the unfiltered verification gate. This guide does not ratify the Draft constitution or proposed ADRs and does not claim that the projects, endpoints, or feature IDs already exist.

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
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/Elsa/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj
dotnet test tests/Elsa/Activities/Sequence/Tests/Elsa.Activities.Sequence.Tests.csproj
dotnet test tests/Elsa/Activities/ControlFlow/Tests/Elsa.Activities.ControlFlow.Tests.csproj
dotnet test tests/Elsa/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet build Elsa.Server.slnx
```

Runtime tests use the checked-in current 27-caller/21-file inventory as a parameterized no-bypass matrix, preserving T015's historical 28-caller/22-file `bd94b3c8d` audit. They cover direct handlers, both checkpoint pipeline middlewares, and incident/bookmark/alteration/activity-parent paths; a separate provider-atomic prepared-fold gate covers the former synthetic direct caller. The same suites cover durable `Prepared` reservations/high-watermarks before enrichment containing canonical raw checkpoint, pre-enrichment state changes, requested context mutation, stable source/operation identity, and optional generic recovery-source authority; duplicate-after-fold receipts/compaction; non-contiguous explicit skipped/failed internal orders; and the unchanged immediate override for non-empty/mutating context or enriched post-commit work. They prove that reservation alone does not attach context or expose checkpoint/outbox/evidence, separately cover the four baseline fact producers, and prove that only an exact durable scheduler continuation may keep an active session alive after its Immediate commit. Benchmark reservation storage size as well as allocation/throughput overhead. API tests cover authorization/tenant access, disjoint typed versus registered-unknown envelopes, correlation-pair/range validation, bounded scan/timeout cursor advancement, all lifecycle outcomes, and malformed/mismatched cursors.

Recovery validation has two distinct lanes and a shared authority/adoption contract:

1. **Authority capture/router (T024/T025):** dispatch an actual outer D1 durable item whose immutable fields cover the v1 authority material. Prove `WorkflowSchedulerDrainer` opens `runtime.scheduler-work` only around that acquired dispatch, a stack-restoring ambient nested call inherits it, and a checkpoint outside dispatch has null authority. This stage does not claim that a test-local nested call is the shipped D2→D1 path; that behavioral proof belongs to the separately blocked spec-123 reconciliation below. One-at-a-time bound tests cover 450-unit identifiers, 64-entry metadata maps, 128/4096 key/value limits, 256 KiB/depth-64 payload, 512 KiB total input, recursive ordinal object sorting, array preservation, minimal numbers, UTC ticks, and domain-separated SHA-256. Exercise exactly `Absent`, `Exact`, `Missing`, `FingerprintMismatch`, `UnsupportedVersionOrKind`, and `Ambiguous`; an actively claimed but still durable item must be `Exact`. Verify exact lookup and fallback pages are finite (maximum 250), keyset-stable, and cursor-bound. A declared source must never route as `Absent`.
2. **Source-bound D1/D2:** stage a real durable `ScheduleActivity`, create the additive exact three-member D1 Prepared prefix, and assert byte-for-byte preservation of all three `CommitId`s, `LedgerToken`s, authorities, provenance values, checkpoint orders, canonical digests, and fingerprints. Restart under a strictly newer workflow fence. One atomic adoption changes only each current authority fence/CAS revision, leaves original fences/revisions and every other field unchanged, and invokes no replay/fold. Normal redelivery must reuse the exact three members and converge through enqueue-by-identity, status no-op, and fold-forward claim. Missing/extra/duplicate/partial members, mixed authority/current fence, stale/downgrade/unauthorized target, and an injected mid-adoption provider failure must roll back every member and produce zero dispatch/replay/fold. The actual D2→D1 authority observation remains deliberately outside this generic continuation contract and belongs to the blocked spec-123 reconciliation.
3. **Source-independent:** prepare an exact contiguous prefix whose original authority is null. Prove the same adoption request works under a strictly newer current fence, exact same-current replay is idempotent, and only then does the shared replayer/exact fold run. Missing/extra/duplicate/partial members, mixed authority/current fence, digest/revision/token mismatch, a hidden source-bound gap, stale/downgrade/unauthorized target, cancellation, transient/enricher/provider failure, and injected mid-adoption failure leave every binding, state, context, outbox, marker, high-watermark, receipt, canonical input, token, and compaction byte-identical and invoke no replay/fold. No case may infer `Skipped` or `Failed`.
4. **Spec 123 continuity (T029a–T029d):** retain the original guardrails without replacing or weakening them. All five ON/OFF shapes, counters, and Groundwork kill ordinals remain owned by the canonical [spec 123](../123-replaysafe-hop-fusion/spec.md) reconciliation, including the D2→D1 recursion boundary. The current `ReplaySafeFusion` inventory is **10 total: 3 PASS / 7 RED**. PASS are the provider-fold harness, ReplaySafe contract probe, and deterministic fingerprint. RED are the authority outer/nested mismatch plus six guardrail byte-identity failures, including External. Reopened T029b passed independent/control-room review and T029c is authorized to resume; unfiltered verification follows only after implementation review. The additive three-member identity test is not a substitute for any original guardrail.

5. **Amended generic materialized-source lifecycle RED:** `RuntimeCheckpointCoalescingTests` owns an 8-case Runtime filter: 7 protective green cases and 1 intentional new-`Committed` exact-source RED requiring the live session to remain active. It travels with the current Activity inventory above: `ReplaySafeFusion` is 3 PASS / 7 RED, with the provider-fold harness now green. T029b re-review passed and T029c is authorized to resume only in `src/Elsa/Workflows/Runtime/Services/Coalescing/{CoalescingRuntimeCheckpointCommitStore.cs,RuntimeCoalescingSession.cs}`, while fusion driver, committer, policy, providers, Evidence, and public contracts remain excluded.

   **Reopened observer review:** Internal provider folds run through `CoalescingInner<IRuntimeCheckpointCommitStore>`, so the former outer-store observers reported false zeroes. The test-only amendment observes that captured durable provider while preserving the outer boundary-crash wrapper. The full filter is 10 total: 3 PASS (provider-fold harness, ReplaySafe contract probe, deterministic fingerprint) and 7 RED (authority outer/nested mismatch plus six guardrail byte-identity failures, including External). Independent review passed and the control room approved T029c to resume within its unchanged two-file boundary; this does not complete production work.

6. **Durable scheduler-continuation boundary (T028/T029):** first add/review the Runtime RED coverage in `tests/Elsa/Workflows/Runtime/Tests/{RuntimeCheckpointCoalescingPolicyTests.cs,RuntimeCheckpointCoalescingTests.cs}`, then implement only the generic contract. Prove the generic after-enrichment rule still forces Immediate for any post-commit work, context snapshot, or context mutation. For a new successful `Committed` context-empty/unmutated commit containing only nonempty `EnqueueSchedulerWork` rows, assert that the exact committed Pending rows/IDs are imported and marked durably persisted and the committed boundary state enters the overlay. The exact materialization/consume transition requires T029 implementation-and-review evidence; eligibility remains generic and no durable recovery input may depend on memory-only scheduler work. `Replay` uses ordinary deactivation/advancement. Assert the existing outbox processor/decorator/scheduler overlay/queue creates no duplicate intent or work item and invokes no handler directly. While that overlay remains live, inline delivery keeps the original row Pending until a later checkpoint/fold incorporates the effect and reconciliation succeeds. Crash once before inline dispatch and once after inline dispatch: both begin with that original Pending row; after session loss ordinary durable redrive idempotently enqueues the exact durable scheduler item and may mark Delivered immediately, while a crash between enqueue and the mark repeats without a duplicate. Mixed/arbitrary/external outbox, context-only or context-mutating commits, delivery failure, and terminal/no-continuation state must deactivate the session and use ordinary durable processing. T029 owns `src/Elsa/Workflows/Runtime/Services/Coalescing/{CoalescingRuntimeCheckpointCommitStore.cs,RuntimeCoalescingSession.cs}` and reuses existing generic outbox/overlay/queue seams. Independent final review and control-room approval passed on 2026-08-06: root slice 38/38, Runtime 1830/1831 with only the clean-baseline T029e RED, no T027 regression, and Groundwork 844/853 with nine known pre-T029 REDs. T029e then separately resolved `RuntimeInProcessHopFastPathTests.Committer_PublishesContinuation_ToOwningLiveDrainCarrier`: a raw candidate is captured only for the owning live drain and published only after a new `Committed` prepared finalization has one exact final durable intent with matching identity/association plus structural equality of the candidate intent payload and independently serialized candidate work-item payload. The durable payload remains authoritative; replay, skip, failure, exception, mismatch, and coalescing ownership do not publish. Source-owner/independent RED reviews passed, control-room approved, corrected implementation review passed with no open finding, and verification was 19/19 focused tests, 50/50 preparation/committer subset, and 1838/1838 full Runtime. T029a is released. This resolution must not weaken the generic Immediate override, invoke a handler directly, duplicate an intent/work item, acknowledge memory-only delivery, or add D1/D2/Evidence/fusion/provider branches.

The eight unfiltered test projects and `Elsa.Server.slnx` build above are the exact spec 123 SC-008 gate. T029e is complete; they remain blocked until T029a–T029d complete, then must record pass counts and canonical shape/counter/kill-point evidence. The known full Groundwork baseline remains 844/853 with nine pre-T029 REDs until T029d runs the unfiltered/kill gate; do not claim it green. A filtered Runtime/Groundwork run alone cannot close the gate.

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
