# StateMachine authoring and execution inspection

**Status:** M0/Q3 engineering proposal, 13 November 2026. This specifies the user journeys and integration
contract before UI implementation. It is neither inspected Studio evidence nor agreement on the runtime's
unproved admission, recovery or projection mechanisms. [OD1–OD49](../acceptance-matrix.md) retain their meaning.

## User goal and reference

An author models a lifecycle with states, ordinary activity work and explicit transitions, publishes it through
normal workflow/activity definitions, then inspects the exact executed version to understand an input's result
or recover failed work. The same journey works for a root machine and a machine nested inside a Flowchart,
Sequence or reusable activity. Document review is sample content; the editor adds no approval-domain types.

Use the existing [graph authoring workspace](../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/GraphAuthoringWorkspace.tsx),
[Sequence drill-in](../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/SequenceDesigner.tsx),
[activity inspector](../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/AttachedRunActivityInspector.tsx)
and shared [module controls](../../../Studio/docs/module-ui.md) as composition references. Their existence is
not StateMachine support. The [graph contribution resource filter](../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/graphAuthoringContributions.ts)
defaults old contributions to workflow definitions; a new StateMachine contribution must explicitly support
activity definitions too rather than assuming workflow-only contributions are compatible there.

## One definition and editing transaction

The machine stays inside the existing definition document, using its normal save, autosave, history, publication,
authorization and reusable-activity rules (OD2, OD16). Chart metadata references ordinary owned activity slots;
it is not another authored asset or a second source of executable truth. Designer selection, collapse state and
viewport position are presentation state. They cannot alter startup, transition priority or visit mode.

Use one document mutation command per user operation so undo/redo covers states, connections, slots, identities
and related references together. Undoing a delete restores the same identities/order; duplicating creates fresh
identities and remaps references internal to the duplicate. Duplicate/paste must report invalid or out-of-scope
references for repair rather than silently discard authored activity work. Incoming transitions are not copied
merely because their destination is duplicated. The exact shared command implementation remains design work.

Dragging/reparenting a state preserves its identity and work. Preview affected transitions, initial selections
and scope changes; refuse a structurally impossible drop. If a permissible move leaves a semantic diagnostic,
keep the affected content visible and identify the repair before publication. Deleting a state/container shows
the exact scope, affected transitions and contained work before committing the user's deletion. Undo remains
available under the normal document history contract.

Unknown/unavailable activity types retain their serialized content and diagnostic rather than being replaced
with an empty slot. Unsupported chart content needs an explicit unsupported/read-only state; opening and saving
must not erase it. No import/export interchange or SCXML support is implied.

## Chart and synchronized outline

Render a focused state/transition chart with collapsible compound states and parallel regions, plus a synchronized
hierarchy outline (OD24, OD43). A row represents the same stable authored identity as its shape. Selecting either
selects the other and its inspector; a collapsed container indicates relevant hidden selection/diagnostics.
Users can reveal the selected element without resetting their entire chart viewport.

The outline supports keyboard navigation through the hierarchy, expand/collapse and selecting an item. Creation,
renaming, choosing an initial state and defining/reordering a transition must be possible without dragging a
canvas edge. Focus returns to the invoking state/transition/slot after closing its editor. Closing a dialog
without committing leaves the definition unchanged. Avoid a focus trap or canvas-only recovery path.

Every machine, compound state and region has an explicit initial child selection (OD26). An empty chart is an
editable draft with an Add state action; it cannot publish until required initial selections are valid. Adding
or moving the first shape does not silently choose execution startup. Parallel entry shows each region's initial
state; a final state is explicitly final (OD20). A state without outgoing transitions is never styled as final
solely because it has no edges.

State details expose kind, initial child where applicable, machine/state-visit values, Entry and Exit activity
slots and the declared outcome mapping for machine-level final states. State-local values describe one visit;
the normal binding chooser must distinguish their owner from machine-wide values (OD21). This is no new value,
expression, secret or conversion policy. A nested final state cannot select an enclosing workflow outcome merely
by its name (OD45).

## Transition inspector and activity work

The structured inspector shows Source, Trigger category, Condition, Action, Destination/visit mode and explicit
semantic order together (OD25). Incompatible choices are constrained and explained before an invalid edge is
committed. A transition's label summarizes its rule; coordinates and edge routing do not imply its priority.

| Choice | Author-visible behavior |
|---|---|
| Ordinary activity/composite trigger | Open its ordinary activity editor. Event waits and timers are easy palette choices, with general composition still available (OD13). |
| Automatic | No trigger activity; condition-based progress precedes queued business events (OD18–OD19). |
| State completion | Offered for its owning compound/parallel visit, once per visit, after required final entry work (OD30–OD32, OD48). |
| Condition | Normal expression editor/binding behavior; an expression fault is not described as false or unhandled. |
| Action without exit | No destination; label that the source visit, local values and deadline continue (OD15, OD42). |
| Explicit self re-entry | Label the new visit, fresh deadline and repeated Exit/Entry work (OD1, OD15). |
| Compound parent to descendant | Explicitly choose preserve-parent or re-enter-parent, with affected visits explained (OD41). |
| Parallel parent to descendant | Require whole-parent re-entry; explain that other regions restart at their initial states (OD49). |
| Conflicting unrelated regions | Display explicit order within the machine; do not suggest that the visually first region wins (OD40). |

Provide Move earlier/later operations for semantic order. Source-local rules and cross-region conflicts use the
same visible ordering representation proposed in [selection](selection.md); reordering it is a semantic document
change, separate from shape ordering or automatic layout. Display compatible regional reactions as compatible,
without suggesting that only one can ever run.

Entry, Exit, Trigger and Action summaries open the normal activity editor with breadcrumbs back to the exact
chart element. Returning preserves chart selection, expansion and viewport. Drill-in can itself contain a
Sequence, Flowchart, nested StateMachine or reusable activity under existing composition/ownership rules.
The chart does not flatten these activities into transition edges or replace their inspector/editor.

## Validation and failure states

Authoring and publication share the same semantic validation owner. Studio may provide early feedback, but its
canvas cannot approve a definition rejected by the backend. Diagnose stable IDs/paths so the chart and outline
can reveal the offending element. Distinguish blocking errors from diagnostics such as an accidental dead end.

At minimum validate containment/identity/order, initial children, final-state intent, destinations, declared
outcomes, slot ownership and cross-region restrictions (OD17, OD20, OD26, OD39). Reject missing/duplicate order,
direct cross-region edges and unsupported visit modes with actionable messages. Ancestor-target semantics and
other unresolved Q1 shapes need their contract resolved before product validation claims support; the reference
model's rejection is not permission to silently omit a required authoring shape.

Read-only/published/source-owned definitions permit navigation, selection and inspecting work but expose no
mutating path through keyboard, palette, canvas, inspector, context menu, duplicate/paste or drill-in. Denied
access is distinct from an empty chart. Save/publication conflicts and validation failures retain the user's
document and selection through normal recovery. Loading a replacement version keeps current content until the
new version is ready; do not clear the chart first or silently substitute a latest-version graph.

## Execution inspection and evidence identity

Extend the normal workflow execution experience (OD44). Select the exact workflow execution and machine
invocation, including the enclosing scope/reusable boundary for nested machines. Load the exact executed
definition/version. Show the active configuration in both chart and outline, and retain explicit state-visit
identities so re-entry, retry and a different invocation are distinguishable.

Show the selected transition set, pending Exit/Action/Entry phase and ordinary activity work linked from that
phase. Independent regional work may wait/fail/complete separately; the UI must not suggest a guaranteed branch
completion order. Successful sibling work remains visible during recovery. Normal activity details, incident
and recovery tools retain their permission and eligibility checks; chart selection is not an alternative state
mutation or retry API.

Input history distinguishes durable acceptance from evaluation and final result. Display the engine's actual
admission order, semantic selection, handled/unhandled result, blocked recovery, capacity refusal, duplicate
receipt and conflicting-ID refusal as applicable (OD3, OD11–OD12, OD34–OD38). Completion/cancellation shows final
not-processed reasons for queued inputs; selected interrupted work remains distinguishable. Do not infer an
event's semantic order from activity timestamps or claim cancellation undid an external effect.

The backend must supply this evidence with definition/invocation/visit/input/transition/work identities and a
committed revision or equivalent consistency marker. Projection delay or missing retained evidence is visible;
Studio must not infer success from an empty queue or merge unrelated snapshots into a falsely coherent phase.
The exact query/journal contracts, retention guarantees and history-level behavior remain Q2/kernel integration
work. This document does not invent a second execution projection or promise evidence a history mode omits.

## Acceptance journeys before delivery

Use the composed-host [UX acceptance procedure](../../../docs/contributing/studio-ux-acceptance.md). The following
are planned tests and observations, not executed results. Cover both workflow-definition and reusable activity-
definition authoring, exact-version execution inspection, light/dark themes and narrow layouts. Narrow-screen
outline/inspector operations must expose the same valid choices even where canvas dragging is impractical.

| Journey | Required observation |
|---|---|
| Root document lifecycle | Create Review/Changes/final states, select initial states and order, edit ordinary slots, save/reload, undo/redo, publish and run the exact version. Revisions re-enter Review with a fresh deadline; old timer evidence cannot advance its replacement visit. |
| Nested lifecycle | A Flowchart validates input, runs the same machine and branches on declared normal outcomes. Open nested ordinary work and return with chart context retained. Reusable definition ownership/publication/permissions remain normal. |
| Parallel review | Editorial and Legal react compatibly to one logical input; show both regional actions and phase barriers. An explicit containing exit cancels remaining work; conflict order stays stable after moving shapes and reload. |
| Recovery and queued input | Wait/fail a phase, accept later events up to capacity, inspect exact receipts, restart the real runtime, recover through its ordinary tools and observe preserved successful work plus ordered evaluation. |
| Invalid and read-only | Repair missing initial states, duplicate order and invalid destinations; no content disappears. Prove all mutation paths refuse edits in published/source-owned/read-only contexts and keyboard focus remains usable. |
| Inspection during change | Change/publish a newer definition while inspecting an older execution. Keep its exact graph/IDs and stable content while loading; expose projection lag, absent retained evidence, denied access and unavailable activities explicitly. |
| Native compensation composition | Author the separately qualified OD47 topology using existing compensation activities, then run/restart and inspect its real journal/recovery boundary. No Saga type, new compensation authority or cancellation-as-undo claim. |

Each implemented slice adds the smallest meaningful regression to its normal editor/runtime suites, plus actual
composed journeys for the changed experience. Independent acceptance opens the downloadable current-source
screenshots/traces and records findings/retest, keyboard behavior and unverified states. Passing fixture tests,
rendered mockups or a green build cannot substitute for that inspection. Keep Q3/M0 open until the integrated
runtime/schema contract and applicable design agreement support these journeys.
