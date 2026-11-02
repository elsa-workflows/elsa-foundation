# Tasks: Continuous HTTP ETL runs

Spec: [spec.md](spec.md). Plan: [plan.md](plan.md). Tasks are ordered by dependency, and `[P]` marks tasks that can run in parallel.

## Phase 1 — Artifact policy (FR-001, FR-002)

- [ ] T001 Add `EtlContinuousRunPolicy` and the init-only `EtlPipelineV2.Continuous`, then update `EtlArtifactV2Json` so that a `null` policy is never serialized. Update `PublicAPI.Unshipped.txt`. (R1)
- [ ] T002 Add `EtlContinuousRunPolicyRules`, covering the three error codes, and call it from the compiler, `EtlConnectorExecutableReader.ValidateLimits` and `EtlConnectorRecordCodec.ValidateBindings`. (R7)
- [ ] T003 [P] Tests:
  - the policy round-trips;
  - a finite artifact's bytes, hash and invocation fingerprint equal a captured pre-change value;
  - an explicit `null` reads as finite;
  - each error code is produced at each of the three sites.

## Phase 2 — Persistence (FR-005, FR-007, FR-013)

- [ ] T004 Add the migration test from R11:
  1. migrate to `20261021170000`;
  2. insert raw prior-writer finite file and HTTP rows;
  3. migrate to latest;
  4. read the rows back unchanged, with the new column null;
  5. assert `HasPendingModelChanges()` is false.
- [ ] T005 Add the `CompletionRequestedAtUtcTicks` column, its CHECK constraint, the `20261102170000_ConnectorRunCompletion` migration and the model snapshot. (R5)
- [ ] T006 Change the idle invariant in `EfEtlConnectorHistory`. (R4)
- [ ] T007 Add `RecordConnectorHttpCaughtUpAsync` to the interface and the EF source store, enforcing the R11 attempt-liveness gate, and forward it in `ForwardingEtlConnectorRuntimeStore`. (R3, R11)
- [ ] T008 Add `RequestConnectorRunCompletionAsync`, which wakes only idle partitions. (R5)
- [ ] T009 Apply the interval in `AdvanceEmptyHttpPageAsync` after a moving empty page in a continuous run. Skip it once completion has been requested. (R6, R11)
- [ ] T010 [P] Store tests:
  - the reservation and metadata totals at each step (before, after `Begin`, after the caught-up record);
  - no new rows;
  - a stale fence, a stale attempt, an out-of-window receipt and a finite run are each refused;
  - a closed-uncertain, `Captured` or `Failed` journal is refused;
  - an idle wait is accepted in a continuous run and refused in a finite one;
  - `Final` relative to the request time;
  - each completion result;
  - only idle partitions are woken.

## Phase 3 — Runtime behaviour (FR-003, FR-004, FR-006, FR-008, FR-009, FR-010)

- [ ] T011 Add `EtlHttpRequestBuilder.IsCaughtUp` and the continuous normalization in `EtlHttpPageCache.NormalizeContinuation`. The cache keeps the continuous flag and refuses construction without a builder. (R2, R11)
- [ ] T012 Classify responses in `EtlHttpSourceAcquirer`:
  - **Idle:** record the response, then return with no page.
  - **Final:** publish, capture and attach the normalized page.
  - In both cases, re-read the cache before recording.
  - **Continuous publish conflict:** capture the winning manifest. (R11)
- [ ] T013 [P] Acquirer and execution tests against an in-process cursor feed with two shards:
  - 20 or more idle polls leave state unchanged;
  - appended records are delivered once;
  - a moving empty page waits the interval;
  - drain settles `Succeeded`, and `BusinessError` when a record is rejected;
  - a caught-up response from an attempt that started earlier does not end the shard;
  - cancellation wins;
  - a finite empty page with an unchanged cursor still fails with `pagination-cycle`;
  - the publish-conflict race is recovered.
- [ ] T014 Add a native journey test: a Flowchart that runs a continuous run, completed through the store, resumes through `Done`; a cancelled run resumes `Cancelled`.

## Phase 4 — API and inspection (FR-007, FR-011)

- [ ] T015 Add the `CompleteRun` endpoint (`Endpoints/CompleteRun/Endpoint.cs`, with `[RequirePermission]`) and:
  - the permission;
  - the resource entry, also fixing the feature description;
  - the operations member, whose default throws 503;
  - 404 through `FindRunAsync`;
  - `EtlRunCompletionView`, registered in `EtlRuntimeApiJsonContext`.

  Update the Runtime/Api `EXTENSION_POINTS.md` and `PublicAPI.Unshipped.txt`. (R11)
- [ ] T016 Add the inspection fields `Mode`, `CompletionRequestedAt` and `NextPollAt` as init-only members, and the `ConnectorPartition` provider-contract check. Note the inspector change in the Core `EXTENSION_POINTS.md`. (R11)
- [ ] T017 [P] API tests: each result, authorization (403 without the permission) and the inspection fields for idle and retry partitions.

## Phase 5 — Studio (FR-012)

- [ ] T018 Studio changes:
  - types and `completeRun` in the API client;
  - the editor switch and the interval field;
  - the inspection labels and "Polls again at";
  - the "Complete run" action, resolved through the optional `etl-run-complete` relation;
  - "Eligible after" shown only when `nextPollAt` is null.
- [ ] T019 [P] Vitest tests for the editor's visibility rules and the policy shape, the inspection labels and the action. Playwright tests for continuous authoring at 320px and 1024px with axe.

## Phase 6 — Worker processes (FR-004, SC-003)

- [ ] T020 Worker-process journeys:
  - a kill after capture leads to a cache replay;
  - a kill during an idle poll's attempt leads to the poll being retried once the attempt expires;
  - two shards are leased by different workers.

## Phase 7 — Docs and evidence (FR-014, SC-004–SC-006)

- [ ] T021 [P] Docs:
  - the ETL README section "Continuous runs", covering the feed contract, completion, the idle-poll cost, the deadline and the lifetime budgets;
  - the Core README;
  - the Core and Runtime/Api `EXTENSION_POINTS.md`.
- [ ] T022 Run the feed server and the Workbench journey driver on an owned host, and record the summary JSON.
- [ ] T023 Real-browser inspection and completion against the owned hosts, recorded as screenshots.
- [ ] T024 Record the bite-proofs in `bite-proofs.json`: those listed in the plan, plus R11's:
  - the validator guard;
  - the fence, attempt, window and phase refusals;
  - each completion status guard;
  - the permission guard;
  - cancellation winning;
  - `NextPollAt` only when idle;
  - the Studio visibility rule.
- [ ] T025 Write the evidence README, set the spec status to Implemented with a link to the PR, and run the whole suites: ETL, architecture, maps, links, evidence sizes, Studio and Playwright.
