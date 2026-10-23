# Feature Specification: Workflow Test Drive

**Feature Branch**: `agent/codex/2862-workflow-test-drive`
**Created**: 2026-10-16
**Status**: In progress — Sipke approved implementation of the reviewed design in the demo session; Foundation #2862.
**Input**: Test an unpublished designer draft with workflow inputs and module-owned trigger stimuli, including real HTTP requests. Third parties must extend this without modifying Elsa source. Record a narrated demo after validation.

## User Scenarios & Testing

### User Story 1 - Drive a selected draft start (P1)
An author selects one valid start trigger and supplies workflow inputs, then inspects the resulting execution without publishing.
**Independent Test**: A multi-trigger draft starts through only the selected branch; inputs and event data remain distinct.
**Acceptance Scenarios**:
1. Given a draft with declared inputs, when required values are absent or invalid, then no execution starts and field diagnostics explain the refusal.
2. Given HTTP and Event starts, when one is selected, then only that entry runs with the captured configuration.
3. Given a changed draft or unsupported trigger, when stale configuration is submitted, then the system refuses it rather than silently selecting a different entry.

### User Story 2 - Send a real HTTP request (P1)
An author configures the endpoint request and sees the actual HTTP response separately from the workflow outcome.
**Independent Test**: Prepare an unpublished HTTP draft, send a real request, and inspect its response and test execution.
**Acceptance Scenarios**:
1. Preparation starts no execution and creates no production binding; sending a valid request starts one test root.
2. Wrong method, denied authorization, excessive body or expired target starts no root.
3. A matching published endpoint and another test run remain unaffected.

### User Story 3 - Extend Test Drive (P1)
A third-party module supplies its trigger's controls and delivery mechanism through public registrations.
**Independent Test**: Register a custom driver and editor in tests without changing built-in code; discover, configure and run it.
**Acceptance Scenarios**:
1. Built-in and third-party drivers use the same contracts.
2. Missing or ambiguous drivers show an unavailable reason rather than an unsafe generic injection.

### User Story 4 - Exercise schedules and waits (P2)
An author fires Timer/Cron once immediately, previews upcoming times, and can supply a supported event to a specific active wait.
**Independent Test**: Fire a schedule start once without registering recurrence; resume exactly one active named-event wait.
**Acceptance Scenarios**:
1. Fire once now preserves the authored interval/expression; schedule preview shows UTC using the normal calculator.
2. Follow-up delivery targets a selected active wait with retry safety; stopped/expired runs refuse further firing.
3. Editing setup or the draft preserves the captured result, identified as an earlier draft where applicable.

### Edge Cases
Invalid expression/trigger placement, multiple methods on one node, duplicate binding identity, unknown input, false/zero/null/default inputs, two same-name waits, consumed wait, duplicate firing, simultaneous stop, unavailable modules, process restart, deferred execution, HTTP response without workflow completion.

## Requirements
- **FR-001**: Capture one immutable draft for discovery and start; validate inputs against its compiled declaration.
- **FR-002**: Bind start selection to exact artifact, provider, node, descriptor and metadata; use ordinary composite semantics.
- **FR-003**: Offer public backend driver and Studio editor registrations. Modules own modes, validation, preparation, stimulus delivery, result details and teardown.
- **FR-004**: HTTP sends a real request to a temporary isolated endpoint, applies authored policy/limits and returns actual response evidence.
- **FR-005**: Named Event and Timer/Cron immediate modes target only the chosen test entry. Schedules remain unchanged.
- **FR-006**: Scope resources to existing test authority and expiry; stop prevents new firing and requests ordinary cancellation.
- **FR-007**: Exclude isolated test waits from global ingress; continuation uses exact active-wait identity and retry receipts.
- **FR-008**: Keep workflow inputs and module firing configuration separate; preserve captured results across edits/reruns.
- **FR-009**: Retain direct Test Run semantics and supported deployment shapes.

### Key Entities
Test Run (captured draft, authority, expiry, resulting execution), start option (artifact-bound entry), driver/mode (module-owned behavior and input contract), prepared target (expiring projection), delivery receipt (retry outcome for exact wait), HTTP exchange (actual status/headers/body).

## Success Criteria
- **SC-001**: An unpublished draft can demonstrate valid input, Event start and actual HTTP exchange from Studio without production publication.
- **SC-002**: A custom module registers and exercises Test Drive without edits to Elsa's built-in driver or designer code.
- **SC-003**: Isolation tests show no effect on matching production starts or unrelated waits.
- **SC-004**: Invalid/stale/expired configurations produce no new execution; accepted dispatch is distinguished from completion.

## Assumptions
Initial scheduling includes Fire once now and calculation preview. Wait for next occurrence and virtual time are later explicit modes. Test runs use the connected environment and real outgoing activity side effects. Existing default retention is 30 minutes; process-local prepared targets fail closed after restart. No new scenario library, broker emulator or production deployment test is introduced. Delivery is one feature unit under none/free-flow, not a new program.
