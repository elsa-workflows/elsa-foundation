# Tasks: Publish Event resumes a waiting Event without faulting

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/event-activity.md](contracts/event-activity.md), [quickstart.md](quickstart.md)

**Tests**: required. The change alters logic-bearing trigger handling and a public activity contract (framework §2.23).
The new routing and end-to-end tests are written first and run on the unchanged code to confirm the #3107 fault. This
is not a TDD cadence declaration. xunit only. Build and test only the touched projects.

## Phase 1: Setup

- [ ] T001 Claim #3107 per Issue Bus Lite (`claim: claude starting`, after asking the owner), on branch `agent/claude/3107-publish-event-resume`, which already contains PR #3031's branch merged in
- [ ] T002 Build `Essentials/src/Activities/Primitives` and `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests` on the branch as-is, to confirm the #3031 merge compiles

## Phase 2: Foundational (blocks every story)

**Goal**: make `EventReceived` the delivered payload with no change to its alias. The record's constructor changes,
so every compile site moves with it.

- [ ] T003 Add `Essentials/tests/Activities/Runtime/Tests/PublishEventResumeRoutingTests.cs`. It drives a `PublishStimulusIntent` through `new PublishStimulusExecutor(router)` into the real `StimulusRouter` against a parked mid-flow Event wait (reuse the setup in `EventCorrelationRoutingTests.cs`), with payloads none, `{"orderId":"1"}` and `"text"`, broadcast and local. Run it on the unchanged code and record the failures (null trigger / contract mismatch) in research.md R-1
- [ ] T004 In `Essentials/src/Activities/Primitives/Activities/Event.cs`, change `EventReceived` to `sealed record EventReceived(JsonElement? Payload = null)` with `[JsonConverter(typeof(EventReceivedJsonConverter))]`. Add a private nested or file-scoped `EventReceivedJsonConverter`: `HandleNull => true`; Read clones any token into `Payload` (null token → `null`); Write emits `Payload` raw, or `null`. Keep the type name and namespace, which hold the alias (D-1)
- [ ] T005 Update the XML docs on `EventReceived` and `Event` in `Essentials/src/Activities/Primitives/Activities/Event.cs`: the trigger is the event payload, and the event name comes from the wait state or the input
- [ ] T006 [P] Replace `JsonSerializer.SerializeToElement(new EventReceived("x"))` with `JsonSerializer.SerializeToElement(new { eventName = "x" })` in `Essentials/tests/Activities/Bpmn/Tests/*.cs`, without touching any assertion (D-5)
- [ ] T007 [P] Same replacement in `Essentials/tests/Activities/Behavioral/Tests/Drives/PrimitiveDrives.cs` and `Essentials/tests/Activities/Behavioral/Tests/GraphBoundaryCompleteDurableCancellationTests.cs`
- [ ] T008 [P] Same replacement in `Essentials/tests/Activities/Runtime/Tests/EventCorrelationRoutingTests.cs` and `Essentials/tests/Activities/Runtime/Tests/UndeclaredPayloadTypeResumeTests.cs`; and in `Essentials/tests/Activities/Runtime/Tests/EventTriggerStimulusProviderTests.cs`, construct `new EventReceived()` where the test built `new EventReceived("order-shipped")`
- [ ] T009 Add `Essentials/tests/Activities/Runtime/Tests/EventReceivedTests.cs`: the converter reads null, object, array, string, number and boolean into `Payload`; the round-trip through serialize and deserialize is exact; `PersistableActivityValue`-style snapshot (`JsonSerializer.SerializeToElement` + `Deserialize<EventReceived>`) of `new EventReceived()` gives `null` and back; the canonical alias of `typeof(EventReceived)` is unchanged (pin the string)

**Checkpoint**: `Elsa.Activities.Runtime.Tests`, `Elsa.Activities.Bpmn.Tests` and `Elsa.Activities.Behavioral.Tests` build, and their existing tests pass.

## Phase 3: User Story 1 - Publish Event without a payload resumes the waiting Event (P1) 🎯 MVP

**Independent test**: a regular workflow and a graph activity body, each waiting on Event `order.approved`, complete
after Publish Event `order.approved` with no payload, with no incident.

- [ ] T010 [US1] In `Essentials/tests/Activities/Runtime/Tests/EventTriggerStimulusProviderTests.cs`, add a request-level resume test through `IStatefulActivity.ResumeAsync(ActivityResumeRequest)` with a JSON `null` trigger payload (the path that threw "trigger resolved to null")
- [ ] T011 [P] [US1] Add Elsa 3 fixtures `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/publish-event-waiter.json` (flowchart: mid-flow Event `order.approved`, `canStartWorkflow` false, then Complete) and `publish-event-publisher.json` (PublishEvent `order.approved`, no payload), shaped like `reusable-waits-event.json`
- [ ] T012 [US1] Add `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/PublishEventResumeTests.cs`. It imports both, publishes and runs the waiter until its Event is Suspended, runs the publisher, then `WaitForSettledAsync`. It asserts the waiter is Completed, the Event is Completed and there is no incident. T003 has already recorded the baseline fault, so this test does not need its own run on unchanged code
- [ ] T013 [US1] In `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/ReusableBodyWaitTests.cs`, replace the stimulus-API workaround in the Event test with an imported Publish Event workflow (no payload), and drop the #3107 comment (D-6)
- [ ] T014 [US1] Run T003's no-payload cases and T010–T013: all pass

## Phase 4: User Story 2 - Publish Event with a payload resumes the wait and hands the payload on (P1)

**Independent test**: Publish Event with `{"orderId":"1"}` resumes the waiting Event in a regular workflow and in a
graph body. The Event's `Payload` output equals the payload.

- [ ] T015 [US2] In `Essentials/src/Activities/Primitives/Activities/Event.cs`, give `EventResult` a `[Output(Key = "Payload", Path = "payload", IsRequired = false)] JsonElement? Payload` with a constructor `EventResult(string eventName, JsonElement? payload = null)` (D-2)
- [ ] T016 [US2] In `Event.ExecuteAsync`, complete with `new EventResult(EventName, context.TryGetTrigger(out var trigger) ? trigger.Payload : null)`. In `Event.ResumeAsync`, complete with `new EventResult(context.State.EventName, context.Trigger.Payload)` (D-3)
- [ ] T017 [US2] In `Essentials/tests/Activities/Runtime/Tests/EventTriggerStimulusProviderTests.cs`, assert `Payload` on resume (object and scalar payloads, null) and on a targeted start with a payload, and assert a direct invocation gives `Payload == null`
- [ ] T018 [US2] Extend `PublishEventResumeRoutingTests.cs` (T003): after the routed resume, assert the Event's completion result carries the published payload unchanged, for each payload kind
- [ ] T019 [P] [US2] Add fixture `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/publish-event-publisher-payload.json` (PublishEvent with payload `{"orderId":"1"}` as a JSON literal)
- [ ] T020 [US2] In `PublishEventResumeTests.cs`, add the with-payload case: the waiter completes, and the Event's `Completion.Result.InlineValue` has `payload` equal to `{"orderId":"1"}` (the no-payload case asserts `payload` is null)
- [ ] T021 [US2] In `ReusableBodyWaitTests.cs`, add the graph-body with-payload case: the body completes, and the inner Event's `payload` equals the published value

## Phase 5: User Story 3 - Existing ways of raising an event keep working (P2)

**Independent test**: the existing Event, BPMN, test-run, start-trigger and graph-boundary tests pass with unchanged
assertions. A stimulus-API resume with `{eventName}` input still completes.

- [ ] T022 [P] [US3] In `Essentials/tests/Activities/Runtime/Tests/EventTestDriveTests.cs`, add a check that `EventTestRunWaitDriver.PrepareAsync` with no payload and with a scalar payload returns input that deserializes into `EventReceived` without throwing
- [ ] T023 [P] [US3] Add a stimulus-API case to `PublishEventResumeTests.cs`: `POST runtime/workflows/stimuli` with input `{"eventName":"order.approved"}` still resumes, and `payload` holds that object
- [ ] T024 [US3] Add a start-trigger case to `PublishEventResumeRoutingTests.cs`: one routed delivery with a payload both starts a published Event-start workflow and resumes a parked wait, and both Events carry the same payload (spec edge case)
- [ ] T025 [US3] Run the full `Elsa.Activities.Runtime.Tests`, `Elsa.Activities.Bpmn.Tests` and `Elsa.Activities.Behavioral.Tests` projects: all green, no assertion edits beyond T006–T008's arrange lines

## Phase 6: User Story 4 - Imported Elsa 3 workflows keep the Event result (P2)

**Independent test**: an Elsa 3 Event whose `Result` is captured converts with the Elsa 4 `Payload` output captured
into the same target, with no Lost diagnostic.

- [ ] T026 [US4] In `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs`, replace `Lost("Result")` with `CarryOutput("Result", "Payload")` on the Event entry (D-4)
- [ ] T027 [US4] Update `Extensions/Elsa3/tests/Mapping/Tests` expectations that pin Event `Result` as Lost (search `Event` + `Lost`/`ELS3` output diagnostics and `Fixtures/elsa3-activity-surface.json` coverage), and add a conversion test where a captured Event `Result` maps to `Payload`
- [ ] T028 [US4] In `publish-event-waiter.json`, capture the Event `Result` into a variable, and assert in `PublishEventResumeTests.cs` that the variable holds the published payload after resume
- [ ] T029 [P] [US4] Amend the `Event.Result (payload)` row in `specs/236-elsa3-activity-map/member-map.md` to R1 → `Payload`, citing spec 256 and the owner's 2026-10-20 decision. Update the Elsa 3 mapping README if it lists Event `Result` as Lost
- [ ] T030 [P] [US4] In `docs/reference/elsa3-migration-readiness.md`, remove the #3107 known-issue note, and state that Publish Event → Event works with or without a payload, with the payload on the Event's `Payload` output (FR-009)
- [ ] T031 [US4] Run `Elsa3.Mapping.Tests` and `Elsa3.Activities.Design.Import.Integration.Tests`: all green

## Phase 7: Polish & cross-cutting

- [ ] T032 Regenerate maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`. Review `git diff --stat docs/` (CRLF caveat on Windows), and stage the changed maps by explicit path, including `docs/maps/manifest.json`
- [ ] T033 Check `EXTENSION_POINTS.md` and the activity docs (e.g. `docs/` pages describing Event outputs) for Event's output list, and update any that list only `Result`
- [ ] T034 Set spec 256 `Status: Implemented` in `specs/256-publish-event-resume/spec.md` in the finishing PR, before merge
- [ ] T035 Commit. Ask the owner before pushing and opening a draft PR against `main` that notes the stack on #3031

## Dependencies & execution order

- Phase 1 → Phase 2 (T004 changes the record's constructor, so T006–T008 must land with it to compile).
- US1 (Phase 3) and US2 (Phase 4) both depend on Phase 2. US2's T015–T016 also feed US1's tests, which then gain
  payload assertions. Run US1 before US2 in one sequence.
- US3 depends on US2 (it asserts `Payload` on the stimulus-API path).
- US4's map change (T026) depends on US2 (the `Payload` output must exist). Docs T029/T030 can go in parallel with
  T026–T028.
- Polish comes last.

## Parallel opportunities

- T006, T007 and T008: different test projects, mechanical.
- T011 and T019: fixture files.
- T022 and T023: different projects.
- T029 and T030: docs only.

## Implementation strategy

MVP is Phases 2–3: the fault is gone for the common no-payload pair, in regular workflows and graph bodies. Phase 4
adds the payload output, Phase 5 locks in the existing paths, Phase 6 closes the Elsa 3 parity row and the readiness
note, and Phase 7 finishes the PR. All of it ships in one PR, because the spec's done criteria span every story.
