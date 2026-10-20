# Feature Specification: Native compensation scopes

**Feature Branch**: `agent/claude/3043-compensation-design`

**Created**: 2026-10-19

**Status**: In progress — design packet [#3043](https://github.com/elsa-workflows/elsa-foundation/issues/3043); M1 runtime under [#3044](https://github.com/elsa-workflows/elsa-foundation/issues/3044), Studio and end-to-end proof to follow.

**Program**: [Native compensation #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035) → [Contract #3041](https://github.com/elsa-workflows/elsa-foundation/issues/3041) → [Design #3043](https://github.com/elsa-workflows/elsa-foundation/issues/3043). Requirements: [PRD #3036](https://github.com/elsa-workflows/elsa-foundation/issues/3036). Boundary with BPMN: [ADR 0086](../../docs/adr/0086-native-and-bpmn-compensation-stay-separate.md).

**Input**: Define the native compensation contract and Studio interaction that make M1 implementation-ready, and specify the M1 slice: a visually authored reservation workflow whose recovery survives a restart and is inspectable in Studio.

This spec is the semantic contract for the whole program (FR-001 to FR-020) and the acceptance contract for the M1 slice (user stories 1–3). Later milestones get their own specs; their stories here record the agreed semantics they must implement, not their acceptance tests.

## Owner decisions (19 October 2026)

| ID | Question | Decision |
|---|---|---|
| OD1 | What does a scope do after its handlers ran? | Completes with outcome **`Compensated`** and exposes the original cause (M1: through inspection; as an activity output once structural outputs are supported, research R12). A per-scope setting re-raises the original fault instead. |
| OD2 | What happens when a handler fails after its retries? | The scope **waits for intervention**. An authorized operator retries the handler, marks it resolved manually with a reason, or abandons recovery. Nothing is labelled compensated. |
| OD3 | Can a completed scope be compensated later? | **Yes, until it is confirmed** or the workflow ends. M1 ships recovery triggered inside an active scope; later targeting lands in M3. |
| OD4 | How does Studio show the pairing? | A **paired step list**: each row shows a forward step and its compensation handler side by side, with a labelled link and drill-in on either side. |

## User Scenarios & Testing

### User Story 1 - Author a compensating scope in Studio (Priority: P1, M1)

A workflow author drops a **Compensation scope** onto a Flowchart, opens it and adds three steps: *Reserve inventory*, *Reserve delivery* and *Confirm order*. They pair the first two with *Release inventory* and *Cancel delivery* as compensation handlers. They map each handler's input to the result of its forward step (the reservation id), then publish. They do this without editing JSON.

**Why this priority**: Visual authoring is a first-slice requirement (PRD D3). A backend-only feature fails the program's mission.

**Independent Test**: In a real browser against a real backend, author the workflow from an empty definition, publish, and read back the published artifact. A missing handler mapping or a step without a name blocks publication with a "Go to" diagnostic.

**Acceptance Scenarios**:

1. **Given** a Flowchart, **when** the author adds a Compensation scope and opens it, **then** Studio shows the paired step list with an empty-state prompt to add the first step.
2. **Given** a step row, **when** the author chooses "Add compensation", **then** the activity picker opens and the chosen activity appears in that row's compensation column, labelled "Undoes: *step name*".
3. **Given** a handler whose input references the forward step's output, **when** the author opens the input editor, **then** the forward step's outputs are offered as captured values and the reference is stored as an ordinary authored expression.
4. **Given** duplicate or blank step names, **when** the author publishes, **then** publication is blocked and each error names its row and navigates to it.
5. **Given** only the keyboard, **when** the author navigates the step list, **then** rows, the forward cell and the compensation cell are reachable and operable, and each control has an accessible name.

### User Story 2 - Recover from a rejected order durably (Priority: P1, M1)

The published workflow runs. Both reservations succeed against an independently persisted reservation participant. *Confirm order* faults with a business rejection. The scope runs *Cancel delivery*, then *Release inventory*, then completes with outcome `Compensated`. If the backend is stopped and restarted during recovery, recovery continues where it was and no reservation is released twice.

**Why this priority**: Recovery that does not survive a restart is not recovery (PRD D5).

**Independent Test**: A backend e2e journey with a file-backed reservation participant and controllable failure points. Kill the host after the first handler's effect but before its completion commits, then restart. Assert the participant's final state, the scope's journal and the handler attempt history.

**Acceptance Scenarios**:

1. **Given** steps that completed in order A, B, **when** step C faults, **then** the scope absorbs C's fault, records it as the cause and runs the handlers for B then A, one at a time.
2. **Given** a step that faulted, **then** its own handler does not run, because it never completed (FR-006).
3. **Given** a crash after a handler's external effect but before its completion is committed, **when** the host restarts, **then** the handler runs again with the same invocation identity, the participant treats the repeat as a no-op, and the journal records one compensated entry.
4. **Given** a crash between two handlers, **when** the host restarts, **then** recovery resumes at the next pending entry. A completed entry's handler is never scheduled again.
5. **Given** all handlers completed, **then** the scope completes with outcome `Compensated`, the workflow continues along that outcome, and the cause is readable through the inspection projection.
6. **Given** each persistence mode (Durable, Balanced, Ephemeral), **then** stories 2.1–2.5 hold, with each mode's restart tests passing (FR-013).
7. **Given** a scope with no failing step, **then** it completes with outcome `Done` and runs no handler.

### User Story 3 - Inspect recovery in Studio (Priority: P1, M1)

An operator opens the run in Studio. The scope shows "Compensated" with text and an icon. Inside the scope, the paired list shows each step's forward result and recovery result side by side. Selecting a handler occurrence shows which forward occurrence it compensated, linked to it, and the original failure.

**Why this priority**: Operators must see why recovery started and what it did (FR11).

**Independent Test**: Browser test against a real backend after the story 2 journey (including the restart), plus Vitest component tests for every recovery state.

**Acceptance Scenarios**:

1. **Given** a compensated run, **when** the operator opens the scope, **then** each row shows forward state (Completed/Faulted/Not run) and recovery state (Compensated/Not needed/Pending/Running/Failed), each as text plus icon, never color alone.
2. **Given** a handler occurrence, **when** it is selected, **then** the inspector shows "Compensates *Reserve delivery*", links to the exact forward occurrence, and shows the scope's recovery cause.
3. **Given** the scope selected, **then** a Recovery panel lists the cause (step, fault code and message, subject to the existing payload disclosure policy) and the entries in recovery order.
4. **Given** the run timeline, **then** handler occurrences are labelled as compensation and are distinguishable from forward work.

### User Story 4 - Uncertain effects and intervention (Priority: P2, M2)

A forward call's response is lost; the activity cannot tell whether the reservation exists. It reports an **uncertain** outcome. The scope records an `Uncertain` entry and, during recovery, runs the step's reconcile-or-compensate handler with the same operation key, which must be safe whether or not the effect exists. When a handler keeps failing, the scope waits for intervention (OD2) and shows the original cause and every recovery attempt.

### User Story 5 - Confirmation, nesting and loops (Priority: P2, M3)

A scope's work can be **confirmed**, which ends compensation eligibility for its entries. A completed inner scope's entries remain compensable by its enclosing scope until confirmed (OD3). A step inside a loop registers one entry per completed iteration, and recovery compensates exactly those occurrences, newest first. An explicit **Compensate** activity starts recovery of the nearest or a named scope from inside any composite.

### User Story 6 - Operator recovery actions (Priority: P2, M4)

An authorized operator retries a failed handler, marks an entry resolved manually with a reason, or abandons recovery, from Studio. Each action shows its consequence before confirmation, records actor, reason and time, and is refused when stale.

### Edge Cases

- A step without a handler completes: it is journaled as `NoHandler` and skipped during recovery, so the journal still shows what ran.
- A scope with zero steps completes `Done` immediately.
- A handler that faults in M1 leaves its blocking incident and the scope in `Compensating`. The run's incident strategy decides what happens next. M2 replaces this with the intervention wait (OD2). M1 never labels such an entry compensated.
- The workflow is cancelled while the scope is active: cancellation runs no activity code today, so no handler runs. The journal remains readable. Compensation on cancellation is an M3 decision (research R7).
- A step that is itself a composite: its faults reach the scope only if every composite in between re-faults (Flowchart and Parallel do; Sequence does not). M1 supports leaf steps and Flowchart steps; the M3 composition matrix closes the gap (research R5).
- A handler references a forward output that the forward step never produced: the handler's input binding faults as for any missing value; nothing is silently defaulted.

## Requirements

### Functional Requirements

**Scope and journal**

- **FR-001**: A **compensation scope** is a native structural activity whose authored structure is an ordered list of named steps. Each step has one forward activity and at most one compensation handler. Step names are unique within the scope and non-blank.
- **FR-002**: The scope runs its steps one at a time, in order. Parallel steps are out of scope (PRD non-goal).
- **FR-003**: When a step completes successfully, the scope appends a **compensation entry** to its journal in the same checkpoint as that completion's parent evaluation. The entry records the step name, the forward activity execution id, its iteration id, a scope-assigned completion ordinal, the handler node, and status `Registered` (or `NoHandler`).
- **FR-004**: While the scope is active, the journal lives in its versioned private state (`ActivityExecutionState.PrivateState`) and is never pruned, so it serves recovery and repeat-delivery protection. The runtime clears structural private state when the scope completes. Completed-scope inspection is derived from run data (FR-019); late compensation of a completed scope (OD3, M3) requires a durable journal or compensation projection that survives completion, because execution metadata and incidents cannot reconstruct entry statuses, ordinals or handler occurrences.

**Triggering and ordering**

- **FR-005**: A fault of a direct step starts recovery: the scope absorbs the fault (spec 115 absorption, which resolves the incident atomically), records it as the **recovery cause** (step, activity execution id, incident id, fault code, message reference), stops scheduling forward steps and enters `Compensating`.
- **FR-006**: Only `Registered` entries are compensated. A step that faulted, was cancelled or never ran has no entry and its handler does not run.
- **FR-007**: Handlers run one at a time in descending completion ordinal. Each is scheduled with the forward step's activity execution id as its scheduling id, so its inputs bind to that exact forward occurrence through the causal result resolver (FR-010).
- **FR-008**: Before scheduling a handler, the scope marks its entry `Compensating` in the same commit as the handler's schedule request. The runtime mints the handler's execution id inside that committed schedule work item, so a replay schedules the same execution. The scope records the id when the handler completes, matching it by the `compensation.entryId` metadata. When the handler completes, the entry becomes `Compensated`. After the last entry, the scope completes with outcome `Compensated` (OD1), or faults with the step's fault when the scope's boolean input `faultAfterCompensation` is true (default `false`). With nothing to undo and `faultAfterCompensation` true, the scope does not absorb the fault and faults at once.
- **FR-009**: A repeat delivery of a handler completion, or a parent re-evaluation after restart, must not advance the cursor twice or schedule a handler for an entry that is already `Compensating` or `Compensated`.

**Captured context and identity**

- **FR-010**: Handler inputs are ordinary authored expressions. Captured context is the forward occurrence's committed result and pinned input snapshot. Both are immutable once committed, so a handler never reads later mutable workflow variables unless its author explicitly references a variable (PRD FR3).
- **FR-011**: The forward step's **invocation id** (`ActivityExecutionContext.InvocationId`) is stable across crash re-attempts and is the idempotency key that forward and handler activities send to external participants. The **logical operation key**, which stays stable across operator boundary retries that mint a new activity execution id, is defined in M2 and owned by this program (see the [program boundary](../../docs/program-goals/native-compensation.md#relationship-to-other-programs)).
- **FR-012**: Handlers are pinned with the scope's executable artifact. A running scope's recovery uses the handler nodes of the artifact it started on, after restart and after later publications.

**Durability**

- **FR-013**: Compensation is supported in Durable, Balanced and Ephemeral modes. The pre-effect boundary is mode-independent: an activity whose side-effect profile is `External` (the default) has its invocation identity and input snapshot durably flushed before its body runs in every mode (`CoalescingRuntimeCheckpointPersistencePolicy`). Support for each mode is claimed only with that mode's restart tests passing. If a mode's tests cannot pass, publication refuses compensation scopes in that mode with a clear diagnostic. Support is never silently downgraded. **Decision (19 October 2026):** all three modes are supported in M1, against #3043's proposal to refuse Ephemeral; see [contracts/semantics.md](contracts/semantics.md#persistence-modes).
- **FR-014**: Publication warns, without blocking, when a step or handler activity is declared `ReplaySafe`. Its claim may be deferred, which is correct for in-workflow work but almost never what a compensated external effect wants.

**Studio**

- **FR-015**: Studio renders a scope's contents as a paired step list (OD4). Each row has an order number, the step name, the forward activity cell and the compensation cell. An empty compensation cell offers "Add compensation". Both cells support drill-in, replace and remove. Rows can be reordered, added and deleted.
- **FR-016**: Handler input editing offers the paired forward step's outputs as captured values.
- **FR-017**: Publish validation errors for scope rows use the existing path grammar `{scopeNodeId}/steps/{index}`, the form `CompensationScopeValidator` emits, so "Go to" lands on the row. Paths cannot key on the step name: the validator also reports blank and duplicate names.
- **FR-018**: Run inspection shows the paired list read-only, with forward and recovery state per row as text and icon. It also shows a Recovery panel for the scope and, on a handler occurrence, a "Compensates" link to its forward occurrence. Handler occurrences are labelled as compensation in the timeline.
- **FR-019**: Inspection adds no endpoint. Studio derives each row from the run data it already reads under the existing activity-execution permission and payload disclosure policy: the scope's latest execution, its children matched by the steps' node ids, handler executions recognised by scheduling cause `compensation`, and the absorbed incident for the cause (research R13). Raw private state is never exposed. Studio reads the activity executions' `provenance` (parent and scheduling ids, scheduling cause) and `outcomeNames`. When the run's activity list is paged (`activityNextContinuationToken`), Studio loads the remaining pages, or marks the recovery evidence incomplete, before it shows a definitive state.
- **FR-020**: Every new Studio control is keyboard operable and has an accessible name, and every state is conveyed by text as well as color. The axe check in the browser test passes.

### Key Entities

- **Compensation scope**: the structural activity `Elsa.CompensationScope` and its authored steps.
- **Step**: a named pair of forward activity and optional compensation handler.
- **Compensation entry**: one journal record per completed forward occurrence.
- **Recovery cause**: why recovery started: a step fault in M1; uncertain outcomes and explicit triggers later.
- **Recovery**: the run of handlers over a scope's registered entries.

See [data-model.md](data-model.md) for schemas and [contracts/semantics.md](contracts/semantics.md) for the state machine.

## Success Criteria

- **SC-001**: The story 1 journey is completed in a real browser from an empty definition, with no JSON editing.
- **SC-002**: In every supported persistence mode, killing the host at each of the four injection points in [quickstart.md](quickstart.md) ends with exactly the expected participant state: no double release and no leaked reservation.
- **SC-003**: Each M1 negative control fails when its safeguard is removed and passes when it is restored: absorption, handler-before-cursor commit order, repeat-completion guard and pinned handler.
- **SC-004**: Studio shows every M1 recovery state with text and icon, and the axe check reports no violations on the scope designer and the run view.

## Out of scope for M1

Uncertain outcomes, handler retry policies and the intervention wait (M2). Confirmation, nested and late compensation, loops, the explicit Compensate activity, cancellation policy and Sequence fault propagation (M3). Operator actions (M4). Parallel compensation, cross-workflow cascades, live-instance migration and exactly-once effects (program non-goals). BPMN (ADR 0086).
