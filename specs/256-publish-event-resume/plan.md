# Implementation Plan: Publish Event resumes a waiting Event without faulting

**Branch**: `agent/claude/3107-publish-event-resume` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/256-publish-event-resume/spec.md`

## Summary

A mid-flow Event wait faults when Publish Event resumes it. The reason: the published payload is routed as the stimulus
input, the resume path stores that input as the Event's trigger, and the trigger type `EventReceived` only accepts
an object, never `null`. The fix stays inside Event (owner decision, 2026-10-20). `EventReceived` becomes "the
delivered payload". A converter on the type accepts any JSON token, `null` included, without changing the type's
canonical alias. `EventResult` gains an optional `Payload` output holding that value. The Elsa 3 map then carries
Elsa 3's Event `Result` to `Payload` instead of marking it Lost, and the readiness guide drops #3107 as a known
issue. Routing, the resume handler and Publish Event do not change.

## Technical Context

**Language/Version**: C# on .NET 10 (repo default)

**Primary Dependencies**: System.Text.Json (converter); the existing activity runtime (`StatefulTriggerActivity`)

**Storage**: N/A. Trigger and result values persist through the existing value envelopes, with no schema change.

**Testing**: xunit only (constitutionally pinned, no FluentAssertions). Build and test only the touched projects.

**Target Platform**: Elsa runtime hosts (server)

**Project Type**: Library (Essentials activity package) plus the Elsa 3 import extension

**Performance Goals**: N/A. The converter is constant work per delivery.

**Constraints**: Waits parked before the change must still resume, so the trigger type's alias stays the same
(FR-005). The runtime contracts (`StimulusDispatchRequest`, the resume-bookmark command) stay as they are (FR-006).

**Scale/Scope**: One activity file, one map entry, one member-map row and one readiness note, plus tests. About 40
test arrange lines change mechanically (research R-3).

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Elsa 3 knowledge changes only in `Extensions/Elsa3` (map entry, fixtures, tests). Essentials learns nothing about Elsa 3 |
| §E2.2 Runtime takes no Design reference | PASS | No project reference changes |
| §E5 versioning / public API | PASS (pre-GA) | `EventReceived` swaps `EventName` for `Payload`, and `EventResult` gains a constructor parameter and an output. Both are public types in `Elsa.Activities.Primitives`. ADR 0080 allows pre-GA breaking cleanup. The persisted alias is unchanged, so stored data stays compatible |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | The converter is about 15 lines, private to `Event.cs` |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: a typed trigger record, an `[Output]` on the result record (precedent `HttpEndpointResult.ParsedContent`), a `[JsonConverter]` on a value type and an R1 `CarryOutput` map rule |
| §2.22 / §2.22.1 docs and extension points | ACTION | Readiness guide (#3107 note), spec 236 member map, and the Elsa 3 mapping README if it lists Event `Result` as Lost. No extension point changes, so `EXTENSION_POINTS.md` is unchanged. The generated maps are regenerated, because the #3031 merge left them stale on this branch |
| §2.23.1–§2.23.3 tests and visibility | ACTION | Converter unit tests, resume-request tests, router-level Publish Event → Event tests and end-to-end import-host tests (regular and graph body). The converter is `internal`/`private` |
| §2.23.5 exception boundaries | PASS | Removes two throws on valid input and adds none |
| Decision record | ACTION | Spec 236 `member-map.md`, Event.Result row, amended with the owner's 2026-10-20 decision |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | `EventReceived` is the delivered payload | `sealed record EventReceived(JsonElement? Payload = null)` with `[JsonConverter(typeof(EventReceivedJsonConverter))]`. Read: any token goes to `Payload` (null token → `null`, `HandleNull = true`). Write: `Payload` raw, or `null`. Same CLR name and namespace, so the alias is unchanged (R-2, R-3) |
| D-2 | `EventResult` gains `Payload` | `[Output(Key = "Payload", Path = "payload", IsRequired = false)] JsonElement? Payload`. `Result`/`eventName` is unchanged (R-4) |
| D-3 | Event fills `Payload` on both paths | Resume uses `new EventResult(context.State.EventName, context.Trigger.Payload)`. Start uses `context.TryGetTrigger(out var t) ? t.Payload : null`. A direct invocation gets null |
| D-4 | Elsa 3 `Result` is carried | `Lost("Result")` becomes `CarryOutput("Result", "Payload")`. The member-map row becomes R1 (R-5) |
| D-5 | Test arrange lines send a plain payload | `new EventReceived("x")` as resume input becomes `new { eventName = "x" }`, the stimulus API's shape. Assertions are untouched (R-3) |
| D-6 | The graph-body test stops using the stimulus-API workaround | `ReusableBodyWaitTests`' Event test raises the event with an imported Publish Event workflow instead of `POST runtime/workflows/stimuli`, with and without a payload |

## Project Structure

### Documentation (this feature)

```text
specs/256-publish-event-resume/
├── spec.md
├── plan.md              # this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── event-activity.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
Essentials/src/Activities/Primitives/Activities/Event.cs           # D-1, D-2, D-3 (EventReceived, converter, EventResult, Event)
Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs   # D-4

Essentials/tests/Activities/Runtime/Tests/
├── EventTriggerStimulusProviderTests.cs      # existing; construct EventReceived(payload), assert Payload output
├── EventReceivedTests.cs                     # new: converter read/write for every token kind, exact round-trip, request-level ResumeAsync with null and scalar payloads, start with payload
├── PublishEventResumeRoutingTests.cs         # new: PublishStimulusExecutor → real StimulusRouter → waiting Event, payload none/object/scalar, broadcast and local
├── EventCorrelationRoutingTests.cs           # existing; D-5 arrange lines
└── UndeclaredPayloadTypeResumeTests.cs       # existing; D-5 arrange lines
Essentials/tests/Activities/Behavioral/Tests/
├── Drives/PrimitiveDrives.cs                             # D-5
└── GraphBoundaryCompleteDurableCancellationTests.cs      # D-5
Essentials/tests/Activities/Bpmn/Tests/*.cs               # D-5 only (about 30 sites in 10 files)

Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/
├── PublishEventResumeTests.cs                # new: regular imported workflows; waiter + publisher, with and without payload; asserts Completed, no incident, Payload output
├── ReusableBodyWaitTests.cs                  # D-6: graph-body Event resumed by Publish Event, with and without payload
└── Fixtures/
    ├── publish-event-waiter.json             # new: Elsa 3 flowchart waiting on Event "order.approved", captures Result
    ├── publish-event-publisher.json          # new: Elsa 3 PublishEvent "order.approved", no payload
    ├── publish-event-publisher-payload.json  # new: same, with payload {"orderId":"1"}
    └── reusable-waits-event.json             # existing (unchanged unless the publisher is a sibling definition)
Extensions/Elsa3/tests/Mapping/Tests/                     # update the Event Result expectation (Lost → carried) and fixtures that pin it

docs/reference/elsa3-migration-readiness.md               # FR-009
specs/236-elsa3-activity-map/member-map.md                # FR-008
docs/maps/*                                               # regenerate (stale after the #3031 merge)
```

**Structure Decision**: No new projects. The router-level tests in `Elsa.Activities.Runtime.Tests` prove the exact
faulting path (executor → router → resume) without a host. The `Elsa3ImportHost` tests prove it end to end with the
real outbox, in regular workflows and in a graph activity body. No existing test delivers a Publish Event through the
outbox to a waiting Event, so both layers are new.

## Complexity Tracking

No constitution violations to justify.
