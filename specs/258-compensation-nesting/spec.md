# Feature Specification: Nested, repeated and confirmed compensation

**Feature Branch**: `agent/claude/3046-compensation-nesting`

**Created**: 2026-10-20

**Status**: In progress — runtime seams, scope v3, Confirm, Compensate, validators, journeys and docs land in [#3126](https://github.com/elsa-workflows/elsa-foundation/pull/3126); the Studio work (Phase C) and the container-local-read publication check (T324) remain.

**Program**: [Native compensation #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035) → [Epic #3039](https://github.com/elsa-workflows/elsa-foundation/issues/3039) → [Feature #3046](https://github.com/elsa-workflows/elsa-foundation/issues/3046) (M3). Builds on [spec 250](../250-native-compensation/spec.md) (M1) and [spec 251](../251-compensation-uncertainty/spec.md) (M2). Their requirements and owner decisions still hold, except where this spec says otherwise.

**Input**: Extend compensation to nested scopes, loops and branches, add confirmation and an explicit Compensate activity, settle cancellation, and make each scope and occurrence clear in Studio (#3046).

## Owner decisions (20 October 2026)

| ID | Question | Decision |
|---|---|---|
| OD8 | When a completed inner scope's work must be undone later, who can undo it? | **Only the enclosing scope.** When an inner scope completes `Done`, it hands its compensable, unconfirmed entries to the nearest enclosing compensation scope. Nothing reaches further: there is no workflow-wide registry, and a completed scope keeps no private state. |
| OD9 | Is a completed inner scope compensable by its parent by default? | **Yes, until confirmed.** A new scope input `confirmOnComplete` (default `false`) confirms on completion instead. A **Confirm** activity confirms explicitly. |
| OD10 | Does cancelling a run or a scope compensate? | **Never.** Cancellation stops forward work and leaves the journal readable; it does not run handlers. Authors who want undo before cancelling use an explicit **Compensate** activity. Host shutdown is not cancellation and stays resumable. |
| OD11 | How do faults deep inside a Sequence (or a loop) reach the scope? | **Opt-in descendant-fault delivery.** A structural activity can declare that it handles faults of descendants whose intermediate composites do not handle faults. Sequence and the loops keep their behaviour; the scope opts in. |

Defaults applied without asking (recorded so they can be challenged):

- **Loops.** Per-iteration undo comes from putting a scope inside the loop body. A step inside a single scope's loop is not a supported shape in M3 (the scope runs its steps one at a time; FR-002).
- **Reusable-activity boundary.** Hand-over, Confirm and Compensate never cross a reusable activity's boundary, in either direction.
- **Workflow completion.** Completing the workflow does not confirm anything. Entries that are never confirmed nor compensated end as kept.
- **Named Compensate.** Compensate aimed at an outer scope cascades: each scope in between compensates itself first, then faults upward with a "compensation requested" fault that the next scope treats as a step fault.

## User Scenarios & Testing

### User Story 1 - A completed inner scope is undone by its parent (Priority: P1)

An *Order* scope has two steps: *Reserve stock* is itself a compensation scope (*Reserve warehouse A*, *Reserve warehouse B*, each with a release handler), and *Charge card* follows it. The inner scope completes. *Charge card* is declined. The outer scope undoes newest first: it has no handler for the declined charge, then undoes the inner scope's work, *Release warehouse B* then *Release warehouse A*, using the inner handlers and the inner forward results. The outer scope completes `Compensated`.

**Why this priority**: Nesting is the first acceptance criterion of #3046, and the most common real saga shape (a reusable "reserve" block inside a bigger order).

**Independent Test**: A backend journey with the shared participant: nested scope, a later outer step rejected; assert effect order and that each inner handler targets its own forward key.

**Acceptance Scenarios**:

1. **Given** an inner scope that completes `Done`, **then** in the same checkpoint its compensable, unconfirmed entries are handed to the nearest enclosing compensation scope, which journals them with fresh completion ordinals in their original relative order.
2. **Given** the outer scope later recovers, **then** it runs each handed-over entry's own handler with the entry's own operation keys, newest first across both scopes' entries.
3. **Given** the inner scope's handler needs the inner forward step's result, **then** it receives it, exactly as it would inside the inner scope.
4. **Given** an inner scope with no enclosing scope, **then** its entries end as kept, as in M1.
5. **Given** an inner scope that compensated itself (`Compensated` outcome), **then** nothing is handed over: its work is already undone.

### User Story 2 - Each loop iteration is undone exactly once (Priority: P1)

A ForEach over three line items has a compensation scope in its body: *Reserve item* with *Release item*. All three iterations complete; the ForEach is a step of an outer *Order* scope; a later step fails. The outer scope undoes the three reservations, newest iteration first, each with its own reservation id.

**Why this priority**: #3046 acceptance 1: repeated executions create distinct recovery occurrences, and retries do not create new logical operations.

**Independent Test**: The same journey with three iterations, one of which retries its forward step once (crash re-attempt) and one with an operator retry.

**Acceptance Scenarios**:

1. **Given** a scope inside a loop body, **then** each iteration's scope hands its entries over separately, and each entry records its iteration.
2. **Given** a forward step retried within an iteration, **then** it is still one entry with one operation key.
3. **Given** recovery, **then** each iteration's handler runs once, in descending ordinal, with that iteration's forward result.

### User Story 3 - Confirm ends eligibility, explicitly or on completion (Priority: P1)

After *Charge card* succeeds, the author places a **Confirm** activity: the payment is final and must not be refunded by an outer recovery. Later the outer scope recovers; the confirmed entries are skipped, the journal still shows them as confirmed, and Studio says why they were not undone.

**Why this priority**: #3046 acceptance 3: confirm ends eligibility while retaining audit evidence; invalid targeting is rejected explicitly.

**Independent Test**: Journeys with Confirm (nearest scope and a named scope), with `confirmOnComplete`, and design-time validation of invalid targets.

**Acceptance Scenarios**:

1. **Given** a Confirm activity inside a scope, **then** every entry that is not yet compensating, compensated, failed or confirmed becomes confirmed, with the Confirm occurrence recorded.
2. **Given** a Confirm that names an enclosing scope, **then** that scope's entries are confirmed, including entries handed over from inner scopes.
3. **Given** a scope with `confirmOnComplete` set, **then** completing it confirms its entries and hands nothing over.
4. **Given** a Confirm whose target is not an enclosing scope, or is on the other side of a reusable-activity boundary, **then** publishing is refused with a validation error naming the Confirm node. At run time a Confirm with no enclosing scope faults with a coded fault.
5. **Given** confirmed entries, **then** they are never compensated, and remain visible as confirmed.

### User Story 4 - An author starts recovery explicitly (Priority: P2)

Inside a Flowchart step, a *Check fraud* activity says "reject". The author places a **Compensate** activity on that branch. The scope stops the running step, records why recovery started, undoes the completed work, and completes `Compensated` (or faults, if `faultAfterCompensation` is set).

**Why this priority**: Recovery triggered by a business decision rather than a fault; prerequisite for the cancellation policy (OD10).

**Independent Test**: Journeys for nearest and named targets, including the cascade through an intermediate scope.

**Acceptance Scenarios**:

1. **Given** a Compensate activity inside a scope's running step, **then** the scope records an explicit recovery cause naming the Compensate occurrence, stops the running step, and recovers as after a fault.
2. **Given** the stopped step had a handler and had started, **then** it is journaled as uncertain and compensated (an effect may exist, spec 251 FR-205 rule).
3. **Given** Compensate names an outer scope, **then** each scope in between compensates itself first and the outer scope then recovers.
4. **Given** a Compensate whose target is invalid, **then** it is refused at publication, or faults at run time with a coded fault.

### User Story 5 - Cancelling does not compensate, and says so (Priority: P2)

An operator cancels a run while its scope is mid-way. Forward work stops; no handler runs. Studio shows the scope as cancelled with its journal: what had completed, what was running, and that nothing was undone.

**Why this priority**: #3046 acceptance 4 and OD10: business cancellation must not silently undo or silently leak.

**Independent Test**: Cancel during forward, during recovery and while waiting for intervention; restart mid-cancel; check no handler runs and the journal is readable.

**Acceptance Scenarios**:

1. **Given** a run or scope cancelled while running forward, **then** no new forward step starts and no handler runs.
2. **Given** cancellation during recovery, **then** the running handler is cancelled, no further handler starts, and the entries keep their recovery status.
3. **Given** a host shutdown at any point, **then** the scope resumes; shutdown is never treated as cancellation.
4. **Given** a cancelled scope, **then** its journal is still readable in run inspection.

### User Story 6 - Faults inside a Sequence reach the scope (Priority: P2)

A scope step is a Sequence of three activities. The second fails. Today the fault stops at the Sequence and the run waits forever on a blocking incident the scope never sees. In M3 the scope receives the fault as a fault of its step and recovers.

**Why this priority**: #3046 acceptance 5: each composite's support is verified or excluded from source, not assumed. Sequence is the most common gap (spec 250 research R5).

**Independent Test**: A step that is a Sequence, a ForEach, a While and a For with a faulting leaf; plus a Flowchart and a Parallel to show their existing behaviour is unchanged.

**Acceptance Scenarios**:

1. **Given** a step that is a Sequence, ForEach, While or For whose descendant faults, **then** the scope receives the fault as a fault of that step, absorbs it, cancels the step's subtree and recovers.
2. **Given** a Flowchart or Parallel step, **then** behaviour is as in M1: the composite re-faults and the scope handles that.
3. **Given** a descendant fault inside a reusable activity, **then** it reaches the scope only as the reusable activity's own fault.

### User Story 7 - Studio tells scopes and occurrences apart (Priority: P2)

An operator opens a run with nested scopes and a loop. The run view shows which scope each entry belongs to now and where it came from ("from *Reserve stock*, iteration 2"), shows confirmed entries as "✓ Confirmed", and shows a completed inner scope as "Compensable by *Order*" until its parent finishes or confirms.

**Why this priority**: #3046 acceptance 6: navigation distinguishes nested scopes and individual loop/branch occurrences with accessible labels.

**Independent Test**: Vitest component tests for every state, and the mocked browser suite with axe at both widths and themes.

**Acceptance Scenarios**:

1. **Given** handed-over entries, **then** each row names its origin scope and iteration, and links to the origin scope's run node.
2. **Given** a completed inner scope whose entries were handed over, **then** its summary reads "Compensable by {outer scope}" until the outer scope completes, confirms or compensates them.
3. **Given** confirmed entries, **then** each shows "✓ Confirmed" as text and icon, with the confirming occurrence.
4. **Given** an occurrence picker for a looped scope, **then** the operator can pick an iteration with the keyboard and the screen reader announces it.

### Edge Cases

- An inner scope completes while the outer scope is already recovering (a Parallel branch, or the inner scope is a handler): the hand-over arrives after the outer scope left `Running`. The outer scope journals it and compensates it in the current recovery if not confirmed (late result).
- The outer scope completes `Done` itself: it hands over both its own and the received entries, transitively, to its own enclosing scope.
- A hand-over message is delivered twice: entries are de-duplicated by origin scope occurrence and origin entry id.
- A handed-over entry's handler fails on every attempt: spec 251's retry and intervention rules apply, in the outer scope.
- Confirm runs during recovery: ignored, with no fault; recovery decides (research R11). Publishing refuses a Confirm inside a handler.
- Compensate runs inside a handler or during recovery: ignored, with no fault (no recovery of recovery, research R11). Publishing refuses a Compensate inside a handler, including one whose cascade would pass through a handler.
- Cancel arrives while an inner scope is handing over: the hand-over either committed with the inner completion or not at all.
- A reusable activity contains a scope: its entries end at the reusable boundary as kept unless that inner scope has its own enclosing scope inside the reusable activity.
- A Sequence nested inside a Flowchart step: the Flowchart handles faults of its direct children only, so the fault never reaches the scope; the run faults and nothing is undone (known limit, pinned by a test).
- More entries than one hand-over message carries: sent as several ordered messages in the same checkpoint.

## Requirements

### Functional Requirements

**Runtime seams (generic)**

- **FR-301**: The runtime lets a structural activity send a coded message to the **nearest enclosing activity of a given activity type** (optionally a given authored node), not only to the nearest ancestor of a runtime consumer kind. The search can be told to stop at a reusable-activity boundary. When no ancestor matches, the sender faults with a coded fault, unless the sender asked for a "maybe" delivery, in which case nothing is sent.
- **FR-302**: That message may also be sent from the evaluation in which a structural activity completes after a child completed, not only from its first evaluation. It is delivered ahead of the sender's own completion, in the same checkpoint.
- **FR-303**: A structural activity can opt in to **descendant faults**: when a descendant faults and every composite between them does not handle child faults, the fault is delivered to the nearest opted-in ancestor, naming the direct child it came through. Absorbing such a fault cancels that direct child's subtree. Composites that handle faults today are unchanged.
- **FR-304**: Structural routing data that a parent needs to recognise a child's outcome lives in the child's **scheduling provenance metadata**, not only in command metadata, so an operator retry (spec 251 FR-211) keeps it. Every new scheduling path in M3 follows this.

**Nesting and repetition**

- **FR-305**: When a scope completes `Done` and `confirmOnComplete` is off, it hands each entry that is `Registered`, `NoHandler` or `Uncertain` and not confirmed to the nearest enclosing scope (FR-301, stopping at the reusable boundary; a "maybe" delivery). Handed-over entries carry their step, operation key, forward execution and iteration, forward outcome, handler node and origin.
- **FR-306**: The receiving scope journals handed-over entries with fresh completion ordinals, keeping their relative order, de-duplicated by (origin scope occurrence, origin entry id). In `Running` it just journals them. In `Compensating` or `AwaitingIntervention` it journals them as eligible for the current recovery. In `Completed` or `Compensated` the message cannot arrive (the scope has no live descendants).
- **FR-307**: To compensate a handed-over entry, the scope schedules the entry's own handler node as its child, with the entry's own keys (spec 251 FR-202), the forward execution as scheduling identity, and origin metadata. The handler binds to the forward step's result and workflow variables; variables local to containers inside the origin scope are not available, and publishing refuses a handler that reads them (pending: T324; until then such a handler fails into retries and then waits for an operator, research R18).
- **FR-308**: A scope inside a loop body yields one set of entries per iteration; each entry records its iteration. Retries never create a new entry (spec 251 FR-201).

**Confirmation**

- **FR-309**: A scope input `confirmOnComplete` (default `false`): when set, completing `Done` confirms the scope's entries instead of handing them over.
- **FR-310**: A **Confirm** activity confirms the nearest enclosing scope, or the enclosing scope whose authored node it names. Confirmation marks every entry not already compensating, compensated, failed or confirmed as `Confirmed`, records the confirming occurrence, and never removes evidence.
- **FR-311**: Confirmed entries are never compensated and are never handed over.
- **FR-312**: Publishing refuses a Confirm or Compensate whose named target is not an enclosing compensation scope on the same side of every reusable-activity boundary, and one placed in a compensation handler (for Compensate, in the handler of any scope its cascade passes through). At run time, a Confirm whose named target matches no enclosing scope faults with the runtime's `runtime.ancestor-notification.target-not-found`. A Compensate whose target matches no enclosing scope is not refused: each enclosing scope recovers in turn and the outermost faults with `compensation.scope.compensate-requested`. A Confirm during recovery, and a Confirm or Compensate inside a handler, are ignored with no fault (research R11, R17).

**Explicit recovery**

- **FR-313**: A **Compensate** activity asks the nearest or named enclosing scope to recover. The scope records an explicit cause naming the Compensate occurrence, stops its running step, and recovers. A stopped step that had started and has a handler is journaled `Uncertain` and compensated. One that had started and has no handler is journaled `Uncertain` with no handler; the scope recovers the rest and then faults with `compensation.scope.uncertain-unhandled`, whatever `faultAfterCompensation` says, so an operator gets a blocking incident to act on (research R9). A step that was only scheduled is not journaled.
- **FR-314**: A Compensate aimed at an outer scope cascades: each scope in between compensates itself, then faults with `compensation.scope.compensate-requested`, which the next scope treats as a fault of its step, until the target recovers.

**Cancellation**

- **FR-315**: Cancelling a run or a scope never runs a handler. Running steps and handlers are cancelled, no new work starts, and the private state is kept so the journal stays readable. Host shutdown is never cancellation.

**Composition matrix**

- **FR-316**: Verified behaviour, from source, for a scope step that is:
  - a leaf, Flowchart, Parallel or reusable activity: unchanged, they reach the scope as faults today;
  - a Sequence, ForEach, While or For: supported through FR-303;
  - a State machine: excluded, because no such activity exists in the runtime today;
  - a BPMN process: excluded (ADR 0086).
- **FR-317**: A step inside a Parallel branch of a step is supported only as the Parallel's fault (FR-002 is unchanged: steps run one at a time).

**Durability and repeats**

- **FR-318**: The scope's private state moves to version 3, upgraded from version 2 when read. Version 3 is never downgraded.
- **FR-319**: Every new transition commits atomically with the evaluation that caused it, holds in Durable, Balanced and Ephemeral modes, and ignores repeat deliveries (hand-over, Confirm, Compensate).

**Studio**

- **FR-320**: Run inspection shows each entry's origin scope and iteration, "✓ Confirmed" with the confirming occurrence, "Compensable by {scope}" on a completed inner scope whose entries were handed over, the explicit cause, and an occurrence picker for repeated scopes. Each state is text plus icon. The designer offers a target picker for Confirm and Compensate limited to valid enclosing scopes.

**Documentation**

- **FR-321**: The user guide documents nesting, loops, confirmation, Compensate, cancellation and the composition matrix, and the glossary adds hand-over, confirmed entry and explicit cause.

### Key Entities

- **Hand-over**: the message an inner scope sends on completion, carrying its unconfirmed entries to the enclosing scope.
- **Origin**: the scope occurrence and entry an entry came from.
- **Confirmed entry**: an entry that is no longer eligible for compensation, kept as evidence.
- **Explicit cause**: a recovery cause raised by a Compensate activity, not by a fault.
- **Descendant fault**: a fault delivered past composites that do not handle faults, through a named direct child.

## Success Criteria

- **SC-301**: In the nested, loop and branch journeys, every completed and unconfirmed forward effect is undone exactly once, newest first, in every persistence mode, across host kills after a hand-over commits and between handed-over handlers.
- **SC-302**: Confirmed entries are never undone, and remain visible with their confirming occurrence.
- **SC-303**: A cancelled run never runs a handler; a restarted run never treats the restart as cancellation.
- **SC-304**: A fault inside a Sequence, ForEach, While or For step reaches the scope; Flowchart and Parallel behaviour and every non-compensation Sequence test are unchanged.
- **SC-305**: Each M3 negative control fails when its safeguard is removed: hand-over on completion, de-duplication of hand-overs, skipping confirmed entries, cancellation not compensating, descendant-fault delivery, and provenance-carried routing data under an operator retry.
- **SC-306**: Studio shows every new state as text plus icon, and the axe check reports no violations.

## Assumptions

- Completed scopes keep no private state (current runtime behaviour); the enclosing scope is the only place handed-over entries live.
- Late handlers need only the forward result and workflow variables. Refusing handlers that read container-local variables at publication is pending (T324).
- Operators resume failed handed-over handlers through #2747's retry (spec 251).
- Studio operator actions (retry button, mark resolved, abandon) are M4.

## Out of scope

Workflow-wide or cross-workflow compensation registries, compensating across reusable-activity boundaries, parallel steps inside one scope, retry backoff (M5), Studio operator actions (M4), BPMN compensation (ADR 0086), and a State machine composite.
