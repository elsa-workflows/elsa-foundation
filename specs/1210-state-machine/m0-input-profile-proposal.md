# M0 proposal: chart inputs and activity-trigger ownership

**Status:** Input/occurrence and owner-lifetime principles agreed under delegated owner authority on 5 November 2026, against exact proposal commit `036a88bfbc721e8a982f35bd11c5735b1ed6e3a9`: [recorded decision](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6669211076). Independent review preceded that decision; reviewer conclusions are not the agreement. The concrete durable fence, transitive obligations, atomic selection/recovery, control/admission protocol, native placement and Q1–Q3 remain design/proof gates. This is no production implementation plan, Model X ratification or producer grant. OD1–OD49 remain unchanged.

## Why this decision is next

The [N9 result](evidence/m0-native-witness-5d4c.json) qualifies real native visit frames within its fixture. The [corrected N10 result](evidence/m0-native-witness-a787.json) qualifies ordinary composite results retaining their actual originating input across separate completion times, restart and later control input. Neither fixture chooses production eligibility or a phase-command protocol.

In N10, one boundary finishes with A while another still waits for B; the second later finishes with A too. Common originating data therefore cannot alone define when a chart must select a transition set. An implicit wait for every composite to finish would be a new execution rule. Conversely, choosing the first callback without defining shared-input boundaries would not establish compatible regional selection. M0 needs a deliberate contract before production code.

## Agreed input principles

Use explicit chart input kinds, with different identities for a business admission, a trigger occurrence and its provenance:

| Input | Admission/evaluation identity | Eligibility principle |
|---|---|---|
| Machine-addressed business event | Sender event ID within the exact machine invocation; canonical receipt and durable admission order | Evaluate that event against the current stable active configuration and all matching authored machine-event rules. Select the compatible set once under hierarchy and authored-order rules. |
| Ordinary activity/composite trigger completion | Trusted current delivery/readiness wave plus exact retained trigger activation/attempt and committed completion occurrences | Collect ready occurrences from that wave, then evaluate the compatible set against one configuration. An earlier external origin remains separate provenance; equal result contents are not an identity. |
| State completion | Exact owning state visit and its one completion opportunity | Apply the agreed once-per-visit, automatic-before-completion rules. |
| Automatic progress | Current configuration and the persisted internal-progress chain | Exhaust eligible automatic transitions before completion reactions and queued business events, retaining the agreed loop-protection boundary. |

The machine-event rule is generic infrastructure for the agreed invocation-addressed queue. Ordinary Event activities keep their normal transport/wait contracts; the design must not silently reinterpret every global Event delivery as an acknowledged machine admission. A connector can explicitly send a normal external event to the machine's admission surface. This distinction must be visible in Studio's trigger choices and documented for authors.

### One source wave before arbitration

Preserve the common-event requirement for **independently authored ordinary triggers** as well as direct machine-event rules. When one actual publication/delivery reaches multiple active waits, its trusted source/cohort identity defines a delivery wave for that machine. Record resulting ready trigger occurrences and establish a durable wave-closure boundary before choosing the transition set. The boundary requires the relevant recipient deliveries and causally runnable continuation work to settle; it does not wait for a composite that has durably suspended. A partially delivered or unknown wave is not declared closed by a timing window or an empty local queue.

This needs an explicit shared-runtime source/continuation fence, or another reviewed mechanism with the same guarantee. The existing captured recipient cohort proves who was selected for delivery; it does not by itself prove that all resulting ready notifications have reached the chart. N10 did not qualify this fence. Source delivery failures, yield/restart and partial continuation must preserve their real outstanding obligations and normal incidents rather than turn unknown work into ineligibility.

At closure, arbitrate all currently ready occurrences together under child-over-parent precedence and compatible-region selection. A guard error remains a fault rather than ineligible fallback. A trigger which consumed the event but is still durably waiting is not yet ready and holds no implicit wait-for-all barrier. When later input B makes that composite complete with retained result A, the completion participates in B's actual readiness wave (or its exact completion cause when there is no shared wave); A remains its earlier data/provenance. This does not re-admit or revive an earlier queued business event A. A completion whose original visit ended remains stale.

For distinct source waves, use the engine's observable processing order at their defined eligibility boundary. Do not manufacture wall-clock priority or let an early physical parent callback bypass a child that belongs to the same still-open wave. The concrete contract must specify closure, evaluation order and durable guard recovery without resetting accepted business-event FIFO or internal-progress priority.

### Trigger occurrence lifetime

Every authored activity trigger and activation has one actual owning container. This proposal adds no shared-activity reference syntax; normal reusable activities still follow the ordinary definition/binding lifecycle. Independent triggers reached by the same actual delivery are grouped through the delivery boundary above, without moving them to another lexical owner.

A rejected occurrence is consumed. Rearm only that trigger, once, if the same source visit remains active, after the input evaluation and any selected work from that evaluation settle. An action-without-exit reaction rearms its trigger after Action succeeds. A completed losing candidate is consumed rather than replayed against a later visit; any allowed rearm follows the same visit check after selected work settles. Unaffected pending waits and deadlines remain intact. When a visit exits, cancel its relevant trigger subtrees and reject late completions by exact visit/activation identity.

**Agreed direction; mechanism still gated:** retain these delivery-boundary and occurrence-ownership principles, including the requirement for a real closure mechanism and the treatment of later durable completions. The reviewed contract must cover both two independent regional Event/composite triggers and independent child-versus-parent triggers receiving one publication. Common-publication grouping remains required for independent triggers. No closure mechanism or production placement is approved by this document.

## Agreed ownership principles; native placement pending

Prefer the ordinary native StateMachine activity, with real machine, compound-state and leaf-visit containers owning the corresponding normal variable frames. Production placement is still pending agreement on the concrete shared input/control extension and the complete qualification matrix.

| Work | Proposed execution owner and lifetime |
|---|---|
| State Entry and Exit | Ordinary child work under the actual state-visit container. Exit finishes while that container's normal frame is still live. |
| State-local trigger and guard | Ordinary work under the source visit. Each trigger belongs to its real declared source owner and its bindings see that normal lexical scope. Each transition's guard runs in its own declared source scope. |
| Transition Action | Ordinary work under its declared source owner. An owner whose visit is in the selected exit set stays open until its required Action work finishes, then closes through normal completion before replacement Entry. Continuing ancestors and action-without-exit owners remain open, retaining their visits, frames, waits and deadlines. An ancestor-owned Action does not implicitly read a descendant's locals. |
| Target Entry | A new actual visit/container for each state being entered, preserving applicable ancestors and initializing new visit-local values normally. |

The semantic active configuration and the native container's cleanup lifetime must be explicit: a selected exiting state stops offering reactions and its relevant waits are cancelled even while Exit/Action work keeps its frame open. Such an open frame is not permission to evaluate another event against the old stable configuration. No closed frame is reopened or retained as an alternative value store.

The native coordinator needs a reviewed, checkpointed way to request work from an already active visit. `SchedulingActivityExecutionId` changes causality, not lexical parentage; ordinary child-to-parent notification is not proof of a reverse command path. The concrete design should reuse the actor, scheduler, checkpoint and outbox authorities and expose only the smallest necessary generic invocation-input/control callback. It must validate exact invocation, pinned material, authority and applicable lifecycle; preserve recorded work and sibling results during recovery; and make duplicate/stale internal commands harmless. Operator `ScheduleActivity/1` is not the proposed runtime coordination mechanism.

## Proof and agreement gate

The principles decision does not accept the missing mechanism. Its contract and implementation still need independent review against the owner decisions, event outcomes, delivery closure and authored trigger choices. Confirm that independent common-event triggers preserve child/parent precedence and compatible regional selection, an evaluated-unhandled input is never revived through retained provenance, and occurrence handling does not silently change lifetime or scope. Production proof must establish the actual shared-runtime fence; a captured cohort, a receipt flag or N10's fixture join cannot substitute for it.

The concrete production contract and plan must then cover shared-input compatible regional selection, competing distinct completions, a composite durably waiting after an earlier event, hierarchy fallback with guards in their real scopes, selected-work phase barriers, cancellation of leaving-state waits, Exit/Action frame closure, and normal secret/withholding/reusable-boundary policies. The invocation-wide admission, content fingerprint, receipt/retention, ambiguous-commit and terminal-race contract remains Q2. Studio's actual definition, trigger ownership and inspection journeys remain Q3. None of these is closed by the bounded N9/N10 results.
