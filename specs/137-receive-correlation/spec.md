# Feature Specification: Receive Event Correlation

**Feature Branch**: `codex/1001-receive-correlation`
**Created**: 2026-07-23
**Status**: Draft
**Input**: User description: "Deliver issue #1001 as an authored opt-in `Event.CorrelationId` receive-side correlation fix. Preserve null/blank broadcast behavior; correlation narrows resumes only; start fan-out and BPMN correlation authoring are out of scope."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resume the Correct Correlated Event Wait (Priority: P1)

As a workflow author, I can assign a correlation value to an Event wait so that a same-named event resumes only the waiting workflow instances carrying that value.

**Why this priority**: It prevents concurrent business conversations that use the same event name from resuming each other accidentally.

**Independent Test**: Create waiting Event instances with the same event name and different correlation values, then deliver an event with one value and verify that only the matching instances resume.

**Acceptance Scenarios**:

1. **Given** two waiting Event instances have the same event name and distinct nonblank correlation values, **When** an event with the first correlation value is delivered, **Then** every waiting instance with the first value resumes and no instance with the other value resumes.
2. **Given** a waiting Event instance has a nonblank correlation value, **When** a same-named event with a different nonblank correlation value is delivered, **Then** the waiting instance remains waiting.
3. **Given** a waiting Event instance has a nonblank correlation value, **When** a same-named event is delivered without a correlation value, **Then** the existing unscoped broadcast behavior remains available.

---

### User Story 2 - Preserve Unscoped Event Delivery (Priority: P2)

As a workflow author who does not use correlation, I can continue to use named Event waits without changing existing workflow behavior.

**Why this priority**: Existing named-event workflows must remain compatible and continue to support broadcast delivery.

**Independent Test**: Create same-named Event waits without a correlation value, deliver an unscoped event, and verify that all eligible waits resume.

**Acceptance Scenarios**:

1. **Given** multiple waiting Event instances have the same name and no correlation value, **When** a same-named event without a correlation value is delivered, **Then** every eligible waiting instance resumes.
2. **Given** an Event wait is authored with a null, empty, or whitespace-only correlation value, **When** it is registered, **Then** it behaves as unscoped and does not acquire a correlation restriction.
3. **Given** a pre-existing wait has no correlation value, **When** a same-named event with a nonblank correlation value is delivered, **Then** that wait does not resume.

---

### User Story 3 - Keep Correlation Scope Limited to Resumes (Priority: P3)

As a workflow operator, I can rely on this change to narrow existing waits without changing which new workflows are started for an event.

**Why this priority**: It fixes the receive-side defect without silently changing established start behavior.

**Independent Test**: Compare the set of workflows started by a named event before and after introducing a correlation value; the start set remains governed by existing start rules.

**Acceptance Scenarios**:

1. **Given** a named event is eligible to start workflows, **When** it is delivered with a correlation value, **Then** the feature changes only the selection of already-waiting Event instances and does not add, remove, or otherwise narrow start candidates.
2. **Given** a published Event start binding carries a nonblank authored correlation scope that differs from the delivered correlation value, **When** the named event is delivered, **Then** the existing start fan-out still includes that binding while resume selection is correlation-narrowed.

### Edge Cases

- Correlation values that are null, empty, or whitespace-only are treated as absent so they cannot create an unmatchable wait.
- Authored Event wait values are trimmed before registration. Delivery values retain their existing producer-specific normalization, and matching remains exact; callers must therefore deliver the same retained value.
- A correlated delivery may resume multiple eligible waits that share its event name and correlation value; it is not a single-recipient operation.
- Existing uncorrelated waits remain compatible with unscoped delivery and remain excluded from a correlated delivery.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let an author supply an optional correlation value on an Event wait.
- **FR-002**: When an Event wait has a nonblank correlation value, the system MUST retain that value with the wait for the full time that the wait is eligible to receive an event.
- **FR-003**: When a same-named event is delivered with a nonblank correlation value, the system MUST resume every eligible Event wait with the identical retained value and MUST not resume eligible waits with a missing or different retained value.
- **FR-004**: When a same-named event is delivered without a correlation value, the system MUST preserve the existing broadcast behavior, including eligibility for Event waits with and without a retained correlation value.
- **FR-005**: The system MUST treat null, empty, and whitespace-only authored Event correlation values as absent and MUST preserve unscoped broadcast behavior for those waits.
- **FR-006**: The feature MUST affect only delivery to already-waiting Event instances; it MUST NOT change the rules that determine which workflows are started by a named event.
- **FR-007**: The feature MUST NOT introduce a BPMN-specific correlation authoring surface, alter BPMN import or export, add or source correlation metadata for non-Event waits, or modify non-Event registration and routing code.
- **FR-008**: Existing Event wait and delivery behavior not covered by a nonblank correlation value MUST remain compatible.

### Key Entities

- **Event wait**: A paused workflow activity that is eligible to resume when an event with its configured name arrives.
- **Correlation value**: An optional nonblank author-supplied value that scopes a waiting Event and an event delivery to the same business conversation.
- **Event delivery**: A named event, optionally carrying a correlation value, that may resume existing waits and may independently trigger existing workflow-start behavior.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In automated acceptance coverage containing at least two same-named waits with different nonblank correlation values, 100% of correlated deliveries resume only waits with the matching value.
- **SC-002**: In automated acceptance coverage containing same-named unscoped waits, 100% of unscoped deliveries retain the existing broadcast result.
- **SC-003**: Automated acceptance coverage demonstrates zero changes to workflow-start fan-out, including when a published Event start binding's authored correlation scope differs from the delivered correlation value.
- **SC-004**: All existing Event-routing acceptance coverage continues to pass without changing its stated objective.

## Assumptions

- A nonblank authored Event wait correlation value is trimmed before it is retained. Delivery surfaces keep their existing normalization rules, and matching remains exact.
- Event delivery already carries an optional correlation value to the receive side; this feature makes authored Event waits eligible for that existing narrowing behavior.
- Correlation is an authored opt-in for Event waits in this work unit. Deriving it from workflow identity or adding BPMN authoring controls is separate work.
- Previously created waits without a retained correlation value remain unscoped and require an unscoped delivery to resume.

## Out of Scope

- Changing workflow-start fan-out, start-trigger selection, or workflow-start correlation identity.
- Adding correlation controls to BPMN message catches, receive tasks, message starts, interchange, or collaboration authoring.
- Deriving correlation from workflow identity or applying correlation metadata to every bookmark or non-Event wait type.
- Changing event names, payload behavior, ordering, retry behavior, or event delivery guarantees.

## Constitution Alignment

- The change is additive to existing Event-wait behavior, preserves existing test objectives, and requires direct coverage of both the correlated and unscoped branches in line with framework §§2.21.1 and 2.23.2.
- The Elsa constitution is currently draft; this specification therefore records a narrow, reversible behavior change and does not introduce a new runtime-to-design dependency or deployment shape.
