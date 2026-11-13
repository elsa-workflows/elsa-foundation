# Native metadata admission probe

**Status:** bounded prototype plan for independent review before implementation, 13 November 2026.
M0/#3820 owns this consumer investigation. The kernel product design remains proposed; no product-code approval,
public input API, persisted product ABI or provider capability is selected by this plan.

## Question

Can a native composite retain an exact invocation's bounded business inputs and post-commit admission replies
without activating authored work, while that work waits or the composite records a recoverable blockage?
The prototype's existing native burst accepts messages and then drains activities in one turn. A composite
callback alone therefore cannot establish a separately committed admission result.

The assigned kernel StateMachine spike at local `a91ffd4e35307d16f47f0de68088a6a18647015d` adds a generic
signalled-composite callback and proves a useful sibling-transition subset. Its receiver discovery hydrates
the composite and its `CanAccept` gates evaluation; held transport messages do not supply the M0 receipt,
capacity or metadata-only boundary. This is a source observation of a separate, unmerged investigation, not
a defect claim against its explicitly bounded scope. Do not edit that owner's branch or duplicate its chart.

## Small proposed extension, prototype only

Use the published `prototypes/kernel-model` native interpreter and the same workflow actor. Add no actor per
machine/state, second transport queue/worker, outside row writer, StateMachine type test or kernel scheduler.
The experiment's fixture is a native composite with ordinary children, not another StateMachine interpreter.

1. A live composite registers an invocation inbox through its normal context once, before scheduling phase
   work. Its policy and address are captured in native state under the exact frame occurrence. The native
   interpreter owns that registration; incoming data cannot create a receiver, pick a policy or change its
   capacity. Definition/node IDs alone cannot address a later occurrence.
2. An admission message carries that exact frame, a sender input ID and a bounded name/string-payload envelope.
   The prototype's envelope is deliberately narrower than the eventual JSON/expression/public contract.
   Compute a versioned digest of those exact semantic fields, with unambiguous presence/length encoding.
   Separate transport retries from sender input IDs. Keep same-ID/same-contents receipts and reject changed
   contents; never derive trusted invocation identity from that user ID.
3. Handle an admission-only turn directly against native metadata, without constructing the activity,
   evaluating bindings, selecting transitions, calling guard/phase callbacks or draining the agenda. Commit
   accepted order, queue/receipt state, consumed request and the normal reply/continuation outputs through
   the existing fenced turn. An enqueue result is not the admission receipt.
4. Release a typed admission reply through the normal post-commit reply mechanism. A lost response is uncertain,
   not a rejected/accepted guess. A retry with a fresh transport ID and the same sender ID resolves the committed
   decision without another queue entry. Definite failure before commit must not release acceptance.
5. Pending capacity includes a selected in-flight input until its evaluation settles. Retained receipt capacity
   is separately bounded. Duplicates use the retained receipt before new-input capacity checks. Full refusal
   cannot evict a previously accepted input or assign it a different order. This first probe need not evaluate
   queued chart events; the selected-slot fixture may represent a recorded pending evaluation explicitly.
6. Keep control and authored-work turns separate even if polling returns a mixed inbox. Process a prefix of
   admission requests only, hold the remaining normal messages, and skip authored draining. On a normal-work
   turn, hold admission requests until their own control turn. Do not indefinitely postpone an earlier normal
   continuation because later admission attempts keep arriving. Record source/arrival order used by this rule.

The precise prototype API/type names are internal engineering choices. Preserve the ordinary Event/timer
paths. A signalled-composite evaluation callback may consume admitted work in a later integration slice;
do not add an alternative callback here just to claim the chart was integrated.

## Fixture and controls

Use a non-packable runner against the actual prototype native model, durable actor and in-memory store, with
the existing deterministic simulation's crash/fence harness where feasible. A serialized reload/new owner is
model-recovery evidence, not a real host/provider/process-restart test. No UI or production activity is added.

The composite registers capacity two and a distinct receipt bound. Its ordinary Entry/Action child waits on
an Event. Another variant's ordinary child faults and its parent's normal callback records a blocked phase
without ending the workflow; this tests metadata acceptance after blockage, not an invented operator retry.
Record completed earlier work so a reload can distinguish it from pending work. No authored activity may be
constructed or binding evaluated by an admission-only turn; use a canary callback/binding that would throw.

Exercise root and nested Sequence placement, both persistence modes used by the native prototype, and:

- accept two inputs during a wait, refuse the third, retain their exact order and invocation;
- same sender ID/contents with a fresh transport ID returns its receipt; changed content conflicts;
- a recorded selected input occupies one slot; receipt budget refusal does not erase accepted receipts;
- mixed normal/admission inboxes retain each message for its correct turn and make forward progress;
- after normal child fault and recorded blockage, new inputs still receive metadata admission decisions;
- failed commit before apply, applied commit/lost acknowledgement, fresh-owner reload and stale fenced write;
- an obsolete frame address cannot target a replacement visit/another nested machine;
- emitted acceptance is absent until commit; replay cannot double-append or reorder admitted inputs.

Negative controls must produce their named failures: hydrate/drain during admission, omit the semantic ID
check, omit the capacity check and release a receipt before commit. Use isolated defect switches/copies;
never change the reviewed source or claim a generic thrown exception is the intended assertion failure.

## Resource and evidence boundary

Prepare source and review it before execution. The intended run is one focused prototype runner, through the
normal build-slot wrapper, with one build worker and no shared compiler/node reuse. No product/whole-solution
build, host listener, Docker/provider service, cloud allocation, old Azure lease or expired job is started.
Check load before execution; timing-shaped failures require source/queue/load assessment rather than inventing
a runtime defect. Retain exact source and command, discovered/executed schedule counts, failures/skips and
negative-control results. Build/run limits are explicit in the reviewed runner before launch.

## What remains after this probe

This only tests the admission-control boundary. The four other obligations must not disappear behind a green
runner: complete ordinary-trigger causal cohorts/nested consumers; normal eligible guard/child recovery;
terminal receipts after root/nested completion/cancellation and retention/deletion; the full chart/Studio and
native-compensation matrix. Receipt expiry/canonical JSON, normal authorization and a public sender/query API
also require their actual integration contracts. Do not claim a production retention window from this fixture.

The current prototype workflow actor consumes/drops ordinary messages addressed to an ended workflow before
dispatching its model. Its forced termination also bypasses model callbacks. Consequently terminal duplicate
receipt/query behavior and deletion fencing need a separately reviewed generic control/retention fit; this plan
does not silently change that workflow authority or count active-frame acceptance as terminal qualification.

After source review and the bounded run, record the observed result and smallest shared change on #3820/#4229.
Only then decide its integration with the assigned StateMachine spike. M0/Q1–Q3 and the complete feature remain
open, with the applicable kernel/product design approval gate unchanged.
