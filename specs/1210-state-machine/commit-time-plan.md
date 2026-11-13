# Persisted commit-time facts for receipt retention

**Status:** proposed M0/#3820 shared-kernel prototype work unit, 13 November 2026. Review before implementation.
This follows the [terminal plan](terminal-receipts-plan.md) and the
[executed counterexample](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6860711364).
No product-kernel design, public API, provider profile or M0 closure is approved here.

## Problem and chosen direction

The local terminal slice at `decd7583e0` fails its first T4 schedule: a finalizing turn waits staged longer than R,
then commits a receipt already expired. The trusted owner clock was sampled before the physical commit.
A successful commit response alone also cannot survive apply-with-lost-acknowledgement.

Propose immutable, model-requested **commit-time facts in the existing actor row**, written under its normal
revision/partition fence. They are generic durable-actor metadata, separate from opaque model state; the kernel
never parses NativeState or branches on a StateMachine type. No new actor, store, queue, transaction, scheduler,
receipt admission authority or independent acknowledgement protocol is introduced.

A single last-commit timestamp was considered. It can be overwritten by a later suspend, terminate or other
command before a private receipt is read, losing the earlier finalization anchor. Avoid making every unrelated
command normalize private receipt state first. Retained immutable facts preserve the exact referenced commit.
An arbitrary grace period, admission timestamp or query-time restamp cannot establish the accepted promise.

## Narrow execution contract

1. Extend the prototype actor row with optional commit-clock metadata. Ordinary actors that never request it
   retain a null field and their existing behavior. Representation and API names remain engineering choices.
2. A model can request a reference to its current turn's commit-time fact, resolve a retained reference without
   creating/restamping it, and release facts it no longer owns. Requests in the same turn may share one fact.
   References are local to the actual actor occurrence; they do not solve the separately open delete/recreate ID bug.
3. The staged fact has no time yet. A qualified store stamps it from its authoritative commit authority in the
   same fenced transaction that writes state and consumes messages. Existing facts remain immutable. Neither a
   caller, binding, native activity nor the workflow owner supplies the timestamp.
4. The successful response and worker cache must reflect those same committed facts. On an unknown response,
   reload resolves the stored facts. A stale writer cannot create or change them. Resolving an absent/unavailable
   fact is unresolved, not permission to expire a receipt or manufacture a new anchor.
5. Models retain references while their obligations need them. Native receipts capture R at admission and request
   a fact only on final disposition; lookup derives the immutable deadline from that fact plus captured R.
   Pending receipts still have no terminal expiry. Cleanup releases a shared fact only when no retained native
   receipt references it, using the same actor commit; it cannot release workflow-owned facts.
6. The workflow actor owns one terminal fact when a workflow using clock metadata first ends, including normal
   completion, forced termination and a parked failure. Repeated controls cannot restamp it. This provides an
   end anchor even when an older model must preserve future private state opaquely. A later compatible reader
   must use the original committed terminal fact, never its own read time, to interpret interrupted pending work.

A transaction's early wall-clock read is not automatically an attestation of durable disposition time. Each
provider must identify the actual trusted commit-time authority or a conservative committed bound that preserves
at least the full promised R. Delays before/during application, response delay and lost acknowledgement must be
qualified against that declared authority. Do not call existing `Committed.Now` sufficient without this proof.
The first in-memory prototype can attest its synchronous atomic application time; that is not EF/Mongo proof.

## Capability, compatibility and failure behavior

An unqualified store must not silently accept a batch requesting these facts. Use an explicit prototype store
capability, default unavailable, and refuse unsupported use before accepting a retention promise. Existing
ordinary models remain usable. Wire/decorator stores must forward that capability and preserve the facts.
There is no claim that older kernel/provider binaries understand the new row field or enforce its retention.

The in-memory implementation and its callers are the first bounded cut. EF/Mongo and other provider profiles
remain unavailable for this capability until their durable representation, clock authority and loss/reload
conformance are qualified. They must reject incoming clock-metadata writes rather than truncate facts through
an older codec; no schema migration or production activation is implied by this prototype. A live mixed-version
or unsupported-provider path must preserve accepted obligations or refuse safely, never report false expiry.

Workflow metadata dispatch must handle a legitimately absent model state (failed initial turn persisted JSON
null) without invoking a model reducer on null or wedging the command slot. Keep the normal control/fault boundary
and retain a defined lookup result. Model state upgrades must preserve existing receipts without inventing R.

## Bounded qualification before terminal integration

Review exact shared source and executable inventory, then use one focused build/run packet through the normal
build-slot wrapper, one worker, bounded commands/processes, no host/provider/Docker/cloud or old Azure job.
Prove on the actual actor/store path:

- Delay a turn's real state/message commit beyond R; its newly committed fact uses the actual atomic apply time.
- Apply without acknowledgement, reload/take over and resolve the same fact; before-apply loss creates no fact.
- Stage an actual old-owner clock write, let a new owner commit, then execute the stale call: fence refuses it.
- Same-turn requests share a stable fact; later turns/ordinary controls do not overwrite it; resolve never creates.
- Release only the owned fact, preserve other references and terminal ownership; old rows without metadata still run.
- Unsupported store capability refuses the new promise and clock write; ordinary native/model workflows still work.
- Actual failed-start → metadata lookup → normal Delete terminates without a null-state crash or a busy slot.

Use isolated named negative controls for pre-commit stamping, lost facts on reload, restamping and unsupported
write acceptance. Preserve failed/unrun stages and do not count an arbitrary exception as a successful control.
Run relevant existing admission, Statechart and native/kernel regressions after shared edits. The terminal
integration must subsequently turn the retained T4 counterexample green on the real receipt/delete path, including
its null-state, shared-reference cleanup and terminal-fact cases. A shared clock test alone is not receipt proof.

## Remaining boundaries

This resolves a proposed clock mechanism, not the full StateMachine feature. Actual provider clock/storage and
mixed-binary rollout, execution incarnation, causal trigger cohorts, normal guard/phase recovery, full hierarchy/
parallel semantics, authorization, transport/value retention roots, Studio and compensation qualification remain
open. Keep the kernel design proposed and record the exact accepted evidence on #3820/#4229 before adoption.
