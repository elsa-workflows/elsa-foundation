# Feature Specification: User Tasks

**Feature Branch**: `agent/codex/3577-user-tasks`
**Created**: 2026-11-02
**Status**: Implemented
**Input**: Feature #3577, Program #3688; owner-authorized design, planning and end-to-end delivery.

## User Scenarios & Testing

### User Story 1 — Request and answer a decision (P1)

An author configures Create User Task with a title, description, one assignee or a group queue, named actions and an optional form. Each action is a connectable outcome. A participant opens the task in Studio, supplies valid responses, and chooses an action. The waiting workflow continues through that action with the submitted response.

**Independent test**: Publish a purchase-approval workflow with Approve, Reject and Request More Info; execute, answer and inspect the selected branch and exact response.

**Acceptance scenarios**:
1. Given a direct assignment, only that active tenant user can discover and complete it unless a supervisor has explicit management authority.
2. Given an open Finance queue task, two eligible members race to claim; exactly one wins. Completion requires that user's claim. Release returns it to the queue.
3. Given invalid form values, completion returns field errors and preserves assignment, history and workflow wait.
4. Given two submissions or a repeated accepted request, exactly one accepted response and one continuation exist.

### User Story 2 — Work survives interruptions (P1)

Open tasks survive host restarts. Accepted responses survive an interruption before the workflow resumes. Tasks are cancelled when their owning execution ends, is cancelled, or its relevant activity wait is removed.

**Independent test**: Restart with an open task, and separately after acceptance before delivery; complete recovery without losing the decision or creating a second continuation.

**Acceptance scenarios**:
1. A restart preserves the task, its pinned actions/form/prefill, claimant and history.
2. An accepted completion cannot be overwritten by later submissions, form edits, reassignments or releases.
3. Concurrent cancellation and completion settle according to the durable workflow wait's decision. A cancelled task never resumes a workflow.
4. Temporary runtime/store failures retain a visible pending response for recovery; missing authority does not silently count as success.

### User Story 3 — Forms and custom applications (P2)

An author creates reusable form definitions with common fields, validation and prefilled workflow data. A custom application can list, inspect, claim, release, reassign and complete through the same authorized API. Integrators can replace form resolution, rendering and validation without replacing the task lifecycle.

**Independent test**: Use a published reusable form in two workflows, then update the authoring form. Existing tasks retain their frozen form. Complete one task from Studio and one from a custom HTTP client.

**Acceptance scenarios**:
1. Text, multiline, number, checkbox, date and single-choice fields render with labels and instructions and validate on the server.
2. Unknown fields, wrong types, invalid choices, required-field omissions and read-only edits are refused; opaque field names and casing are preserved.
3. A configured action can waive missing required fields, while all supplied values still obey their type/range/choice constraints.
4. A form provider is explicitly identified and versioned. Missing/duplicate providers refuse safely. External integrations expose a usable, authorized form experience without arbitrary script execution in Studio.

### User Story 4 — Manage and understand work (P2)

A supervisor with task-management authority can reassign open work and inspect authorized history. Participants understand whether a task is available, claimed, pending completion, completed or cancelled and who acted when.

**Independent test**: Reassign a claimed task, verify the prior claimant loses access, complete as the new assignee, and inspect immutable actor/action/UTC history.

**Acceptance scenarios**:
1. Reassignment checks the target user/group in the current tenant, records the change, and invalidates old claims.
2. Completed/cancelled tasks are read-only. Stale clients see a conflict and can refresh without losing unsent input.
3. Lists are bounded and filter authorized rows before pagination; totals and identifiers do not reveal other users' or tenants' work.
4. Inbox and detail support keyboard operation and narrow layouts, and show distinct loading/empty/error/denied/conflict states.

### Edge Cases

Duplicate/blank/reserved action keys; malformed form schemas; removed users/roles; membership revoked after claim; Unicode and case-distinct identifiers; task creation retried before bookmark commit; an early completion before the bookmark exists; crash after queue acceptance; workflow deletion; completion pending across runtime failures; activity retries and loop occurrences; API caller-supplied tenant/actor/group spoofing; form updates during a wait; missing provider; completed task retention and untrusted descriptions.

## Requirements

- **FR-001** Create User Task MUST suspend durably and expose author-configured action outcomes and response outputs.
- **FR-002** A task MUST belong to exactly one tenant, execution and activity occurrence; crash replay of the same invocation MUST preserve identity, while separate loop occurrences and explicitly retried successor invocations MUST remain separate.
- **FR-003** Every read and mutation MUST authenticate and enforce permission, tenant, assignment and current group eligibility. The caller MUST NOT supply authoritative memberships or actor identity.
- **FR-004** Group work MUST require a claim before completion, with atomic claim/release/reassignment and stale-client conflict detection.
- **FR-005** Form definitions and actions MUST be frozen for each created task. Responses MUST pass authoritative validation before acceptance.
- **FR-006** Accepted responses and delivery responsibility MUST persist atomically. Recovery MUST replay the same accepted response against the exact owning wait, with idempotent continuation.
- **FR-007** Terminal executions and removed waits MUST cancel outstanding work; completed/cancelled tasks MUST reject new actions.
- **FR-008** History MUST identify the trusted actor, operation, selected action where applicable and UTC instant; a state mutation and its history MUST commit together.
- **FR-009** Studio MUST provide assigned/group inboxes, detail/forms/actions/history and authorized management, using shared module UI conventions.
- **FR-010** A public API MUST support the same operations and validation, with bounded authorized paging and safe errors.
- **FR-011** Basic reusable forms MUST support common fields, prefill and validation; form integrations MUST be explicit, replaceable and authorized.
- **FR-012** Native and Elsa-powered BPMN workflows MUST consume the general task capability through their existing execution contracts; no new token engine.

### Key Entities

Task (identity, owning occurrence, assignment, frozen content, lifecycle, revision); task action (stable key and form requirement); form version (provider, stable identity, schema); response (action and case-preserved values); history entry (operation, actor and instant); delivery responsibility (accepted response and exact wait identity).

## Success Criteria

- **SC-001** Purchase approval succeeds through every configured action, with the matching branch and exact submitted response.
- **SC-002** Open and accepted-but-undelivered work recover after host restart without losing a task/response or adding a continuation.
- **SC-003** Every unauthorized tenant, user, revoked-group, forged-actor and permission-negative acceptance row denies access without leaking work.
- **SC-004** Concurrent claim, completion and cancellation tests yield one valid terminal decision and at most one continuation.
- **SC-005** Studio and a custom task application complete the same form/lifecycle contract, including a supplied integration witness.
- **SC-006** Every acceptance row has executed evidence, required checks pass on the reviewed head and resulting main is qualified before Program closure.

## Assumptions and Boundaries

Existing tenant roles are the default groups, identified by stable role ID; membership is loaded from trusted identity storage on each operation. An eligibility integration can substitute another group source. One human response completes each task. Human-readable action labels are stable outcome names in the initial version. Basic form authoring uses a structured field editor, not a visual layout designer. Due dates, reminders, escalation, voting and a separate portal are deferred. Existing runtime cancellation and persistence contracts retain authority. Foundation/Studio consolidation remains priority one; no unrelated program is activated.
