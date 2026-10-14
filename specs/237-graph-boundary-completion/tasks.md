---
description: "Tasks for spec 237: graph boundary completion from any depth"
---

# Tasks: Graph Boundary Completion From Any Depth

**Input**: Design documents from `specs/237-graph-boundary-completion/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/complete-activity.md](contracts/complete-activity.md), [quickstart.md](quickstart.md)

**Tests**:
- These are risk-based behaviour tests under framework §2.23 and ADR 0080 D1. xUnit only, with no
  FluentAssertions. Assert on codes, statuses and store contents, never on message text.
- No feature-registration test is needed except for the draft validator, whose wiring is the only new registration.
- **Undeclared cadence**: the Elsa constitution declares no test cadence (§2.21.2, Elsa amendment D9). Tests are
  ordered test-after within each story. The one exception is T004, which is a risk gate that must pass before
  feature code builds on R2. The application owner may declare a cadence; this unit does not infer one.

**Paths**: all source and test paths are relative to the repository root; `Essentials/` holds the .NET tree.

**Builds**: build and test only the touched projects listed in [quickstart.md](quickstart.md); the machine is
shared, per the AGENTS.md section "Builds on a shared machine".

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

**Purpose**: confirm the baseline before changing anything.

- [X] T001 Re-check for competing work on #2750 and for an open `main is red: CI` issue (`gh issue list --search "main is red"`), then record the baseline: run `dotnet test` for `Essentials/tests/Activities/Graph/Tests/Elsa.Activities.Graph.Tests.csproj`, `Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj` and `Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj`, and note any pre-existing failures in the PR description draft.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the generic ancestor-targeted notification (research R1, R2). Every user story depends on it.

**Risk gate**: T004 must pass before T005 onward. If it cannot be made to pass, stop, switch to the R2 fallback (`GraphActivity` ignores late completions from a `Complete` child), and amend [plan.md](plan.md) and [research.md](research.md) before continuing.

- [X] T002 Add `RuntimeAncestorNotificationRequest(string AncestorConsumerKey, string Code, IReadOnlyDictionary<string,string> Payload)` in `Essentials/src/Workflows/Runtime/Core/Models/RuntimeAncestorNotificationRequest.cs`, and add `void RequestAncestorNotification(string ancestorConsumerKey, string code, IReadOnlyDictionary<string, string>? payload = null)` plus `IReadOnlyCollection<RuntimeAncestorNotificationRequest> GetAncestorNotificationRequests()`; as built, the payload is `JsonElement?` like parent notifications (match the shape of the existing `RequestParentNotification` / `GetParentNotificationRequests` pair) to `Essentials/src/Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs`. In the same file, correct the stale XML doc on `RequestChildSubtreeCancellation` so it lists child-notification evaluations as permitted (handler `WorkflowNotifyParentActivitySchedulerWorkHandler` L176-184).
- [X] T003 Implement staging in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/ActivityExecutions/SimpleActivityExecutionContext.cs`: validate that the arguments are non-blank, and stage the requests the way `RequestParentNotification` does (L154-157). The root-staging and Fault/Cancel restrictions that apply to parent notifications also apply here.
- [X] T004 Write the gate tests in `Essentials/tests/Activities/Runtime/Tests/AncestorNotificationExecutionTests.cs`, using `WorkflowExecutionHarness.Create().WithProbeLeaf()` and `StructuralExecutionTestSupport` as `ChildSubtreeCancellationExecutionTests.cs` does. Define a probe structural notifier and a probe ancestor that has a test consumer key and implements `IRuntimeActivityChildNotificationHandler`. Cover:
  - (a) A notifier two levels below the ancestor, with a plain probe composite between them, reaches the ancestor. The intermediate composite receives nothing.
  - (b) With two matching ancestors, only the nearest receives the notification.
  - (c) No matching ancestor: the notifier faults with code `runtime.ancestor-notification.target-not-found`.
  - (d) Ordering: the ancestor's `OnChildNotifiedAsync` runs before the notifier's direct parent receives `OnChildCompletedAsync`, and when the ancestor completes during the notification, the direct parent never receives that completion.
  - (e) A notification to an ancestor that is no longer `Running` is dropped without fault.
  - Run (d) on the in-memory queue here. The EF queue ordering is covered by T024.
- [X] T005 Resolve ancestor targets in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/ParentNotificationEvaluation.cs`:
  - Add a builder path for `GetAncestorNotificationRequests()` that walks `ParentActivityExecutionId` from the notifier's committed parent upward through `IActivityExecutionStateStore.FindAsync`, following `RuntimeContainerScopeService.BuildVisibleFramesAsync`.
  - At each ancestor, look up `Execution.ExecutableNodeId` in the pinned executable and compare `Descriptor.ConsumerKey` ordinally.
  - Build the same `RuntimeNotifyParentCommandPayload` / `NotifyParentActivity` work item, addressed to the matched ancestor.
  - When nothing matches, throw a dedicated internal exception that the invoke handler converts into an `ActivityFault` with code `runtime.ancestor-notification.target-not-found`. The fault's message names the notifier's executable node id and the consumer key.
- [X] T006 Order the intents in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`, near L300-313 and L1066: harvest the ancestor requests next to the parent notifications, and enqueue ancestor-notification work items **before** the notifier's own completion intent. Parent notifications keep their order. Map the T005 exception to the fault. Apply the same ordering in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/StructuralParentEvaluationSupport.cs` (L485-486) only if ancestor requests can be staged during child evaluations; otherwise reject them there with an `InvalidOperationException` and a test in T004.
- [X] T007 Run T004 until it passes, then run the whole `Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj` suite to catch regressions in parent-notification and subtree-cancellation behaviour.

**Checkpoint**: the runtime can address the nearest ancestor by consumer key, ahead of the notifier's own completion.

---

## Phase 3: User Story 1 - End a reusable activity from deep inside its body (Priority: P1) 🎯 MVP

**Goal**: a `Complete` node at any depth ends the nearest graph boundary with its outcome (FR-001–FR-009,
FR-018, FR-019).

**Independent Test**: a reusable activity with outcomes `succeed` and `fail`, whose Flowchart body reaches
`Complete(fail)`, makes the parent Flowchart follow only its `fail` connection.

### Implementation for User Story 1

- [X] T008 [P] [US1] Add `CompleteOutcomes` (`IReadOnlyList<GraphActivityCompleteOutcomeDescriptor>`, record `(string ReferenceKey, string BoundaryOutcomeName)`) to `Essentials/src/Activities/Graph/Runtime/Models/GraphActivityDescriptor.cs`. Add it through an additional `[JsonConstructor]` overload, keeping the existing constructors, as was done for `OutcomeMappings` (L41-55). Validate that reference keys are unique and names are non-blank. Default to empty when the JSON property is absent.
- [X] T009 [P] [US1] Create `Essentials/src/Activities/Graph/Runtime/Activities/Complete.cs`:
  - `public sealed class Complete : StructuralActivity, IRuntimeStructuralActivity` with `[ActivityInput(Key = "outcome")] public string? Outcome { get; set; }`, `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]`, no child slots and no `[ActivityOutcome]`.
  - `ExecuteStructureAsync` returns `RuntimeStructuralContinuation.Faulted(new ActivityFault("activity.graph.complete.outcome-required", …))` when `Outcome` is blank.
  - Otherwise it calls `RequestAncestorNotification(WellKnownRuntimeActivityConsumers.GraphActivity, "elsa.graph.complete", { ["outcome"] = Outcome })` and returns `RuntimeStructuralContinuation.Complete()`.
  - Put the notification code and payload key in an internal constants class `Essentials/src/Activities/Graph/Runtime/GraphCompleteNotification.cs`, shared with `GraphActivity`.
  - XML-doc the nearest-boundary semantics and point to `contracts/complete-activity.md`.
- [X] T010 [US1] Extend `Essentials/src/Activities/Graph/Runtime/Activities/GraphActivity.cs`:
  - Implement `IRuntimeActivityChildNotificationHandler.OnChildNotifiedAsync`. For code `elsa.graph.complete`:
    - resolve `payload["outcome"]` in `Descriptor.CompleteOutcomes`, or return `Faulted(activity.graph.complete.outcome-unknown)`;
    - set `_selectedOutcomeName`;
    - call `RequestChildSubtreeCancellation` for every live direct child from `GetLiveChildActivities()`;
    - return `Complete(name)`.
  - Any other code returns `Defer`.
  - In `OnChildCompletedAsync`, when `OutcomeMappings` is empty and `CompleteOutcomes` is not, return `Faulted(activity.graph.entry-completed-without-mapping)`.
  - In `PrepareCompletionCheckpointAsync` (L198-201), use `_selectedOutcomeName` whenever it is set, before the `OutcomeMappings.Count == 0 ? Done` fallback.
- [X] T011 [US1] Add unit tests for the descriptor and `GraphActivity` in `Essentials/tests/Activities/Graph/Tests/GraphActivityExecutionTests.cs`. Cover:
  - descriptor JSON without `completeOutcomes` deserializes with an empty list and round-trips byte-identically;
  - a duplicate reference key throws;
  - the notification with a known key selects the name and requests cancellation of the entry execution;
  - an unknown key faults with `activity.graph.complete.outcome-unknown`;
  - an unknown code defers;
  - natural completion with no mappings and non-empty `CompleteOutcomes` faults with `activity.graph.entry-completed-without-mapping`;
  - `PrepareCompletionCheckpointAsync` returns the selected exit outcome when `OutcomeMappings` is empty;
  - all existing tests in the file still pass unchanged.
- [X] T012 [US1] Add engine-driven exit tests in `Essentials/tests/Activities/Behavioral/Tests/GraphBoundaryCompleteTests.cs`, building on `Essentials/tests/Activities/Behavioral/Tests/Drives/GraphActivityDrive.cs`, with a pinned descriptor carrying `CompleteOutcomes`. One test for each:
  - `Complete` under Flowchart → the boundary outcome is `fail` and the parent routes only `fail`;
  - under Sequence → later steps never run;
  - under If and under Switch;
  - inside a ForEach body on iteration 2 → no iteration 3;
  - inside a While body;
  - inside one Parallel branch → the boundary completes without waiting for the other branch;
  - nested graph A ⊃ B ⊃ `Complete(x)` → only B completes with `x` and A continues;
  - `Complete` as the graph entry → immediate completion;
  - a graph whose entry completes naturally → the mapping outcome as today;
  - `Break` inside the same body still ends only its loop;
  - output mappings are captured at exit, and a missing required output faults the boundary;
  - two Parallel branches reach `Complete` → the first committed outcome wins and the workflow does not fault;
  - `Complete` placed directly in a workflow with no boundary → faults with `runtime.ancestor-notification.target-not-found` and the workflow is not finished.
- [X] T013 [US1] Run `Essentials/tests/Activities/Graph/Tests/Elsa.Activities.Graph.Tests.csproj` and `Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj`. Fix failures in the production code, not by weakening the assertions.

**Checkpoint**: User Story 1 works at run time when a pinned descriptor carries `CompleteOutcomes`. The design
side comes in User Story 3.

---

## Phase 4: User Story 2 - Remaining work in the body stops for good (Priority: P1)

**Goal**: an exit durably deletes the body's bookmarks, durable timers and queued work in the same commit
(FR-010–FR-012, SC-002).

**Independent Test**: a body with one sibling suspended on a bookmark and one waiting on a durable timer leaves
nothing in either store after `Complete`, on in-memory and on EF Sqlite.

- [X] T014 [US2] Create `Essentials/tests/Activities/Behavioral/Tests/GraphBoundaryCompleteDurableCancellationTests.cs`. The boundary's Flowchart body forks into:
  - branch A, an activity that suspends on a bookmark (reuse the bookmark probe or `Event` used in existing Behavioral drives);
  - branch B, a durable-timer activity from `Elsa.Activities.Scheduling`;
  - branch C, `Complete(done-early)`.
  
  After the exit, assert with `FakeTimeProvider` that:
  - `IBookmarkStateStore` lists no bookmark for the workflow execution;
  - `IDurableTimerStore` holds no timer for it;
  - no scheduler work item remains for the body;
  - every body execution except `Complete` has status `Cancelled`, and `Complete` is `Completed`;
  - advancing time past the timer's due time and sweeping (`SweepUntilQuietAsync`) runs nothing in the body;
  - resuming with the old bookmark's stimulus resumes nothing;
  - the parent workflow's sibling branch outside the boundary keeps its own bookmark (FR-012).
- [X] T015 [US2] Parameterize T014 over two store setups: the default in-memory harness, and EF Sqlite using the setup from `Essentials/tests/Activities/Scheduling/Tests/DurableTimerRestartCrashTests.cs` (~L176-200: `AddRuntimeEntityFrameworkCore` with provider `Sqlite`, `AddEfModuleMigrations<RuntimeDbContext>("Sqlite")`, `DurableTimerPumpTask`). Add the needed project reference to `Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj` only if it is missing. Clear only the test's own SQLite pool at teardown.
- [X] T016 [US2] Run both variants. If a store keeps state, fix the cancellation path through `RequestChildSubtreeCancellation` in `GraphActivity.cs`. Do not touch Flowchart, Parallel or `finish`; those belong to #2752.

**Checkpoint**: SC-002 is proven on both stores.

---

## Phase 5: User Story 3 - Invalid placements are caught before they run (Priority: P2)

**Goal**: provider validation and compilation of `Complete` nodes, and rejection in plain workflows
(FR-013–FR-017, FR-016, SC-004).

**Independent Test**: publishing `Complete(unknown)` fails with `activity.graph.complete.outcome-unknown`;
publishing a plain workflow containing `Complete` fails with `Graph/CompleteOutsideGraph`.

- [X] T017 [US3] In `Essentials/src/Activities/Graph/Design/Services/GraphActivityProvider.cs`, add `IActivityDefinitionLookup` (from `Essentials/src/Activities/Design/Core/Contracts/IActivityDefinitionLookup.cs`) as a constructor dependency, and add an internal `CompleteNodeDetector`:
  - At compile time, a node is a `Complete` when its resolved dependency's `DefinitionId == ActivityCatalogStableIds.DefinitionId("Elsa.Activities.Graph.Runtime.Activities.Complete")`. Cache the stable id in a static field.
  - At draft time, a node is a `Complete` when `IActivityDefinitionLookup` resolves its `activityVersionId` to that type key. An unresolved version id is treated as "not a `Complete`".
  - Update `GraphActivitiesDesignFeature.cs` registration if the constructor change needs it, and update the test constructor helpers in `Essentials/tests/Activities/Graph/Tests/GraphActivityProviderTests.cs` (and `ActivityProviderConformanceTests.cs`) with a fake lookup.
- [X] T018 [US3] Add the validation rules in `GraphActivityProvider.cs`:
  - read each `Complete` node's `outcome` input from `node.Activity.Inputs` (`ArgumentState`/`ArgumentValue`); require exactly one entry whose `ExpressionType` is `Literal` and whose value is a non-blank JSON string, else `activity.graph.complete.outcome-literal-required` at `node.JsonPointer + "/inputs"`;
  - require the value to name an emitted contract outcome reference key, else `activity.graph.complete.outcome-unknown`;
  - in schema 1, report every `Complete` node as `activity.graph.complete.schema-unsupported`;
  - change `ValidateBoundaryOutcomeMappings` (L431-483) to take the set of reference keys named by `Complete` nodes, so `activity.graph.outcome-mapping-required` fires only when neither a mapping nor a `Complete` node names the outcome.
  
  Run these rules from both `ValidateAsync` and `CompileAsync`, so publication is exact.
- [X] T019 [US3] Emit `completeOutcomes: [{ referenceKey, boundaryOutcomeName }]` in `BuildBoundaryPayload` (L916-996) for schema 2, **only** when at least one `Complete` node exists. Order entries by reference key ordinally, list each key once, and resolve names through the contract outcomes as `outcomeMappings` does (L925-932). Leave `CompilerFingerprint` and `RuntimeDescriptorSchemaVersion` unchanged.
- [X] T020 [US3] Add provider tests in `Essentials/tests/Activities/Graph/Tests/GraphActivityProviderTests.cs`, using the existing `CreateSchemaTwoManifest`, `OutcomeContract` and `CreateDependency` helpers. Pass the `Complete` stable definition id for `Complete` occurrences. Cover:
  - a Theory over the four new codes;
  - an outcome reached only by `Complete` passes;
  - an outcome reached by neither still fails with `outcome-mapping-required`;
  - a `Complete` nested under Flowchart → If → Sequence is found;
  - the compiled payload contains `completeOutcomes` with resolved names;
  - a graph without `Complete` compiles to a payload byte-identical to the current output (capture it before T019 and compare);
  - draft-time `ValidateAsync` detects `Complete` through the fake lookup.
- [X] T021 [P] [US3] Create `Essentials/src/Activities/Graph/Design/Services/CompleteOutsideGraphDraftValidator.cs`, an internal `IDraftValidator`:
  - for each node of the workflow definition draft, resolve the version through `IActivityDefinitionLookup` and return `ValidationError(path, "Graph/CompleteOutsideGraph", …)` for every `Complete` node;
  - follow `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchDuplicateCaseValidator.cs` for the draft traversal and path format;
  - add the `ProjectReference` to `Essentials/src/Workflows/Design/Validations/Core/Elsa.Workflows.Design.Validations.Core.csproj` in `Essentials/src/Activities/Graph/Design/Elsa.Activities.Graph.Design.csproj`;
  - register it with `services.AddScoped<IDraftValidator, CompleteOutsideGraphDraftValidator>()` in `Essentials/src/Activities/Graph/Design/GraphActivitiesDesignFeature.cs`;
  - update `packages.lock.json` if restore changes it.
- [X] T022 [US3] Add `Essentials/tests/Activities/Graph/Tests/CompleteOutsideGraphDraftValidatorTests.cs`, modelled on `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchDuplicateCaseValidatorTests.cs`. Cover:
  - a root-level `Complete` is reported;
  - a `Complete` nested in a Flowchart is reported;
  - a draft with no `Complete` returns nothing;
  - an unresolvable version is ignored;
  - a registration test that `GraphActivitiesDesignFeature` adds the validator as an `IDraftValidator` (§2.23.1; this is the one new logic-relevant registration).
- [X] T023 [US3] Run `Essentials/tests/Activities/Graph/Tests/Elsa.Activities.Graph.Tests.csproj` and confirm the end-to-end compile path: extend one T012 case to build its descriptor through `GraphActivityProvider.CompileAsync` plus placement, instead of a hand-made descriptor, if the Behavioral drive supports it. Otherwise cover the path in the e2e script (T027).

**Checkpoint**: invalid placements are rejected before run time, and valid graphs compile `completeOutcomes`.

---

## Phase 6: User Story 4 - Existing reusable activities keep working (Priority: P2)

**Goal**: FR-015 and SC-003. Nothing that validates today changes.

- [X] T024 [US4] Add a regression test in `Essentials/tests/Activities/Graph/Tests/GraphActivityProviderTests.cs`: every existing schema-1 and schema-2 fixture manifest in the file produces the same diagnostics and the same compiled payload bytes as before (snapshot taken at T001's commit). Extend T004(d) with an EF Sqlite queue variant so the ordering gate is proven on both queues.
- [X] T025 [US4] Update the catalog contract surfaces:
  - add `Elsa.Activities.Graph.Runtime.Activities.Complete` to `Essentials/tests/Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json`, regenerating it with the test's documented update mechanism rather than by hand where one exists;
  - add it to `Essentials/tests/Activities/Behavioral/Tests/Infrastructure/ActivityContractInventory.cs`, and to `Essentials/tests/Activities/Behavioral/Tests/ActivityCoverageTests.cs` if that list requires it;
  - check what outcomes facet the scanner emits for an activity without `[ActivityOutcome]`, and record the finding in the PR (Studio follow-up only, per D8).
- [X] T026 [US4] Run `Essentials/tests/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj`, `Essentials/tests/Architecture/Tests` (the §E2.2 Runtime-no-Design guard and the feature composition test), and the four suites in [quickstart.md](quickstart.md).

---

## Phase 7: Polish and cross-cutting concerns

- [X] T027 [P] Create `e2e-tests/reusable-activities/Test-ReusableActivityComplete.ps1`, modelled on `Test-ReusableActivityOutcomeLimit.ps1` and `_ReusableCommon.ps1`:
  - publish a schema-2 reusable activity with outcomes `succeed` and `fail`, whose Flowchart body branches to `Complete(succeed)` or `Complete(fail)` on an input, with no `outcomeMappings`;
  - run it from a parent Flowchart for both inputs and assert that only the matching branch runs;
  - assert that publishing `Complete(unknown)` is rejected with `activity.graph.complete.outcome-unknown`;
  - assert that promoting a plain workflow containing `Complete` is rejected with `Graph/CompleteOutsideGraph`;
  - list it in `e2e-tests/reusable-activities/README.md`.
- [X] T028 [P] Document the emitter contract (FR-021) in `docs/reference/activity-definition-authoring-composition.md`: add a section "Ending a reusable activity from any depth" summarising [contracts/complete-activity.md](contracts/complete-activity.md) (type key, input key, literal rule, reachability rule, diagnostics, run-time semantics, `Break` unchanged), with a link to spec 237. Add a one-line pointer on the "Graph activity" entry in `docs/glossary/elsa.md`.
- [X] T029 [P] Update the extension-point catalogs (§2.22):
  - `Essentials/src/Activities/Graph/Runtime/EXTENSION_POINTS.md`: the `Complete` activity, and `GraphActivity` as a child-notification handler for `elsa.graph.complete`;
  - `Essentials/src/Activities/Graph/Design/EXTENSION_POINTS.md`: the `CompleteOutsideGraphDraftValidator` as an `IDraftValidator` known implementation (cross-domain), the provider's `IActivityDefinitionLookup` dependency, and a fix to the stale schema row so it lists `1` and `2`;
  - the Workflows Runtime Core catalog that documents `IRuntimeActivityExecutionContext`: the new `RequestAncestorNotification` member;
  - the root `EXTENSION_POINTS.md`, if its index rows change.
- [X] T030 Refresh generated maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, then inspect `git diff --stat docs/`. On Windows, `-- check` always reports stale because of CRLF, so judge freshness from the diff. Stage only the maps this unit actually changed, by explicit path, including `docs/maps/manifest.json`.
- [ ] T031 Set `specs/237-graph-boundary-completion/spec.md` `**Status**:` to `In progress` when implementation starts. Set it to `Implemented` in the PR that merges, per `docs/reference/spec-lifecycle.md`.
- [X] T032 Run the regression e2e scripts `e2e-tests/reusable-activities/Test-ReusableActivityOutcomeLimit.ps1`, `Test-ReusableActivity.ps1`, `Test-ReusableActivityDeep.ps1` and `Test-ReusableActivityComplete.ps1` against a server started from source, and record the results as evidence on the PR (SC-003, SC-005).
- [ ] T033 Re-check for competing work on #2750 before committing. Open the PR (draft is fine) when pushing, comment the PR link on #2750, and comment on #2706 that spec 237 defines the construct, linking `contracts/complete-activity.md`.

---

## Dependencies and execution order

- **Phase 1 → Phase 2**: T001 before anything else. In Phase 2, T002 → T003 → T004 (gate) → T005 → T006 → T007.
- **User Story 1 (Phase 3)** depends on Phase 2. T008 and T009 run in parallel; T010 needs both; T011 needs T010; T012 needs T009–T010.
- **User Story 2 (Phase 4)** depends on User Story 1 (T010, T012). T014 → T015 → T016.
- **User Story 3 (Phase 5)** depends on Phase 2 only for T023. T017 → T018 → T019 → T020. T021 and T022 can run in parallel with T017–T020.
- **User Story 4 (Phase 6)** runs after User Stories 1–3, because it asserts the unchanged surface over everything they touched.
- **Polish (Phase 7)**: T027–T029 run in parallel after User Story 3. T030 comes after all code and doc changes. T031 is at the start and at merge. T032 and T033 come last.

## Parallel examples

```text
# Phase 3, once Phase 2 is green:
T008 GraphActivityDescriptor.CompleteOutcomes   ||   T009 Complete.cs

# Phase 5:
T017→T020 provider work   ||   T021→T022 draft validator

# Phase 7:
T027 e2e script   ||   T028 reference docs   ||   T029 EXTENSION_POINTS
```

## Implementation strategy

1. **MVP**: Phases 1–3. After the risk gate, a hand-pinned descriptor proves that the exit reaches the nearest
   boundary through every composite.
2. **Durability**: Phase 4 proves SC-002 on both stores before any design-side surface ships.
3. **Authoring**: Phase 5 makes `Complete` publishable and safe. Spec 236 can start emitting it from here.
4. **Hardening and evidence**: Phases 6–7 cover regression, catalog, docs, maps and e2e, then the PR.
