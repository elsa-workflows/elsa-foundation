# Tasks: Continuous HTTP ETL runs

Spec: [spec.md](spec.md). Plan: [plan.md](plan.md). Tasks are ordered by dependency, and `[P]` marks tasks that can run in parallel.

## Phase 1 — Artifact policy (FR-001, FR-002)

- [x] T001 Add `EtlContinuousRunPolicy` and the init-only `EtlPipelineV2.Continuous`, then update `EtlArtifactV2Json` so that a `null` policy is never serialized. Update `PublicAPI.Unshipped.txt`. (R1)
- [x] T002 Add `EtlContinuousRunPolicy.FindViolation`, covering the three error codes, and call it from the compiler, `EtlConnectorExecutableReader.ValidateLimits` and `EtlConnectorRecordCodec.ValidateBindings`. (R7)
- [x] T003 [P] Tests:
  - the policy round-trips;
  - a finite artifact's root members and content hash equal the finite writer's (the invocation fingerprint derives from that hash);
  - an explicit `null` reads as finite;
  - every error code is produced by the shared rule and by the compiler; the reader refuses the pagination and interval codes, and runtime admission refuses them with an `ArgumentException`.

## Phase 2 — Persistence (FR-005, FR-007, FR-013)

- [x] T004 Add the migration test from R11:
  1. migrate to `20261021170000`;
  2. seed a prior-writer finite HTTP run with the existing legacy seeder (no file run is seeded) and capture its connector-run columns raw;
  3. migrate to latest;
  4. read the rows back unchanged, with the new column null;
  5. assert `HasPendingModelChanges()` is false.
- [x] T005 Add the `CompletionRequestedAtUtcTicks` column, its CHECK constraint, the `20261102170000_ConnectorRunCompletion` migration and the model snapshot. (R5)
- [x] T006 Change the idle invariant in `EfEtlConnectorHistory`. (R4)
- [x] T007 Add `RecordConnectorHttpCaughtUpAsync` to the interface and the EF source store, enforcing the R11 attempt-liveness gate, and forward it in `ForwardingEtlConnectorRuntimeStore`. (R3, R11)
- [x] T008 Add `RequestConnectorRunCompletionAsync`, which wakes only idle partitions. (R5)
- [x] T009 Apply the interval in `AdvanceEmptyHttpPageAsync` after a moving empty page in a continuous run, also while a completion drains. (R6, R11)
- [x] T010 [P] Store tests:
  - the reservation and metadata totals, and no new rows, across caught-up records (checked by the journey's state snapshot and by every store audit);
  - a stale fence, a stale attempt, an out-of-window receipt and a finite run are each refused;
  - a closed-uncertain, `Captured` or `Failed` journal is refused;
  - an idle wait is accepted in a continuous run and refused in a finite one;
  - `Final` relative to the request time (the drain-race journey);
  - each completion result, including `accepted` for a preparing run;
  - only idle partitions are woken.

## Phase 3 — Runtime behaviour (FR-003, FR-004, FR-006, FR-008, FR-009, FR-010)

- [x] T011 Add `EtlHttpRequestBuilder.IsCaughtUp` and the continuous normalization in `EtlHttpPageCache.NormalizeContinuation`. The cache keeps the continuous flag and refuses construction without a builder. (R2, R11)
- [x] T012 Classify responses in `EtlHttpSourceAcquirer`:
  - **Idle:** record the response, then return with no page.
  - **Final:** publish, capture and attach the normalized page.
  - In both cases, re-read the cache before recording.
  - **Continuous publish conflict:** capture the winning manifest. (R11) Implemented; not exercised by a test.
- [x] T013 [P] Acquirer and execution tests against an in-process cursor feed with two shards:
  - 20 or more idle polls leave state unchanged;
  - appended records are delivered once;
  - a moving empty page waits the interval;
  - drain settles `Succeeded`, and `BusinessError` when a record is rejected;
  - a caught-up response from an attempt that started earlier does not end the shard;
  - cancellation wins;
  - a finite empty page with an unchanged cursor still fails with `pagination-cycle`;
  - cancellation also wins over a completion that has not drained.

  The publish-conflict race is not exercised by a test.
- [x] T014 Native journey: a Flowchart running a continuous run resumes through `Done` once completed, and a cancelled run ends `Cancelled`. Covered on the actual Workbench by the T022 driver, not by a separate in-process test; `RunEtl` itself is unchanged.

## Phase 4 — API and inspection (FR-007, FR-011)

- [x] T015 Add the `CompleteRun` endpoint (`Endpoints/CompleteRun/Endpoint.cs`, with `[RequirePermission]`) and:
  - the permission;
  - the resource entry, also fixing the feature description;
  - the operations member, whose default throws 503;
  - 404 through `FindRunAsync`;
  - `EtlRunCompletionView`, registered in `EtlRuntimeApiJsonContext`.

  Update the Runtime/Api `EXTENSION_POINTS.md` and `PublicAPI.Unshipped.txt`. (R11)
- [x] T016 Add the inspection fields `Mode`, `CompletionRequestedAt` and `NextPollAt` as init-only members, and the `ConnectorPartition` provider-contract check. Note the inspector change in the Core `EXTENSION_POINTS.md`. (R11)
- [x] T017 [P] API tests: each result, authorization (403 without the permission) and the inspection fields for idle and retry partitions.

## Phase 5 — Studio (FR-012)

- [x] T018 Studio changes:
  - types and `completeRun` in the API client;
  - the editor switch and the interval field;
  - the inspection labels and "Next poll at";
  - the "Complete run" action, shown only when the host reports a continuous run and resolving `etl-run-complete` only when used;
  - "Eligible after" shown only when `nextPollAt` is null.
- [x] T019 [P] Vitest tests for the editor's visibility rules and the policy shape, the inspection labels and the action. Playwright tests for continuous authoring at 320px and 1024px with axe.

## Phase 6 — Worker processes (FR-004, SC-003)

- [x] T020 Worker-process journeys:
  - a kill after capture leads to a cache replay;
  - a kill during an idle poll's attempt leads to the poll being retried once the attempt expires;
  - both shards are leased and recovered independently (the worker host runs one attempt at a time).

## Phase 7 — Docs and evidence (FR-014, SC-004–SC-006)

- [x] T021 [P] Docs:
  - the ETL README section "Continuous HTTP runs", covering the feed contract, completion, the idle-poll cost, the deadline and the lifetime budgets;
  - the Core README;
  - the Core, Runtime/Api and Studio `EXTENSION_POINTS.md`.
- [x] T022 Run the feed server and the Workbench journey driver on an owned host, and record the summary JSON.
- [x] T023 Real-browser inspection and completion against the owned hosts, recorded as screenshots.
- [x] T024 Record the bite-proofs in `bite-proofs.json` (20 mutations with failing test names, mostly at the round-2 head; later rounds re-recorded or added those their changes touched, each with its own revision): the validator and serialization guards; classification and cache normalization; the reservation release and idle invariant; the fence, window and phase refusals; `Final` versus the attempt start; each completion guard (repeat, terminal, finite) and the selective wake-up; the permission; the moving-cursor interval; `NextPollAt` only when idle and with a claim; the Studio visibility rule. The guards tested without a bite-proof, and the two untested paths, are listed in the evidence README's bite-proof section.
- [x] T025 Write the evidence README, set the spec status to Implemented with a link to the PR, and run the whole suites: ETL, architecture, maps, links, evidence sizes, Studio and Playwright.
