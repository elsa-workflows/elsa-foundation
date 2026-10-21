# Research: qualifying native compensation (M5)

Citations are to `main` at `7fb745f86` (20 October 2026, after the M4 backend #3136). Paths are under `Essentials/src/` unless they say otherwise. Studio paths are under `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`. Facts about the unmerged M4 Studio slice (#3140) are read from `origin/agent/claude/3047-compensation-operator-studio` at `6a7f730fa`.

## R1 — Where a structural activity can wait durably

- **Problem:** spec 251 OD7 deferred backoff because a structural activity cannot hold a bookmark: its continuation can only complete, defer, fault or cancel (spec 251 research R3). Between two handler attempts the scope must wait, durably, without blocking the run.
- **Evidence — the facilities that exist:**
  - **Durable timers.** `Workflows/Runtime/Core/Models/DurableTimer.cs` with `IDurableTimerScheduler`; the pump `Workflows/Runtime/Scheduling/DurableTimerPumpTask.cs` fires each due timer *as a bookmark resume* through `IBookmarkResumeDispatcher` (class remarks, :3-30). `Activities/Scheduling/Activities/Delay.cs` writes the timer before it suspends (:34-58). The pump knows no other dispatch than a bookmark.
  - **Scheduler work queue.** Per run, strictly FIFO: a claim peeks the head and returns nothing when the head is hidden (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/InMemoryWorkflowSchedulerWorkQueue.cs:285-289`). `VisibleAfter` is the claim's visibility timeout and the release time (:296, :376), not a due time. `RuntimeSchedulerWorkItem` has no due time (`Workflows/Runtime/Core/Models/RuntimeSchedulerWorkItem.cs:8-23`).
  - **Post-commit outbox.** A checkpoint's `EnqueueSchedulerWork` intent carries a materialized work item (`RuntimePostCommitIntent.MaterializedSchedulerWorkItem`, `Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs:359-375`). Its outbox item has `AvailableAt` (`RuntimePostCommitOutbox.cs:46-86`), and a pending item is not claimable before it (`RuntimePostCommitOutbox.cs:443-444`). Today only delivery retries set it (`RuntimePostCommitOutboxProcessor.cs:568`, `InMemoryRuntimeCheckpointCommitStore.cs:476-478`).
- **Options:**
  1. **Delayed child schedule (R-7).** The scope schedules the next attempt as usual, with a due time. The schedule work item commits in the evaluation's checkpoint as an `EnqueueSchedulerWork` intent whose outbox item is available at the due time; the outbox sweep enqueues it when due.
  2. **Structural timer callback.** A new seam to request a wake-up: a durable timer with a new stimulus type, a pump dispatch that enqueues a new command kind, and a callback interface (`IRuntimeActivityTimerElapsedHandler`), modelled on R-5's delivery handler.
  3. **A hidden Delay child.** The scope schedules a Delay node before each retry.
  4. **A delayed scheduler work item.** Put a due time on the queue item.
  5. **Wait in process.** `Task.Delay` inside the evaluation.
- **Decision: option 1, R-7** ([contracts/runtime-seam.md](contracts/runtime-seam.md)).
  - It adds one optional argument and no command kind, callback, store or table. The schedule work item, its minted activity execution id and its pinned executable are committed at the failure, exactly as an immediate retry's are (spec 250 FR-008, FR-012).
  - The outbox is already durable, claimed, fenced and swept in every provider, and already honours `AvailableAt`.
  - The program charter says compensation "adds no scheduler, work ledger or lease"; R-7 adds none.
- **Rejected:**
  - Option 2 needs a second dispatch path in the timer pump, a new command kind and a callback. It is more general (it could serve structural timeouts), so it stays the **fallback** if R2's checks fail.
  - Option 3 needs a node in the pinned executable that the author never placed, and makes the compensation module depend on Activities.Scheduling.
  - Option 4 blocks every later item of the run behind the waiting one (FIFO head-of-line), so a hand-over or a cancellation would wait too.
  - Option 5 is not durable and holds the work-item claim and the run's ownership for the whole delay.

## R2 — What R-7 needs from the commit and delivery paths

- **Evidence:**
  - Durable stores honour `AvailableAt` for pending items: the model's claim filter (`RuntimePostCommitOutbox.cs:443-444`), the in-memory store's ordering (`InMemoryRuntimeCheckpointCommitStore.cs:432, 1011`), and the EF store's `DeliverableAt` projection (`Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs:696-711`).
  - The coalescing overlay (Balanced and Ephemeral) orders by `AvailableAt` and delivers only items with `AvailableAt <= now` (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Coalescing/RuntimeCoalescingSession.cs:573-599`).
  - The in-process hop publishes every `EnqueueSchedulerWork` intent that has a materialized work item, without looking at availability (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs:183-199`).
  - A checkpoint is forced durable when it is marked mandatory (`RuntimeCheckpointCommitter.cs:207-210`, `IsMandatoryCheckpoint`).
- **Decision:**
  - `RuntimePostCommitIntent` gains `NotBefore`; the committer writes it to the outbox item's `AvailableAt`.
  - The in-process hop and the immediate post-commit dispatch skip an intent whose `NotBefore` is later than the commit time.
  - A checkpoint that carries one is mandatory.
  - A coalescing session that ends with an undelivered delayed item flushes it as a durable pending item, exactly as any other undelivered overlay item.
- **To verify in PR 1 (T501):**
  - the immediate dispatch path (`RuntimePostCommitIntentDispatcher`, `RuntimeSchedulerPostCommitIntentDispatcher`) is the only one besides the hop that can deliver before a sweep;
  - an overlay item's `AvailableAt` survives the end-of-drain flush in Balanced and Ephemeral;
  - the recovery candidate selector (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Recovery/RuntimeRecoveryCandidateSelector.cs`) and run health do not report a run whose only work is a pending delayed item as stalled (FR-504).
  If one of these cannot hold without new machinery, stop and switch to option 2 of R1 before writing the scope change.

### R2 outcome (T501, PR 1)

The stop-and-replan gate held: the outbox `AvailableAt` design works, and the structural timer fallback (R1 option 2) was not needed. The four checks were written first as `RuntimeDelayedScheduleDeliveryTests` (`Essentials/tests/Workflows/Runtime/Tests`) against an intent carrying `NotBefore`, and all four failed before the commit path changed:

- **Hop.** `PublishInProcessHopWorkItems` published the delayed continuation to the live drain's carrier. The carrier is only a payload cache (`RuntimeSchedulerPostCommitIntentDispatcher.ResolveWorkItem`), so this alone never delivered early, but the committer now skips a delayed intent so the cache holds nothing it will not use.
- **Immediate dispatch.** The drain's own delivery step (`RuntimePostCommitOutboxProcessor`, claim-free `GetDeliverableAsync` and the claim path) delivered the item at once only because its `AvailableAt` was the commit time. Both paths already filter on `AvailableAt`; writing the due time there (`RuntimePostCommitOutboxItems.AvailableAt`) was enough. No other path delivers before a sweep: the orchestrator's settlement step only lists claimed items, and the dispatchers deliver what the processor selected.
- **Balanced and Ephemeral.** A deferrable checkpoint carrying the item was folded into the coalescing overlay and written only at the end-of-drain flush. `CheckpointRequirement` metadata did not help: the coalescing policy ignores it, and only the background-work override forced a flush. The committer now treats a delayed intent like background work (mandatory and `Immediate`), so the coalescing store folds the buffered segment with this commit into one durable write at once, and the item keeps its `AvailableAt` through the end-of-drain flush.
- **Recovery and run health.** The run was re-driven early only because the sweep delivered the item early. The recovery candidate selector reads leases, heartbeats and interruptions, and a quiesced drain holds none, so it never selects a run that only waits. "Run health" is the dashboard's status aggregate (`IWorkflowRunHealthDataSource`), which has no notion of a stall and counts the run as running. No change was needed in either.

Two details the research did not foresee: the due time travels on the child's `ScheduleActivity` work item as `runtime.scheduleNotBefore` (one rule in `SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent` covers every evaluation path), and the schedule handler applies the FR-503 checks only to a work item that carries it, so every ordinary schedule keeps exactly today's checks (the existing schedule-handler suites pass unchanged).

## R3 — Precision and the sweep

- **Evidence:** the outbox is swept by `RuntimeResumptionService` (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Recovery/RuntimeResumptionService.cs:59, 90`), driven by `RuntimeResumptionPumpTask` every `RuntimeResumptionOptions.SweepInterval`, 10 seconds by default (`Workflows/Runtime/Resumption/Options/RuntimeResumptionOptions.cs:12`). The durable timer pump also sweeps every 10 seconds (`Workflows/Runtime/Scheduling/Options/DurableTimerPumpOptions.cs:11`).
- **Decision:** the configured delay is a minimum. A retry runs at the first sweep after its due time, so up to one sweep interval later. The guide says so. No wake-up timer is added. A host composed without the resumption sweep cannot deliver delayed items, so staging one faults the evaluation (FR-505) instead of waiting for ever; the capability is registered by the feature that registers the sweep.

## R4 — Backoff inputs and formula

- **Evidence:** the scope retries in `OnHandlerFaulted` (`Activities/Compensation/Activities/CompensationScope.cs:669-704`) through `ScheduleHandler` (:865-919), which records a new `Running` attempt in the same commit. Inputs are plain `[ActivityInput]` properties with literal defaults (`handlerMaxAttempts`, :188-189); M2 and M3 added inputs the same way.
- **Decision:**
  - Inputs `handlerRetryDelay` (TimeSpan, `00:00:00`), `handlerRetryBackoffMultiplier` (double, `2`), `handlerRetryMaxDelay` (TimeSpan, `00:05:00`).
  - Wait before attempt n+1 = `min(cap, delay × multiplier^(n−1))`, where n is the number of failed attempts so far.
  - The due time is the scope's clock at the failure plus the wait; the scope reads `TimeProvider`, as `Delay` does (`Activities/Scheduling/Activities/Delay.cs:23, 34`), so tests drive it with the fake clock `CompensationHost` already uses.
  - No jitter: one run has one retry chain, and deterministic due times make restarts testable. Jitter can be an additive input later.
  - Operator retries are immediate: an operator chose to run it now.
- **Compatibility:** the default delay is zero, so the zero branch is today's code path, unchanged (SC-501). Runs already in flight keep their pinned inputs, so they never gain a delay (OD17 B would change only newly published scopes).

## R5 — FR-014 was not implemented, and what it needs

- **Evidence:**
  - Spec 250 FR-014 records the deferral: "design-time validation has no side-effect profile for an activity version".
  - No compensation validator reads a profile (`Activities/Compensation/Internal/CompensationScopeValidator.cs`, `CompensationHandlerVariableValidator.cs`, `CompensationTargetValidator.cs`).
  - The profile is resolved only by the compiler, from the CLR attribute (`Workflows/Publishing/Elsa.Workflows.Publishing/Services/ExecutableNodeCompiler.cs:459-467`), via the type resolver (`ClrActivityTypeResolver.cs:13-26`).
  - A Draft node names an activity version id (`Workflows/Design/Core/Models/ActivityNode.cs:25-31`). Warnings exist: `ValidationError.Severity` with `ValidationSeverities.Warning`, which "does not block" (`Workflows/Design/Validations/Core/Models/ValidationError.cs:34-48`); promotion filters warnings out (`Workflows/Design/Persistence/EntityFrameworkCore/Commands/EfWorkflowDesignCommands.cs:431-436`); the Connections validator emits one (`Connections/Design/Validators/ConnectionDraftValidator.cs:112`).
  - `ReplaySafe` activities: Write Line, Write Lines, Fault, Inline, Break, Do, If, Switch, Sequence, Flowchart, For, For Each, While, Parallel, Write HTTP Response, and the compensation scope itself.
- **Decision (OD20 A):**
  - A Core contract `IActivitySideEffectProfileReader` (`ReadAsync(activityVersionId)` returning the profile, or nothing when it cannot be resolved), in `Elsa.Activities.Runtime.Core` next to `SideEffectProfile`.
  - Its implementation lives in Publishing and shares one helper with `ExecutableNodeCompiler.ResolveSideEffectProfile`, so the two cannot drift.
  - A new compensation validator `CompensationReplaySafeValidator` warns on a `ReplaySafe` leaf as a step with a compensation, or as a handler. It skips structural activities (any node with a `Structure`) and resolves nothing when no reader is registered.
- **Rejected:** a design facet carrying the profile on the activity version, written by the CLR scanner (`Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`). It changes the version content, and so the hash, of every existing `ReplaySafe` activity version, which reconciliation would see as drift. **To verify in T509:** whether a facet would in fact change the hash; if it would not, the facet is the simpler home and the reader becomes a facet read.

## R6 — Studio blocks publication on warnings today

- **Evidence:**
  - Studio's draft `ValidationError` has no severity (`workflowTypes.ts:183-191`).
  - The publish flow turns every draft validation entry into a blocking message (`workflow-editor/useWorkflowOperations.ts:267-271`), and the review dialog blocks while any is listed (`workflow-editor/PublicationReviewDialog.tsx:82, 121, 131`).
  - The validations view does carry severity (`Workflows/Design/Api/Models/DraftValidationsView.cs:26-40`). `DraftValidationError.severity` exists for the validations endpoint (`workflowTypes.ts:528-534`), but the publish path does not use it.
- **Decision:**
  - Read severity on both shapes; list warnings under their own heading with text and icon; keep **Publish** enabled when only warnings remain (FR-515).
  - **To verify in T511:** whether the saved draft's `validationErrors` already include warnings (then a Connections warning blocks Studio publication today, and this is a bug fix with its own regression test).

## R7 — The broken M1 live journey (#3142)

- **Evidence:**
  - `Studio/tests/live/compensation-journey.spec.ts` publishes with "Review and Publish" and runs with "Run" and "Run workflow" (:50-60).
  - The M4 journey on the Studio branch uses "Review & publish", the "Publish workflow" dialog, and "Test Run" then "Start Test Run" (`Studio/tests/live/operator-actions-journey.spec.ts`, `authorAndPublishOrderWorkflow` and `startTestRun`).
  - Spec 260 research R19 (on the M4 Studio branch) records that the M1 journey "still uses the older … labels and fails against the current Studio".
  - Both files copy the same `signIn`, `pick`, `fill`, `rename` and axe helpers.
- **Decision:**
  - Move the helpers into `Studio/tests/live/support/journey.ts` (sign in, author, publish, start a test run, open a scope, wait for a phase, axe at both widths) and use them from every live journey.
  - Repair the M1 journey on top of them.
  - Its handlers are Write Line, which is `ReplaySafe`, so under OD20 A its publish step must also see the new warnings and publish anyway: the M1 journey doubles as the live proof of FR-515.

## R8 — Provider coverage today

- **Evidence:**
  - Compensation journeys run on a SQLite file in-process: "Host generations over one SQLite file in one persistence mode" (`Essentials/tests/Activities/Compensation/Tests/Support/CompensationHost.cs:5-10`; provider at :306-311).
  - The Workbench owned-process tests use the Workbench's SQLite composition (`Essentials/tests/Workbench/Tests/WorkbenchCompensationRecoveryTests.cs`) and run in `Build & test`.
  - The server providers are covered only by the nightly and dispatch-only EF container suites (`.github/workflows/ci.yml:223-320`; manifest `tools/ci/ef-suites.json`). The `runtime` suite proves the stores the scope relies on (checkpoint participants, scheduler queue, outbox contention, alterations, incidents) on PostgreSQL, SQL Server and MySQL, through `RuntimeBookmarksPostgreSqlFixture`, `…SqlServerFixture` and `…MySqlFixture` (`Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/`), but runs no structural activity end to end.
  - Oracle has a scoping report only (`docs/reports/ef-core-oracle-scoping-2026-08.md`).
  - Multi-node placement has its own suite (`runtime-distributed`) and no compensation journey.
- **Decision (OD18 A):**
  - Make `CompensationHost` take a provider.
  - Add `Essentials/tests/Activities/Compensation/Persistence/EntityFrameworkCore/ProviderTests` reusing the runtime fixtures, with a required-provider switch `ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS`, registered as suite `compensation` in `tools/ci/ef-suites.json`.
  - Its subset is in [contracts/qualification-matrix.md](contracts/qualification-matrix.md#provider-subset).

## R9 — Migration and serialization

- **Evidence:**
  - Compensation adds no table in M1–M4 (data models of specs 250, 251, 258 and 260).
  - What changed is the scope's private-state payload: v1 to v4 through `CompensationScopeStateUpcaster`, persisted by `CompensationScopeStatePersister`. The upcasts are proven on payloads (`CompensationScopeUncertaintyTests.cs:203-260`, `CompensationScopeDecisionTests.cs:430-441`, nesting tests).
  - No test resumes a live waiting run from an older payload end to end.
  - Wire contracts: `compensation.journal/1` (projector tests), `RetryFaultedActivity/1` and `DecideIntervention/1` (runtime tests), and the incident outcome metadata keys (spec 260 data model).
- **Decision:**
  - Add v4→v5 to the upcaster.
  - Add an integration test per older version: drive a run to a waiting point, overwrite the scope's committed private state with the equivalent older payload (as an older build would have written it), restart, and finish the recovery (FR-518).
  - Run the private-state round trip on each server provider as part of the subset (JSON column behaviour).

## R10 — Retention

- **Evidence:**
  - A scope clears its private state when it completes, and the state view then returns `StateCleared` (spec 260 FR-415, T413).
  - Run deletion works only for terminal runs, atomically with their run-owned state (`Workflows/Runtime/Core/Contracts/IWorkflowExecutionDeletionStore.cs:3-12`). Operational delivery receipts are outside that boundary.
  - Executables are kept by retention roots and collected by `WorkflowExecutableReferenceGarbageCollectionPumpTask`.
  - A Studio test run's executable reference expires after `StartWorkflowTestRunRequestHandler.DefaultRetention`, 30 minutes (`Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs:83, 151`). The M4 live journey and the Workbench operator test use test runs (spec 260 research R18 and R19, on the M4 Studio branch).
- **Decision:** prove four things (FR-519):
  1. deleting a compensated run removes its incidents and so its operator history;
  2. a running or waiting run is refused;
  3. a pending delayed schedule keeps its executable from collection;
  4. what a waiting scope in a test run does when the window ends.
  For point 4, PR 2's first task reads `ITestRunRootCanceller` and the test-scope cleanup. If expiry cancels the run, the guide says "a test run that waits for an operator is cancelled after 30 minutes; use a published run" and nothing else changes. If expiry leaves the run running but unresumable, that is a defect: file it and list it as a known limit.

## R11 — Docs drift found while reading

The full list is in [contracts/docs-and-samples.md](contracts/docs-and-samples.md#audit). The two that change meaning:

- **Step results.** "Pass data from a step to its compensation" teaches only a workflow variable. Shipped behaviour also binds a handler input to the paired step's result directly, through causal lineage (spec 250 FR-010, FR-016; `WorkbenchCompensationRecoveryTests.cs:362-367` uses `ActivityResultArgument`). In a loop the variable is overwritten by later iterations, so the direct reference is the safe default.
- **Reconciliation.** The glossary's "Reconciliation" means catalog reconciliation (`docs/glossary/elsa.md:84`). Saga reconciliation (finding out whether an uncertain effect exists) needs its own disambiguated row, or the four-way table will send readers to the wrong concept.

## R12 — Elsa 3

- **Evidence:**
  - #3048 asks for an assessment linking elsa-core#4015, "the Elsa 3 compensation request".
  - The program charter tracks the Elsa 3 implementation as elsa-core#8884, paused until the M0 packet was reviewed ([native-compensation.md](../../docs/program-goals/native-compensation.md#relationship-to-other-programs)).
  - This session cannot read either issue.
- **Decision:**
  - The assessment links both and claims nothing about their content beyond what the charter and #3048 say.
  - It is built from what this repository owns: the semantic scenarios in the M1–M4 quickstarts, rewritten engine-neutrally ([contracts/elsa3-follow-on.md](contracts/elsa3-follow-on.md)).
  - Implementation stays a separate effort.
