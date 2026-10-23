# Feature Specification: Publish Event resumes a waiting Event without faulting

**Feature Branch**: `agent/claude/3107-publish-event-resume`

**Created**: 2026-10-20

**Status**: Implemented — via [#3115](https://github.com/elsa-workflows/elsa-foundation/pull/3115)

**Issue**: [#3107](https://github.com/elsa-workflows/elsa-foundation/issues/3107)

**Input**: A workflow waiting on a mid-flow Event is resumed by Publish Event with the same name. The wait
faults with `ActivityResumeFaulted` instead of completing: "trigger resolved to null" when Publish Event carries no
payload, and a contract mismatch when it carries any other payload. The event payload is delivered as the Event's
trigger, and the Event only accepts the shape `{ "EventName": ... }`. Raising the same event through the stimulus
API with `{ "eventName": ... }` as input resumes the wait correctly.

**Depends on**:
- Spec 246 (#2796, PR #3031, open, merged into this branch): publishes imported Elsa 3 reusable activities as graph
  activities, proves that Event waits resume inside a graph body, and adds the Elsa 3 migration readiness guide
  that names #3107 as a known issue.
- Spec 236 (merged): the Elsa 3 activity map, which marks the Elsa 3 Event `Result` member Lost today.

**Owner decision (2026-10-20)**: @j03y-nxxbz chose to fix this in Event: it accepts any delivered payload as its
trigger, and the runtime's stimulus routing stays unchanged. The owner also chose to expose the delivered payload as an
Event output in this spec. This amends the spec 236 member map: the Elsa 3 Event `Result` member is carried instead
of Lost.

## User Scenarios & Testing *(mandatory)*

The actors are workflow authors who pair Publish Event with a waiting Event, and operators who import Elsa 3
workflows that use this pair.

### User Story 1 - Publish Event without a payload resumes the waiting Event (Priority: P1)

A workflow waits on Event `order.approved`. Another workflow, or a later step in the same one, runs Publish Event
`order.approved` with no payload. The waiting workflow continues past the Event and completes. Elsa 3 workflows
usually pair the two this way, so every imported workflow of this shape depends on this story.

**Why this priority**: Today, every workflow of this shape faults at the wait and raises an incident. This is the
issue as reported.

**Independent Test**: Run a waiting workflow and a publishing workflow with no payload. The waiting workflow ends
Completed, and there is no incident or `ActivityResumeFaulted`.

**Acceptance Scenarios**:

1. **Given** a regular workflow suspended on a mid-flow Event `order.approved`, **When** another workflow runs
   Publish Event `order.approved` with no payload, **Then** the waiting workflow resumes past the Event and
   completes without an incident.
2. **Given** a workflow whose graph activity body is suspended on a mid-flow Event, **When** Publish Event with the
   same name and no payload runs, **Then** the body resumes, the graph activity completes and the outer workflow
   continues.
3. **Given** a workflow suspended on an Event, **When** the same workflow runs Publish Event with that name as a
   local event, **Then** the wait resumes and completes.

---

### User Story 2 - Publish Event with a payload resumes the wait and hands the payload on (Priority: P1)

Publish Event carries a payload, for example `{ "orderId": "1" }`. The waiting Event resumes, and the next steps can
read that payload from the Event's output, as they could from the Event's `Result` in Elsa 3.

**Why this priority**: A payload of any shape faults today. Without the output, a payload sent to a waiting
workflow is silently dropped.

**Independent Test**: Publish an object payload, a scalar payload and an array payload to waiting Events. Each wait
completes, and a step after the Event reads back the exact payload.

**Acceptance Scenarios**:

1. **Given** a regular workflow suspended on Event `order.approved`, **When** Publish Event `order.approved` runs
   with payload `{ "orderId": "1" }`, **Then** the wait completes and the Event's payload output equals
   `{ "orderId": "1" }`.
2. **Given** a graph activity body suspended on an Event, **When** Publish Event runs with a payload, **Then** the
   body resumes, completes, and its Event's payload output equals the published payload.
3. **Given** an Event resumed with no payload, **When** a later step reads the payload output, **Then** it reads
   an empty (null) value, not an error.
4. **Given** an Event resumed with any JSON payload (object, array, string, number, boolean), **When** the wait
   resumes, **Then** it completes and the payload output holds that value unchanged.

---

### User Story 3 - Existing ways of raising an event keep working (Priority: P2)

Authors and tools already raise events through the stimulus API, through the Studio test-run "Deliver event" action,
by starting workflows from an Event start trigger, and through BPMN catch events (intermediate, boundary,
event-based gateway, event subprocess). All of these keep working, and the waits that are already parked when this
change is deployed still resume.

**Why this priority**: The fix must not swap one broken path for another.

**Independent Test**: The existing Event, BPMN catch-event, test-run and start-trigger tests pass unchanged, and a
stimulus API resume with `{ "eventName": ... }` input still completes the wait.

**Acceptance Scenarios**:

1. **Given** a workflow waiting on an Event, **When** the event is raised through the stimulus API with input
   `{ "eventName": "order.approved" }`, **Then** the wait completes as before, and the payload output holds that
   input.
2. **Given** a published workflow whose start trigger is Event `order.approved`, **When** Publish Event
   `order.approved` runs with or without a payload, **Then** a new instance starts and its Event completes.
   When there is a payload, the started Event's payload output holds it.
3. **Given** a BPMN process waiting on a message catch event, **When** the matching event is delivered, **Then**
   the process behaves exactly as before this change.
4. **Given** a waiting Event that was parked before this change was deployed, **When** the matching event arrives,
   **Then** it resumes normally.
5. **Given** a test run waiting on an Event, **When** the author uses "Deliver event" with or without a payload,
   **Then** the wait completes, and the payload output holds the delivered payload.

---

### User Story 4 - Imported Elsa 3 workflows keep the Event result (Priority: P2)

An operator imports an Elsa 3 workflow in which a step reads the Event's `Result` (the event payload). The import
carries that reference to the Elsa 4 Event payload output instead of reporting the member as Lost, and the readiness
guide no longer lists #3107 as a known issue.

**Why this priority**: This closes the remaining Elsa 3 parity gap on Event once the payload reaches the wait.

**Independent Test**: Convert an Elsa 3 definition whose Event `Result` is bound to a variable. The converted
definition binds the Elsa 4 payload output to that variable, with no Lost diagnostic for `Result`.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 Event whose `Result` output is captured, **When** the definition is converted, **Then** the
   Elsa 4 Event's payload output is captured into the same target, with no Lost diagnostic.
2. **Given** the Elsa 3 migration readiness guide, **When** a reader looks up Publish Event and Event, **Then** the
   guide says that the pair works, with or without a payload, and no longer names #3107 as a known issue or the
   stimulus API as a workaround.

### Edge Cases

- A payload that is JSON `null` is treated the same as no payload.
- A payload that happens to look like the old trigger shape, `{ "EventName": "x" }`, is still the payload: the
  output holds that object, and the event name output stays the waited name.
- The Event's event-name output always reports the name the Event waited on, whatever the payload holds.
- A correlated Publish Event still resumes only the waits with the same correlation value. A local Publish Event
  still resumes only waits in the publishing execution. Payload handling does not change either rule.
- Large payloads follow the same size and protection rules as any other value the runtime keeps for an instance.
- One Publish Event that both starts a new instance and resumes a parked wait delivers the same payload to both.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A mid-flow Event wait MUST resume and complete when the matching event arrives with no payload or with
  a JSON payload of any shape, whatever path raised it: Publish Event (broadcast or local), the stimulus API or a
  test-run delivery.
- **FR-002**: The Event MUST expose the delivered payload as an output that later steps can read. It holds the
  payload unchanged, or null when there was none.
- **FR-003**: The Event's existing event-name output MUST keep its key and meaning: the name the Event waited on, or
  was started by.
- **FR-004**: When an Event starts a workflow, the start delivery's payload MUST appear on the same payload output.
  A start delivery without a payload MUST still complete the Event.
- **FR-005**: Waits that were parked before the change MUST still resume after it, so the registered trigger type
  stays the same.
- **FR-006**: The stimulus routing contract, the resume path and Publish Event's delivery MUST stay as they are. The
  fix lives in the Event activity.
- **FR-007**: BPMN catch events that use Event (message intermediate catch, boundary, event-based gateway, event
  subprocess start) MUST behave as before.
- **FR-008**: The Elsa 3 activity map MUST carry the Elsa 3 Event `Result` member to the Elsa 4 Event payload output
  instead of marking it Lost. The spec 236 member map MUST record the change.
- **FR-009**: The Elsa 3 migration readiness guide MUST stop naming #3107 as a known issue and MUST describe the
  Publish Event → Event pair as supported, with or without a payload.
- **FR-010**: Automated tests MUST cover Publish Event → Event resume with and without a payload, both in a regular
  workflow and inside a graph activity body, and MUST assert the payload output.

### Key Entities

- **Event trigger delivery**: the value that arrives with a matching event: no payload, or any JSON value. After this
  change it is the event's payload, not a description of the event.
- **Event result**: what a completed Event hands to later steps: the event name (existing) and the delivered
  payload (new).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Publish Event → Event resumes in the new tests complete with no incident: with and without a
  payload, in regular workflows and in graph activity bodies.
- **SC-002**: Every payload shape in the tests (no payload, null, object, array, string, number, boolean) reaches a
  later step unchanged.
- **SC-003**: Every existing Event, Publish Event, BPMN catch-event, test-run and graph-boundary test passes with no
  change to its assertions.
- **SC-004**: An imported Elsa 3 workflow that reads the Event `Result` converts with zero Lost diagnostics for
  that member.
- **SC-005**: The readiness guide has no remaining reference to #3107 as an open known issue.

## Assumptions

- The stimulus API keeps delivering its `input` as the event payload. Callers that sent `{ "eventName": ... }` to
  satisfy the old trigger shape keep working, and that object now also appears as the payload output. No caller
  depends on the payload output being empty in that case, because the output is new.
- A waiting Event does not need the event name from the trigger: the name it waited on is already in its own state.
- The payload output is a plain value, with no schema or type checks. Typed payload contracts are out of scope.
- Studio shows the new output the same way it shows other activity outputs; no Studio-specific work is in scope.
- Spec 246 (PR #3031) merges before or together with this work. This branch already contains its changes, so the
  graph-body tests and the readiness guide edit build on it.
