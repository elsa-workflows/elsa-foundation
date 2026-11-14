# Statechart selection and phase contract

**Status:** engineering proposal for M0/Q1, 12 November 2026. This refines the historical P2 investigation;
it is not a public definition schema, a runtime implementation or completed M0 qualification. Rules already
chosen by the owner retain their [OD references](../acceptance-matrix.md). Additional choices below are proposed
engineering rules and require review and the integration evidence described at the end.

The [product specification](../spec.md) owns requirements. The [delivery order](../delivery.md) owns sequencing.
This contract addresses chart behavior; the runtime owns durable messages, activity work, values, authorization,
faults and recovery. It does not require the old C2 command numbers, projection fields or provider capabilities.

## Semantic input and identities

A machine invocation contains an active configuration: its active state visits and their ancestor visits. A
visit is a particular entry into a state, not the state's definition ID. Re-entry creates a new visit. A host
restart restores the existing visits and their pending work rather than entering the states again.

A transition has a stable authored identity, a source, a trigger category, an optional condition, ordinary action
work, a destination or an action-without-exit mode, and explicit semantic order. State/transition identities and
semantic order survive save/reload/publication. Diagram coordinates never determine behavior.

An evaluation receives exactly one triggering input and one committed active configuration. A machine-addressed
business input, a completion of an ordinary trigger activity, a state-completion opportunity and automatic
progress are distinct categories. They do not become interchangeable simply because they have the same name.
The [admission/recovery proposal](../m0-admission-recovery-contract.md) records the accepted business-input
obligations; its old implementation mechanism remains historical.

The proposed ordering representation is one explicit total transition order per machine, filtered when showing
or evaluating one source. This satisfies both source-local order (OD17) and unrelated-region conflict order
(OD40) without adding an implicit region priority. Publication rejects missing, duplicated or ambiguous order;
the concrete serialization and editor interaction remain Q3/implementation work.

## Before selection

1. Finish the currently selected transition set's required work. A pending exit, action or entry prevents later
   chart-event evaluation, including when its ordinary activity awaits recovery. Durable bounded business-input
   admission is independent of that semantic gate (OD9–OD11, OD23, OD28–OD29, OD38).
2. Once entry/selected work has completed, evaluate eligible automatic transitions. Repeat after every completed
   automatic reaction until none is eligible (OD18–OD19).
3. Process a still-applicable state-completion opportunity. After its reaction, return to automatic evaluation.
   Completion is offered once per completed compound/parallel visit even when no transition is eligible;
   later value changes do not revive it (OD30–OD32, OD48).
4. Only after this internal progress has settled may the next business input be evaluated, in its durable
   admission order. Duplicate sender retries retain the original queue position (OD36).

Competing distinct ordinary triggers retain OD3: the first eligible trigger processed by the engine wins when
the alternatives conflict. A timer becoming due does not replace the business queue's admission order with
wall-clock ordering. Processing order must be inspectable. Workflow cancellation and operator recovery remain
separate runtime operations.

All guards for an evaluation use its recorded configuration and normal visible-value context. This creates no
stronger cross-activity variable-isolation guarantee than Elsa provides. A guard error stops that evaluation
through normal recovery with its input/decision context retained; it is never treated as false or unhandled.
The integration witness must prove a continuing machine can retain bounded admission during that recovery.

## Candidate selection

Apply these steps to one input. No authored exit/action/entry work runs during candidate discovery.

1. Visit active, entered sources descendant first. At each source, first check whether an already eligible
   matching descendant suppresses this ancestor fallback. If it does, do not evaluate that ancestor's guards,
   even if the descendant acts without exiting. Otherwise evaluate the source's matching rules in explicit
   order and retain its first eligible transition. A false condition permits the next rule on that source;
   if none is eligible, continue toward the parent fallback (OD5). A condition error in a rule actually
   considered suspends selection; it is not permission to try the next rule.
2. Finish that hierarchy-aware discovery before cross-region arbitration. One shared ancestor is considered
   once, not once per active leaf. Suppressing an ancestor does not itself discard candidates in independent
   regions. A suppressed ancestor guard cannot fault an input handled by its eligible descendant.
3. Compute each surviving candidate's exit set and the source visit it depends on. Two candidates conflict if
   their exit sets overlap, or one exits the other's source visit. An action-without-exit has an empty exit set
   but still depends on its live source; another candidate cannot exit it concurrently.
4. Visit surviving candidates in explicit total order. Retain a candidate only if it is compatible with every
   candidate already retained. Compatible independent regional candidates may all be selected (OD6).
   A whole-container exit has no special priority over another region's local rule (OD40).
5. Do not revive an ancestor suppressed at step2 or try a later same-source rule when the source's first
   eligible rule loses a cross-region conflict. This is a proposed engineering rule: order chooses among
   already eligible candidates, not a search for a different interpretation after arbitration.
6. Record the selected set, participating visits and required phase work before running that work. An input
   selecting several compatible regional reactions has one evaluation and one selected set. A recovery does
   not select a different set against a partially changed configuration.

If no candidate remains, a business input receives its final observable unhandled outcome (OD12). It cannot
wait indefinitely for a future state to make it eligible. A rejected ordinary activity trigger follows its
own rearming rule; it is not a retained business input or permission to reset the state visit.

## Exit and entry boundaries

These rules define behavior, not physical activity/frame storage.

| Authored shape | Visits that change | Visits preserved |
|---|---|---|
| Action without exit, no destination | No visit changes; run only action work. | Source, descendants and their waits/local values. |
| Explicit self re-entry | Exit source and its active descendants, then enter a fresh source visit and its required initial configuration. | Ancestors above the source. |
| Transition between descendants under a common compound ancestor | Exit the active branch below the shared ancestor that the target replaces; enter the explicit target path and its required initial descendants. | Shared ancestor and its ancestor visits. |
| Compound-parent transition to its descendant, preserve-parent mode | Exit the parent's active descendants and enter the explicit target path. | Parent visit and parent-owned work/deadline/local values. |
| Compound-parent transition to its descendant, re-entry mode | Exit that parent and its active descendants; enter a fresh parent and the explicit target path. | Visits above that parent. |
| Parallel-parent transition to a descendant | Require explicit whole-parent re-entry: exit all active regions and the parent; enter a fresh parent, follow the target in its region, and enter other regions at their initial states. | Only visits above the parallel parent (OD49). |
| Transition leaving a containing state | Exit every active descendant in the exited scope and then its containing visit. | Visits outside the exit boundary. |
| Child-to-ancestor target, proposed engineering profile | Require whole-target-ancestor re-entry: exit the ancestor and its active descendants, then enter it and its explicit initial configuration, including every parallel region. | Visits above the target ancestor. |

Entering a compound state selects its explicit initial child unless the explicit target path selects a child.
Entering a parallel state enters every region, each at its explicit target or initial state (OD26).
Direct cross-region edges within one parallel container are invalid in the initial release (OD39). Do not
silently rewrite one as a whole-parent reset. Action-without-exit and explicit re-entry remain separate authored
choices (OD15, OD41); a destination equal to a source does not implicitly decide between them.

No initial-release destination restores the historical substates of an exited state (OD46). This restriction
does not affect durable recovery of the current invocation's existing configuration.

The child-to-ancestor row is a new engineering proposal, not an accepted owner choice. It uses the external
transition-domain boundary described by W3C SCXML's `getTransitionDomain`/`findLCCA`
[algorithms](https://www.w3.org/TR/scxml/); this is no full SCXML compatibility commitment. The private .NET
compiled planner exercises this profile separately. The Node reference still rejects that shape, and the
native public prototype-definition adapter remains sibling-restricted until native/schema integration is
qualified. Those current limits do not reduce the required product hierarchy.

## Execute the selected work

Each selected set has three phase barriers: **Exit → Action → Entry** (OD22).

- Exit each applicable active descendant before its exited parent. A parent exit starts only after the exits
  it depends on succeed. After every selected exit has succeeded, start action work.
- Independent selected actions may progress concurrently. After every required action has succeeded, start
  entry work.
- Complete an entered parent's entry before entering its children. Independent regional entries may progress
  concurrently subject to those parent dependencies. The entry barrier completes when every required entry
  succeeds.

An empty phase completes without inventing an activity. Every non-empty slot composes ordinary Elsa activities
and composites; it may suspend durably. There is no guaranteed completion order for independent branches in
the same phase (OD27–OD28).

Persist successful and pending work separately. Recovery uses the failed activity's normal eligible runtime
boundary and preserves completed earlier phases (OD23). For regional Action work, preserve successful siblings
and add no automatic sibling cancellation; still-running work follows the runtime's incident policy (OD29).
A retry's successor activity does not create a new chart visit or selected set.

Proposed extension for Exit and Entry: retain individual successful work and use the same no-added-sibling-
cancellation rule, subject to normal incident policy and recovery eligibility. OD29 does not settle these other
fault paths. Qualify this extension explicitly, including a failed regional Entry beside a still-waiting sibling,
before adopting it as the execution contract.

Cancel pending waits/work for visits that are actually left, through normal runtime cancellation. Cancel does
not undo external effects. A late completion must match the exact visit and work occurrence for which it was
created; it cannot be redirected by a state name, node ID alone or a later visit of the same state.

Entry work succeeding makes that state visit entered. Trigger arming must be tied to that visit and the
agreed activation point; the whole selected set still finishes its entry barrier before another chart input
is evaluated. The physical arm/notification/commit boundary remains an integration proof obligation.

## Ordinary activity triggers and losing work

Ordinary activities/composites may serve as triggers; their completion makes a transition eligible (OD13).
Completion must retain its logical input/source identity so one published input can drive compatible regional
reactions without accidentally grouping independent inputs by equal payload or by time.

- If the completed trigger's condition is false, first let this input's evaluation and any selected work settle.
  Then, if the same source visit remains active, restart only that rejected trigger. Do not reset other waits
  or the existing deadline (OD14). In particular, do not restart a rejected child's composite while a selected
  parent exit is still cancelling that visit.
- After a successful action-without-exit, rearm only that reaction's trigger if the same source visit remains
  active. An event wait listens for a new input; a composite may repeat its authored work (OD42).
- A trigger whose source was exited is cancelled and is not rearmed for a replacement visit. Re-entry arms
  that replacement visit's own work, including a fresh state-owned deadline (OD1, OD15).
- Proposed disposition for a completed candidate that loses arbitration: consume this completion for its
  current visit; if that visit remains active, rearm only its trigger after the selected set settles. Do not
  queue the losing completion to apply after a later state change. This extends no business-input retention.

Cancelling competing work does not retract an already accepted machine-addressed input. Source/cohort closure,
the point at which all matching trigger occurrences are known, and disposition under an ambiguous delivery
must be proved using the runtime's actual durable mechanism. A currently empty worker queue is not that proof.

## Explicit finality and internal completion

A final state is explicit, not inferred from having no outgoing edge (OD20). Its entry work must succeed before
it counts as final (OD31). A compound state becomes complete when its active child is final; a parallel state
becomes complete when all its regions are final (OD8, OD30). No implicit any/threshold completion policy exists.

A completion opportunity belongs to the completed compound/parallel **visit**. Completion-triggered rules on
that owner may react; a child's completion alone cannot satisfy the completion trigger of an incomplete parent.
Nested completion does not itself complete the machine. Only successful entry into a machine-level final state
completes the StateMachine activity, with an explicit mapping to a declared normal outcome (OD45).

Automatic progress has precedence over completion reactions. After any reaction, recompute applicability from
the resulting configuration; stale completion opportunities for exited visits cannot fire. Consuming an
unhandled completion opportunity is durable and once per visit, including across restart (OD32, OD48).

Proposed ordering for simultaneously available independent completion opportunities: use their durable ready
order; where one decision makes several ready together, record children before parents and use stable state
identity as the tie-break between unrelated states. This ordering is inspectable, independent of canvas layout,
and does not promise a completion order for concurrently running regional activity work. The exact ready-order
record and recovery behavior require the integration witness.

## Loop protection and recovery

Automatic/completion progress cannot be allowed to run indefinitely (OD19, OD33). The protection must cover one
causal settling sequence across durable waits, continuations and host restarts. A generic per-drain work budget
that resets on every turn is insufficient. Immediately completing/rearmed ordinary triggers also need bounded
progress; moving the same cycle onto new scheduler work IDs must not evade protection.

On exhaustion, preserve committed progress, stop further chart evaluation and report an actionable incident.
Only supported recovery can release the block; ordinary replay/restart must not repeat the same cycle
automatically. Bounded event admission may continue while recovery blocks evaluation (OD38). Workflow
cancellation remains available independently.

The numeric limit, what advances its counter, when a genuinely new external input resets it, and the permitted
operator recovery actions remain explicit M0/Q2 decisions to qualify on the chosen runtime. This proposal does
not silently adopt a runtime turn budget as a semantic limit or declare loop protection implemented.

## Worked selection traces

These are expected semantics to test, not executed test results.

| Configuration and input | Eligible candidates | Expected result |
|---|---|---|
| Review/Checking; Withdraw | Checking's special withdrawal and Review's default withdrawal | Select the child; suppress the parent fallback. |
| Review/Checking; Withdraw, eligible child but parent fallback guard would throw | Child withdrawal | Select the child without evaluating the suppressed parent guard. |
| Review/Checking; Withdraw, child guard false | Review's default withdrawal | Select the parent; exit Checking before Review. |
| Same false child guard; selected parent exit is pending | Parent withdrawal remains selected | Do not rearm the rejected child trigger during exit; after settlement the exited visit is ineligible for rearming. |
| Parallel Review: Editorial/Checking and Legal/Checking; same published input | Region-local editorial and legal rules | Select both if compatible; preserve phase barriers and both source identities. |
| Same parallel configuration; unrelated Legal rule exits Review | Editorial local rule and Legal whole-container exit | They conflict. Explicit transition order chooses; neither whole-container exit nor region geometry gets implicit priority. |
| Editorial AddComment without exit versus Legal exit of Review | Empty-exit Editorial action and Legal containing exit | They conflict because Legal exits Editorial's source visit. |
| Review action pending; Submit then Withdraw durably admitted | No later chart evaluation yet | Finish the selected action/entry work, settle automatic/completion progress, then evaluate Submit before Withdraw. |
| Child A returned for changes and later resubmitted | New visit of UnderReview | Fresh review deadline; an old visit's timer notification remains stale. |
| A compound completion was offered with false conditions | No completion reaction | Consume this visit's opportunity; later value changes do not resurrect it. |

## Required qualification before adoption

1. Validate every boundary shape above with nested compound/parallel charts, including multiple active leaf
   paths to one ancestor candidate and an action-without-exit conflicting with a containing exit.
2. Exercise alternative regional arrival orders for one shared input and for distinct inputs; prove exact
   grouping and deterministic arbitration from committed identities, rather than payload/timestamp heuristics.
3. Suspend/fault/recover each phase, including independent regional work. Read the exact selected set and
   completed/pending work after a real host restart; do not rerun successful siblings or change visits.
4. Prove business FIFO and automatic/completion precedence, stale completion consumption, rearming and loop
   recovery across real durable continuations. Remove the safeguards in negative controls and require their
   named assertions to fail.
5. Resolve the marked engineering choices and the physical arm/control, guard-recovery and protection details
   with the selected runtime. Update Q1–Q3 and the implementation plan from those results; this document alone
   closes none of those gates.
