# Feature Specification: Activity execution browsing

**Feature Branch**: `agent/codex/3375-activity-executions`
**Created**: 2026-10-26
**Status**: Implemented — local validation complete; external-provider CI remains required.
**Input**: Browse every activity's individual executions and inspect child occurrences in context. Issue #3375. Program bucket: none/free-flow; direct owner request.

## User Scenarios & Testing

### User Story 1 — Inspect any occurrence (Priority: P1)

An operator selects an activity and moves between its recorded executions without losing the canvas.
**Independent Test**: Inspect Write Line in Looping: execution 4 shows `Current: 4`, execution 10 shows `Current: 10`.

1. Given ten executions, selecting the activity initially displays the latest and its position/count; previous/next and an expandable list select exact occurrences.
2. Given an explicit selection, refresh and return navigation preserve it.
3. Given a faulted execution, selecting it retains its values and offers an explicit Issues action.

### User Story 2 — Explore child executions (Priority: P1)

An operator explores a composite's child occurrences and sees only the selected occurrence's runtime evidence on its canvas.
**Independent Test**: Select the fourth Do body occurrence, then Write Line; its captured value is 4. Return to the parent context and select the tenth.

1. Recorded parent relationships scope child execution lists, canvas overlays and activity selection.
2. Breadcrumbs restore outer execution contexts; nested loops and parallel/reusable invocations remain separate.
3. Activities without a recorded execution in the selected scope say so without borrowing evidence from another scope.

### User Story 3 — Navigate safely through large histories (Priority: P2)

Operators browse more executions than the initial run-detail page and open exact timeline, incident and bookmarked executions.
**Independent Test**: Browse a history exceeding one page, open an exact execution link and refresh without changing the selected evidence.

1. Counts and positions describe matching recorded executions, with bounded pages and stable sequence ordering when timestamps match.
2. Loading, empty, partial, denied, unsupported and failed results remain distinct. Obsolete responses never display another selection's evidence.
3. Existing inspection permissions and sensitive-value hiding apply to every navigation path.

### Edge Cases

Single/unexecuted activities, flowchart back-edges, concurrent branches, nested iterations, retries, suspended executions, deleted evidence, changing running-workflow data, boundary-level capture, permission/backend changes, narrow/keyboard/theme layouts.

## Requirements

### Functional Requirements

- **FR-001**: Every activity MUST offer the same generic execution navigation, without an activity-type allowlist.
- **FR-002**: Selection MUST default to latest in scope and retain explicit selections through refresh.
- **FR-003**: The expandable list MUST expose occurrence, status, timing and incident indication; exact IDs remain copyable.
- **FR-004**: Child browsing and breadcrumbs MUST use recorded execution relationships and preserve pinned presentation and reusable boundaries.
- **FR-005**: Iteration wording MUST require runtime iteration evidence; retries MUST remain distinguishable.
- **FR-006**: Lists MUST filter/count within the requested activity and parent/ancestor context before pagination; clients MUST NOT fetch an entire run to build history.
- **FR-007**: Exact timeline, incident and bookmarked selection MUST resolve execution context and never substitute another execution's values.
- **FR-008**: Inspection MUST preserve tenant/authorization/redaction and prevent stale response disclosure.
- **FR-009**: Navigation MUST work with keyboard, narrow layouts and supported Studio appearance modes.

### Key Entities

- Activity: the pinned executable node in its invocation context.
- Execution: one recorded occurrence with sequence, parent, timing and evidence.
- Inspection scope: an exact ancestor execution limiting the displayed occurrences.

## Success Criteria

- **SC-001**: Operators inspect occurrences 4 and 10 of the existing Looping body and read the corresponding values without searching the full timeline.
- **SC-002**: The same controls select ordinary repeated activities and nested/parallel/reusable occurrences without cross-context values.
- **SC-003**: Histories larger than one page remain fully browsable with truthful counts and exact selection.
- **SC-004**: Permission changes and out-of-order responses reveal no evidence from the previous context.

## Assumptions

Use recorded evidence only; no scheduling changes, condition-history capture or variable time travel. Missing capture is qualified. Latest is defined by recorded scheduling time with sequence and identity tie-breakers. Owner approval covers implementation and normal issue/PR bookkeeping; no merge is requested.
