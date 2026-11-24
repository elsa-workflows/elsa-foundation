# StateMachine on the kernel

> **Owner decisions of 19 November 2026.** The owner reviewed the closed StateMachine program's 49 decisions
> ([#3815](https://github.com/elsa-workflows/elsa-foundation/issues/3815), OD1 to OD49 of 4 November) against the
> kernel and answered four questions on 19 November ([owner decisions](#owner-decisions-19-november)). The invariants
> S1 to S4 below are a coordinator design under those answers. K7 builds and checks them
> ([plan](../program-goals/clean-slate-kernel.md#milestones)), and K10 carries the Studio part. Register:
> [StateMachine on the kernel](decisions.md#statemachine-on-the-kernel-19-november).

StateMachine is a native composite on a statechart engine (register, 12 November,
[execution models](decisions.md#execution-models-and-jobs)). The engine has no kernel dependency. The composite runs
inside the workflow instance's turns and reaches the foundation only through the model contract
([the foundation contract](foundation-contract.md#the-model-contract-built-on-the-actor-turn)). The
[design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#containers-and-their-semantics-owner-decision-12-november)
left the placement for #3815's M0 to confirm. #3815 closed on 19 November without confirming it, and the owner's
cards below do not name it. This page reads the 12 November register rule as the placement (a coordinator reading).

## Owner decisions (19 November)

Each answer is an owner card.

1. **The machine's inbox: the kernel inbox only (option A).** A machine keeps no inbox, receipts, capacity or
   retention of its own. Admission, deduplication, order and holding are the foundation's (FC-3, FC-16, FC-18):
   - OD34: an outside add past the foundation's inbox bound answers `Refused(InboxFull)` with `RetryAfter`, and an
     internal send is never refused. There is no capacity per machine.
   - OD35: a repeat of the same id answers `Duplicate`, whatever its content. The outcome is read from the journal tail.
   - OD36: the machine evaluates events in the turn's delivery order, and held events keep their place. Only one
     sender's order is guaranteed.
   - OD37: the turn that ends or cancels the machine records each held event as not processed, with the reason.

   Rejected: (B) a layer 3 cap on held events per machine; (C) the prototype's invocation inbox with receipts in
   workflow state (`models/Native/InvocationInbox.cs`, which the
   [fairness page](fairness-and-backpressure.md) calls M0 research); (D) widening the foundation contract with a
   bound per target, a content digest, `RefusalReason.Ended` and an order across senders.
2. **What a machine event targets: kernel events (option A).** Each machine invocation subscribes once per trigger key
   for its whole life, with a `wait` that names the invocation. Senders use a turn's `Publish` or an outside publish to
   `Event(key)`, with the correlation in the key. An outside publish with the same `(key, id)` within its `KeepFor` is
   one publication (FC-6). An event published while no machine waits on its key reaches no machine, so it has no
   outcome. Rejected: (B) direct sends to the workflow instance with a frame address in the payload; (C) both.

   Two levels follow from this answer (a coordinator design under it):
   - **Kernel subscriptions** are per invocation and key: a key is subscribed when the invocation starts, or in the
     commit that first arms a trigger on it, and stays for the machine's life. The turn that ends the
     machine removes them (`Unsubscribe`, or the instance's `End`, FC-7).
   - **Trigger arming** is per visit and held in the machine's committed state. Entering a visit arms its triggers,
     and leaving it disarms them. Later arming and disarming never subscribe or unsubscribe.

   **Parked machines (coordinator choice).** A Park removes the subscriptions (FC-7), so events published while the
   machine is parked do not reach it. Machines subscribe without `keep` by default, matching the owner's default for
   `keep`, and an author may opt in to `keep`. A recover re-arms the subscriptions from `RestoreWaits` (FC-10).
3. **Release 1 scope: the full surface (option A, widened).**
   - Hierarchy and parallel regions in the engine (K7).
   - In Studio (K10): the chart designer with the transition inspector and the hierarchy outline (OD24, OD25, OD43),
     and the run view of the active configuration and pending phases, with linked event and transition history
     (OD44).
   - History states stay deferred (OD46).

   Rejected: (B) the designer in 1.x; (C) hierarchy only in release 1.
4. **The saga-style example: example without compensation (option C).** Release 1's example models a saga-like
   lifecycle with ordinary activities and explicit compensating states and transitions, and adds no Saga type. There
   is no compensation port in release 1; porting native compensation (ADR 0086) to the kernel is a later scope
   decision. Rejected: (A) native
   compensation on the kernel in K6 with the example on it; (B) no example in release 1.

## Invariants S1 to S4

These invariants widen the sketch's S1 to S4
([plan step 3](../reports/clean-slate-kernel/design-sketch-draft-5.md#plan)), which the prototype's witness already
checks ([Statecharts.Run](../../essentials/tests/Workflows/Runtime/Statecharts/Run/README.md)). K7 runs them in the
simulation under its faults, in Embedded and Cluster, at `Full` history. Each mutation must be caught.

| Invariant | Rule | Mutations |
|---|---|---|
| **S1 Run to completion** (OD10, OD19, OD22, OD32, OD38, OD48) | For each machine invocation, no business event is evaluated between the commit that selects a reaction and the commit that settles it. A reaction is settled when every phase is done, no automatic or completion reaction is eligible, and each completion opportunity has been offered once per visit and consumed. Events delivered in that span are held (FC-18) and keep their order | Consume a held event mid-phase; evaluate an event ahead of an eligible automatic transition; offer a completion twice in one visit |
| **S2 One outcome per event** (OD2, OD3, OD6, OD12, OD14, OD37) | Every machine event that a committed turn consumes gets exactly one outcome entry: handled (with every compatible regional transition it selected), unhandled, or not processed (the machine ended or was cancelled). A duplicate (FC-3) gets none. A held event gets none until it is consumed. A late timer or job result for an exited visit changes no configuration. An event for an exited visit, or for a trigger that is not armed, changes no configuration, and the event gets its unhandled entry | Drop the unhandled entry; record an outcome twice after a lost acknowledgement; let a stale timer fire a transition |
| **S3 Legal configuration, and waits that match it** (OD7, OD20, OD26, OD30, OD31, OD39; OD2, OD13, OD14, OD15, OD41, OD42, OD49) | After every committed turn, the machine and each active compound state have exactly one active child, and each active parallel state has every region active with one state each. No state is active under an inactive parent. The machine completes only at its top-level final state, after that state's entry work succeeds. No timer or job of an exited visit survives the exiting commit (FC-4, FC-5). A trigger is armed only while its source visit is active, and an event for a trigger that is not armed is consumed as unhandled (S2). A preserved parent keeps its armed triggers, timers and jobs. A re-entered parent's old visit loses its armed triggers, timers and jobs in the exiting commit, and the new visit arms its own in the commit that completes its entry. `RestoreWaits` returns the invocation's subscriptions plus the active visits' timers and jobs, and nothing else (FC-10) | Keep an exited state active; skip a region's initial state; miss a `Recall` or `CancelJob` on exit; leave an exited visit's trigger armed; drop the invocation's subscriptions from `RestoreWaits`; restore a timer or job of an exited visit; re-arm a trigger after its visit ended |
| **S4 Ordered phases** (OD15, OD22, OD23, OD27, OD28, OD29, OD41, OD49) | For each selected reaction, all exits finish before any action, and all actions before any entry. On each hierarchy chain, exits run innermost first and entries outermost first. Regions may interleave within a phase. An action-without-exit runs no exit or entry. A self re-entry runs exit then entry and starts a fresh visit. A parent re-entry runs the parent's exit and entry around the child's phases, and a preserved parent runs neither. After a fault and its recovery, a completed phase never runs again in the committed history | Exit the parent first; enter before the action barrier; rerun a completed exit after a retry; run a preserved parent's exit |

## The 49 decisions on the kernel

- **Carry:** layer 3 implements the decision unchanged.
- **Adapt:** the decision holds in the kernel's terms given.
- **Conflict:** the decision contradicted the foundation contract, and an owner answer above resolves it.
- **Scope:** an owner answer above places the decision in or out of release 1.

| OD | Class | On the kernel |
|---|---|---|
| 1 | Carry | A generic composite with no domain types; compensation semantics stay the model's (ADR 0099 keeps ADR 0086) |
| 2 | Adapt | A visit's timer is `Send(Instance.Ref, kind, payload, dueAt)`, a send to itself, with its id kept in the visit; the exit calls `Recall` in the exiting commit (FC-4), and a delivered timer whose id is not the current visit's is dropped as stale |
| 3 | Adapt | The first processed trigger is the first in the turn's delivery order (FC-3, FC-18); the losers are disarmed in state, with their timers recalled and their jobs cancelled in the same commit, and the choice is recorded |
| 4 | Scope | Root and nested use comes with the native composite; the designer is in release 1 (answer 3) |
| 5 | Carry | Child-first selection with parent fallback |
| 6 | Adapt | One subscription per machine invocation and key (FC-7, answer 2); the engine gives the one delivery to every compatible regional transition in one turn |
| 7 | Carry | A parallel state completes when every region is final |
| 8 | Scope | Hierarchy and parallel regions are both in release 1 (answer 3) |
| 9 | Carry | Phase slots hold ordinary activities; the pending phase is in committed state and shown through the statechart's journal entries |
| 10 | Adapt | Later events are held (FC-18) until the selected work settles; the selected plan and phase are committed state and survive an owner change (FC-1, FC-2) |
| 11 | Adapt | An event is durably accepted as an `Accepted` admission or a committed `Publish` (FC-2, FC-16); capacity is the foundation's inbox bound (answer 1) |
| 12 | Adapt | The turn consumes the event and records an unhandled entry (FC-11); it is visible at `Full` history, the default, and not at `State` or `Minimal` |
| 13 | Adapt | A trigger slot is an ordinary activity; losers are disarmed in state, their timers recalled and their jobs cancelled (`Recall`, `CancelJob`; FC-4, FC-5), and effects already done stay done under their effect key |
| 14 | Adapt | Only the rejected trigger restarts: its timer stays (no `Recall`) and it stays armed in state, so the invocation's subscription brings it the next event (FC-6); a composite trigger's rerun repeats its effects at least once |
| 15 | Carry | Action-without-exit and self re-entry are distinct reactions |
| 16 | Carry | Ordinary workflow definitions; the native composite placement is the 12 November register rule (a coordinator reading) |
| 17 | Carry | Authored transition order is definition data |
| 18 | Carry | Automatic transitions are evaluated in the turn after entry work completes |
| 19 | Carry | Internal progress settles in memory before held events are evaluated, under layer 3's own step limit; FC-17's caps are a backstop |
| 20 | Carry | Completion needs an explicit final state |
| 21 | Carry | Machine and visit values follow the native value rules and values by reference; chart state keeps handles only |
| 22 | Carry | Exit, Action, Entry, each able to wait durably (S4) |
| 23 | Adapt | An activity fault is a native blocking incident committed with the progress so far ([observability](observability.md), decision 1), so completed phases stay done; only a throwing turn parks (FC-10), and its uncommitted work reruns at least once on retry |
| 24 | Scope | The chart designer is in release 1 (answer 3, K10) |
| 25 | Scope | The transition inspector is in release 1 (answer 3, K10) |
| 26 | Carry | Explicit initial states; a parallel state starts every region |
| 27 | Carry | Exits innermost first, entries outermost first |
| 28 | Carry | Regional work interleaves over the turns of one single-writer actor (FC-1), and jobs run outside turns |
| 29 | Adapt | As OD23: a regional fault is a native incident, siblings' committed progress stays, and recovery is the native intervention retry; the foundation's `Recover` resolves only a Park |
| 30 | Carry | Completion transitions stay local; the top-level final completes the activity |
| 31 | Carry | A final state counts only after its entry work succeeds |
| 32 | Carry | Automatic, then completion, then business events |
| 33 | Adapt | The loop bound raises a native blocking incident and the instance stays `Running`; recovery is a native intervention message, outside the closed command list (FC-9) |
| 34 | Conflict | Resolved by answer 1: `InboxFull` at ingress with `RetryAfter`; an internal send is never refused; no capacity per machine |
| 35 | Conflict | Resolved by answers 1 and 2: `Duplicate` per sender, target and id (FC-3, FC-16), or one publication per `(key, id)` within `KeepFor` (FC-6), with no content comparison and no receipt |
| 36 | Conflict | Resolved by answer 1: delivery order, held events keep their place (FC-18), one sender's order guaranteed (FC-3) |
| 37 | Conflict | Resolved by answer 1: the ending turn records held events as not processed with the reason; outcomes are journal entries under the data lifecycle's retention |
| 38 | Adapt | Under an incident the instance stays `Running` and holds events (FC-18). While parked, direct arrivals are kept as `Arrived` and `InboxFull` still applies (FC-10, FC-16); the Park removes the subscriptions (FC-7), so published events are missed unless the author opted in to `keep`, and a recover re-arms the subscriptions (a coordinator choice, answer 2) |
| 39 | Carry | No direct transitions between regions, enforced by validation |
| 40 | Carry | Authored order for conflicting candidates in different regions |
| 41 | Carry | Preserve or re-enter a compound parent; waits follow the choice (S3, S4) |
| 42 | Adapt | The trigger is re-armed in the commit of the turn where its action succeeds, while the source visit is active; the event it consumed cannot return (FC-3), and events held during the action count as new |
| 43 | Scope | The hierarchy outline is in release 1 (answer 3, K10) |
| 44 | Adapt | The run view is a K10 projection of the statechart's journal entries in commit order (FC-11); causal links are ids in the entries; the executed version is the header's `Labels`; outcomes replace receipts (answer 1) |
| 45 | Carry | Final states map to declared native outcomes |
| 46 | Scope | History states stay deferred (answer 3); a restart restores the configuration from committed state and `RestoreWaits` (FC-10) |
| 47 | Scope | Release 1's saga-style example uses ordinary activities and explicit compensating states and transitions (answer 4) |
| 48 | Carry | A completion opportunity is offered once per visit (S1) |
| 49 | Carry | A transition from a parallel parent to a descendant re-enters the whole parent (S3, S4) |

Totals: Carry 24, Adapt 14, Conflict 4, Scope 7.
