# Feature Specification: Switch MatchAny case activities

**Feature Branch**: `agent/codex/3052-s12-switch-match-any`

**Created**: 2026-11-22

**Status**: Implemented

The approved capability is implemented in the same PR under the coordinator's explicit delivery instruction. Exact-head validation, independent review and evidence are recorded on PR #5410; final initiative integration and coordinator merge gates remain separate.

**Input**: Run the activity of every matching Switch case in MatchAny mode, including faithfully imported Elsa 3 Switch definitions.

## Scope and dependencies

This work supplies S-12's case-body behavior under the existing [explicit outcome contract](../1208-explicit-outcomes/spec.md), especially FR-028 and FR-031. It applies [ADR 0045](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md), the existing child lifecycle and [variable conflict rules](../../docs/adr/0092-variable-reads-and-writes-share-one-versioned-lifecycle.md). It adds no join mode: outgoing Flowchart paths retain their existing default join.

The implementation enables both native and imported workflows. Runtime needs only the published executable. Step writes to workflow outputs, correlation ID and instance name remain separate work. This spec changes neither expression permissions nor the import boundary.

## User Scenarios & Testing

### User Story 1 - Run every matching case body (Priority: P1)

An author puts an activity inside each applicable case rather than expanding the decision into a graph. In MatchAny mode every matching populated case starts, and the Switch finishes after all selected bodies succeed.

**Why this priority**: This is the refused construct and the capability needed for faithful conversion.

**Independent Test**: Run three cases with two true conditions, distinct observable bodies and an observable successor. Both selected bodies run once, the false body never runs, and the successor cannot start while a selected body is waiting.

**Acceptance Scenarios**:

1. **Given** true, false and true cases with bodies, **When** MatchAny runs, **Then** the first and third bodies are scheduled in case order, may progress independently, and the Switch selects both labels in case order only after both succeed.
2. **Given** a matching empty case and a matching populated case, **When** the populated body waits, **Then** Default does not run, the Switch remains incomplete, and its eventual selection includes both labels.
3. **Given** no matching case, **When** a Default body exists, **Then** only Default runs and the Switch completes with `Default` after it succeeds; without a Default body it completes directly with `Default`.
4. **Given** MatchFirst or omitted mode, **When** several conditions are true, **Then** the first selected body, condition short circuit and single selected outcome retain existing native behavior.

### User Story 2 - Preserve progress across waits and interruption (Priority: P1)

An operator can resume a waiting matching body or recover the process without losing siblings' committed work or starting the Switch's successor early.

**Why this priority**: A fan-out that works only for immediate activities is not a durable workflow construct.

**Independent Test**: Complete one selected body, suspend another, reconstruct the runtime from persisted state and resume it. Previously committed work and input evaluations do not repeat, and the Switch completes exactly once.

**Acceptance Scenarios**:

1. **Given** two selected bodies with independent waits, **When** they resume in reverse order, **Then** each resumes its own execution and the Switch completes only after both succeed.
2. **Given** a selected body faults, **When** its sibling succeeds, **Then** the blocking incident remains visible and no successful Switch completion or outgoing routing is recorded. Existing operator recovery admission rules remain visible; this feature does not newly admit rescheduling a faulted child.
3. **Given** active or waiting selected bodies, **When** the Switch's enclosing subtree or workflow is cancelled, **Then** its selected descendants and waits are cancelled through the normal runtime lifecycle and no success routing is emitted.
4. **Given** a selected body returns `Break`, **When** the Switch handles it, **Then** it propagates `Break` immediately, atomically stops its remaining live child subtrees and emits no case-label selection. Existing enclosing-loop handling still applies.

### User Story 3 - Import an Elsa 3 Switch without flattening it (Priority: P1)

An integrator imports a supported Elsa 3 MatchAny Switch containing activities, publishes it and runs the equivalent native workflow.

**Why this priority**: Removing a conversion diagnostic is useful only if the resulting executable preserves behavior.

**Independent Test**: Convert a nested Elsa 3 Switch with two matching bodies and a successor, publish the result, run it, and inspect both body effects and successor timing. Repeat with no match and with an unsupported nested body.

**Acceptance Scenarios**:

1. **Given** a supported literal MatchAny mode and supported case bodies, **When** conversion runs, **Then** it preserves case order, labels, condition meaning, identities, nested bodies and Default, and does not report the former case-body mode refusal.
2. **Given** converted Elsa 3 `Done` connections, **When** all matching bodies finish, **Then** they route using the existing explicit-outcome conversion and default join, with one successor invocation where selected labels share a target.
3. **Given** a dynamic or unknown mode, an unconvertible condition or an unsupported body, **When** conversion runs, **Then** it still refuses with the applicable stable diagnostic, source location and actionable guidance; it never drops a body to make import succeed.
4. **Given** an Elsa 3 MatchAny case whose converted body can deliver native `Break` to its owning Switch, **When** conversion runs, **Then** it refuses rather than cancelling a sibling wait that Elsa 3 would retain. This includes Break activities, script-selected outcomes and propagated nested labels. A genuine Break signal consumed by a supported loop wholly inside the case remains eligible under existing rules and must leave another selected case running.

### User Story 4 - Author and inspect the chosen mode (Priority: P2)

An author chooses MatchFirst or MatchAny visually in Studio, configures case conditions and bodies, saves and reopens the workflow, then publishes and observes its run.

**Why this priority**: The author must be able to understand and use the runtime behavior without editing source JSON.

**Independent Test**: On the composed Studio host, author two true populated cases in MatchAny, save/reopen/publish/run, and confirm both bodies run. Switch back to MatchFirst and confirm the first body alone runs.

**Acceptance Scenarios**:

1. **Given** an editable Switch, **When** the author chooses MatchAny, **Then** the control, help text and case presentation explain that all matching bodies run and completion waits for them; MatchFirst remains the default.
2. **Given** renamed or reordered populated cases, **When** the author saves, undoes/redoes and reopens, **Then** mode, stable case identity, conditions, nested content and outcome connections remain attached correctly.
3. **Given** read-only or narrow views, **When** the Switch is inspected, **Then** its chosen mode and populated cases remain readable, controls have accessible names, and editing restrictions are respected.

### Edge Cases

- Zero cases; no Default body; all selected cases empty; a mix of empty, false and populated cases.
- A later MatchAny condition fails after an earlier true condition: no body starts from partial selection.
- Bodies finish in another order than the case list; duplicate or delayed child notifications; stale callbacks from another invocation.
- A suspended sibling when another completes, faults or breaks; cancellation before queued bodies activate; recovery between child commit and parent handling.
- Distinct selected outcomes share a target; an unselected path leads toward the same downstream join.
- Two matching bodies read/write the same variable: existing unordered-write diagnostics and runtime conflict policy apply.
- Literal-match legacy cases and predicate cases retain their existing matching and upgrade rules.

## Requirements

### Functional Requirements

- **FR-001**: MatchAny MUST evaluate all applicable case conditions in authored order through the existing typed input pipeline before starting any body. The committed selection basis MUST be reused on redelivery and recovery. A condition error MUST use the ordinary blocking input-failure path and MUST start no case or Default body.
- **FR-002**: Every matching populated case MUST schedule its body once per Switch invocation, in case order. Bodies MUST be independently identifiable and able to wait independently; completion order is not guaranteed and later bodies MUST NOT wait for earlier bodies to finish before they can start.
- **FR-003**: A matching empty case MUST contribute its selected label without creating child work. The presence of any matching case, including an empty one, MUST suppress Default.
- **FR-004**: With no matches, only Default MUST be selected. A populated Default MUST finish before Switch success; an absent Default MUST complete directly with `Default`. An empty case list follows this same rule.
- **FR-005**: Normal MatchAny success MUST wait for every selected body to complete successfully, then complete exactly once with all matching labels in authored order, including labels of selected empty cases. Outcomes from inside a body MUST NOT replace its case label, except for existing `Break` propagation.
- **FR-006**: Outgoing paths MUST be selected only by the Switch's successful completion. A named selection MUST contain no `Done`; existing empty-selection semantics inside a body MUST still count as successful body completion. Shared downstream targets MUST retain the default join's single execution.
- **FR-007**: Completion accounting MUST be scoped to the current Switch invocation and its selected bodies. Unselected children, Default on a matching run, descendants, stale or duplicate notifications and superseded failed executions MUST NOT satisfy missing work. A reconstructed activation MUST recover its answer from committed runtime state, not process memory.
- **FR-008**: A waiting selected body MUST keep the Switch incomplete while other selected bodies may progress. Suspension/resume and process recovery MUST preserve pinned inputs, case selection, child identity and committed child effects, and MUST NOT repeat a recorded step.
- **FR-009**: A body fault MUST preserve the existing Switch blocking-incident behavior and MUST NOT count as success, select case outcomes or silently introduce Parallel's terminal fault/threshold policy. Cancellation of a selected child alone MUST NOT be counted as successful completion. Existing operator recovery admission rules, including refusal to reschedule a faulted child under a parent that has not opted in, MUST remain in force; this feature adds no such opt-in.
- **FR-010**: A selected body's `Break` MUST propagate immediately with no case-label completion. Switch MUST request cancellation of its remaining live child subtrees in the same committed evaluation as that propagation, using existing runtime cancellation behavior. Already committed work remains committed. Workflow `Finish` and enclosing cancellation MUST retain their existing terminal semantics.
- **FR-011**: MatchFirst, omitted mode, existing outcome-only MatchAny and legacy literal matching MUST retain their behavior. No per-arrival routing, configurable child threshold, join selector or new expression capability is introduced.
- **FR-012**: Draft validation, publication, executable remapping and runtime structure checks MUST accept valid MatchAny case bodies. They MUST retain case/slot/condition identity and pinned-contract checks, duplicate-label/identity rejection, supported mode validation and malformed-reference protection.
- **FR-012a**: A newly supported MatchAny configuration with populated case bodies MUST reject the reserved case label `Done` in Draft/publication and executable backstops, with rename guidance. It MUST NOT publish a body-containing Switch whose required selection can violate the existing Done-with-branch prohibition. This bounded rule does not change existing MatchFirst or outcome-only configurations.
- **FR-013**: Design-time child analysis MUST treat distinct MatchAny case bodies as potentially concurrent and unordered, a case and Default as mutually exclusive, and successful Switch completion as after all selected bodies. It MUST NOT claim a guaranteed result dependency between sibling cases. Existing variable-write warnings and conflict policies MUST apply without a new policy.
- **FR-014**: The Elsa 3 converter MUST lower supported MatchAny bodies into the native Switch's case slots, preserving supported literal mode forms, condition conversion, order, labels, stable case identity, body content and Default. It MUST remove only the refusal whose reason is the presence of a MatchAny case activity.
- **FR-015**: Import MUST remain faithful-or-refuse, with no new warning exception. Unsupported modes, labels, conditions, nested activities and unsafe bindings MUST keep applicable diagnostics and guidance. Existing Elsa 3 `Done` mapping MUST remain correct after all selected bodies complete.
- **FR-015a**: A newly enabled Elsa 3 MatchAny case MUST be refused with `ELS3-ACTIVITY-UNSUPPORTED-MODE`, source location and control-flow restructuring guidance unless conversion can establish that native `Break` cannot reach its owning Switch from that converted body. The check MUST cover effective rewritten outcome flow, including Break activities, literal/computed script selections, propagated container outcomes and nested Switch/FlowSwitch labels, with aliases/replacements normalized before analysis. A declared Break port alone does not prove it is reachable; an unproven boundary MUST be refused rather than assumed safe. A loop outside the Switch does not waive this rule. A genuine Break consumed by a supported case-local loop MAY convert under existing rules only when no native Break can escape the case boundary and its source behavior is preserved. No warning exception or change to existing MatchFirst imports is introduced.
- **FR-015b**: A newly enabled imported body-containing MatchAny Switch MUST also validate its own outward selection, not only its bodies. Case labels `Break` and `Done` MUST be refused with localized `ELS3-ACTIVITY-UNSUPPORTED-MODE` and rename guidance. `Break` would turn an ordinary Elsa 3 structured Switch completion into enclosing-container control; `Done` can create an invalid multiple selection. Ordinary unique supported labels and Default MUST remain eligible. This does not change existing MatchFirst/outcome-only imports or native intentional Break outcomes.
- **FR-016**: Conversion fidelity MUST be demonstrated by import, publication and execution with multiple matches, Default/no Default, mixed empty/populated cases and a waiting body. Break-activity, literal/computed script-selected Break and propagated nested-label/bookmarked-sibling counterexamples MUST be deliberate-refusal controls. Owning Break/Done labels in an enclosing Sequence with an observable successor MUST also refuse, while an ordinary-label control runs its successor once. A case-local loop consuming a genuine Break MUST preserve its sibling's wait. Ordinary non-Break script/nested labels and a body with a declared but unreachable Break port MUST remain positive controls. Outcome-only FlowSwitch and MatchFirst MUST remain regression controls.
- **FR-017**: Studio in this repository MUST expose a labelled MatchFirst/MatchAny choice using shared authoring controls, persist it through ordinary draft updates, and show mode-accurate help in editable and read-only Switch views. Authors MUST be able to populate each matching case without source JSON editing.
- **FR-018**: Mode changes, save/reopen, undo/redo, case reorder/rename and nested navigation MUST preserve case identity, conditions, bodies and connections. The composed host MUST prove author→save→reopen→publish→run for two matching bodies, with keyboard, narrow/read-only and validation recovery coverage appropriate to the changed controls.
- **FR-019**: Runtime, converter, publication and Studio guidance that claims MatchAny cannot hold case activities or always selects the first case MUST be reconciled. Historical S-3 restrictions MUST point to this completed capability rather than claim it is still refused once this implementation lands.

### Key Entities

- **Switch case**: a stable identity, unique outcome label, condition or retained literal match, and optional nested activity.
- **Selected case set**: the ordered matches for one invocation, derived from committed inputs; empty bodies remain selected cases.
- **Selected body execution**: a direct child belonging to that invocation, with its existing runtime lifecycle and branch provenance.
- **Switch completion**: one successful selected-outcome set after all selected bodies, or the established explicit control exit.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In the three-case acceptance run, exactly the two matching bodies execute once and the false/Default bodies execute zero times; successor execution begins only after both selected bodies finish.
- **SC-002**: The zero-match, absent-Default, all-empty and mixed-body acceptance runs produce exactly their specified selection and no extra body execution.
- **SC-003**: Reverse-order resume, reconstructed-runtime recovery and duplicate-delivery controls produce one Switch completion and zero repeated committed body effects or condition evaluations.
- **SC-004**: Fault, cancellation and Break controls produce no premature normal-success routing, and interruption leaves no live wait in a cancelled selected subtree.
- **SC-005**: All supported conversion fixtures import, publish and run with the specified body effects and routing; every deliberate unsupported construct remains refused with its applicable diagnostic.
- **SC-006**: The composed Studio journey authors and reopens the two-body Switch without source editing, proves both modes' different run behavior and has no unresolved verified usability finding on the reviewed head.

## Assumptions

- The existing outcome algebra, structural scheduling, input snapshots, durable child records and subtree cancellation are sufficient; no new engine or persistence provider is required.
- Existing retries preserve the same invocation's inputs. New operator re-executions remain governed by their current admission and fresh-input rules.
- Branches may write shared state under the already agreed variable lifecycle/conflict contract; case ordering promises evaluation and scheduling order, not isolation or body completion order.
- Approval and implementation follow the initiative's per-spec gate. A prepared proposal and local document checks do not authorize production implementation or merging.
