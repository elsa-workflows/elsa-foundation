# Ordinary intervention and successor recovery

**Status:** proposed M0 engineering profile, 14 November 2026. This is not product architecture agreement, a public
recovery protocol or completed StateMachine support. [FR-034/OD23 and FR-040/OD29](../acceptance-matrix.md) require
ordinary recovery preserving completed phases and successful regions. The [admission contract](../m0-admission-recovery-contract.md),
[selection contract](selection.md) and [value lifetime proposal](values.md) retain their own authority.

## Existing behavior and reference boundary

At source `db416ee7c8f962693bf16dca6dd14ebf9efccb17`, the private native model removes a failed frame, cancels its
children, disposes owned resources and propagates the fault. Its default composite callback faults the parent.
It therefore cannot yet qualify the required continuing-coordinator recovery. `WorkflowInstance.Park` keeps the
previous checkpoint but makes the workflow Faulted; reopening that row is not an implementation of this contract.

The current Foundation implementation provides the ordinary semantics to preserve, without constituting an approved
API for the proposed kernel:

- [ContinueWithIncidents](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Incidents/ContinueWithIncidentsIncidentStrategy.cs)
  opens an incident while preserving workflow execution. An open incident alone is not retry eligibility.
- [Child intervention](../../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeChildFaultInterventionRequest.cs)
  and the ordinary WaitForIntervention action retain a blocking fault with a deferred structural parent continuation.
- [RetryFaultedActivity](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/Handlers/RetryFaultedActivityAlterationHandler.cs)
  requires an exact Faulted source, running parent, source-owned blocking incidents awaiting intervention, no conflicting
  descendant incident/claimed work and available pinned executable material. A terminal run or ended parent is refused.
- [Successor staging](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/ActivitySuccessorStaging.cs)
  creates a fresh execution/scope, preserves pinned inputs, parent/scheduling/branch/iteration provenance and logical
  operation keys, and clears per-invocation private state and wait/attempt markers. The
  [boundary command](../../../Essentials/src/Workflows/Runtime/Core/Models/RetryActivityBoundaryCommand.cs) rejects reuse of the source ID.

These are observed source rules. Provisional constitution sections and bounded prototype evidence supply no additional
product approval, default incident strategy, public capability, provider or migration agreement.

## Proposed native profile

The experiment is explicitly opt-in, with effective policy persisted for the invocation. Legacy/default fault behavior
stays unchanged. Initially qualify materialized stateless leaf faults under declared supported normal structural parents
in Balanced/Durable. Hydration/intrinsic/stateful-resume/background/composite-owned faults and custom-parent recovery must
have explicit support/refusal boundaries; they are mandatory later qualification, not removed first-release scope.
Rowless/Ephemeral, providers and production authorization/retention remain unavailable until their own proof.

Use the existing native checkpoint and actor writer. Logical records below do not reserve CLR names or a public schema:

| Evidence | Required identity/lifetime |
|---|---|
| Failed source | Exact actor creation, source activity occurrence, node/material reference and ordinary input snapshot. Faulted is distinct from runnable. |
| Intervention | Source-owned incident with an explicit WaitForIntervention outcome, not an arbitrary open incident. |
| Outstanding ordinary work | Stable logical operation and parent scheduling/branch/iteration/tag context, independent of its current physical child execution. |
| Successor | Fresh execution/scope; preserved logical operation and pinned inputs; new per-invocation ownership, private state and waits. |
| Parent binding | Exact old/current child reference and its replacement, committed together with the successor continuation. |
| Control outcome | Exact source/incident/request and declared scalar contents; replay must not schedule another successor. |

A normal structural parent can defer a child fault for intervention while its own frame and ancestors continue. Do not
cancel successful or independently waiting siblings simply because a child failed. Explicit absorption/cancellation must
remain distinct and use their ordinary cleanup. No work/evaluation can run under a faulted subtree merely because a
retained frame exists. A failed invocation closes its own old inbox; an active continuing ancestor keeps its own accepted
queue/receipts. A successor receives no copied source queue or receipts.

Inputs use the existing materialization/snapshot owner. Capture bindings once; create a successor from the pinned ordinary
snapshot with correct new invocation ownership. Do not reevaluate values changed by a sibling, hand a mutable forensic
source snapshot to a successor, or add a chart-specific evaluator. Existing value/secret policies remain authoritative;
the private registry/JSON profile does not establish product executable pinning, secret or mixed-binary support.

## Proposed retry boundary

Before mutation, verify the exact creation/policy/source, blocking intervention outcome, live parent, usable lexical
parent, available pinned node/input material and absence of conflicting descendant work/incidents. A continuing parent
cannot authorize work under a retired source visit. Unsupported or changed material/routing must produce an honest
refusal; it cannot silently retry under the newest node or reset the machine.

Prepare the parent's replacement through a generic, constrained structural-state preparation seam. It receives the
current owned parent state and exact source/successor/provenance, and returns a proposed replacement. It cannot schedule
activities, execute guards, materialize unrelated authored inputs or emit external effects. Validate the proposal before
applying it. An unsupported/mismatched parent cannot leave partial mutation. Known frame-independent containers can keep
their state; frame-indexed consumers such as StateMachine must replace the exact physical-child reference while preserving
its logical phase obligation. This seam and supported parent set require independent implementation review.

One ordinary checkpoint must include all of: intervention resolution, source supersession and necessary subtree cleanup,
fresh successor and its normal input/scope/operation ownership, parent rebinding and owed successor continuation. An
uncertain commit/repeated control cannot resolve an incident without its continuation, allocate a second accepted retry
or lose the parent's outstanding work. The source execution is never reopened. A stateful successor, when later qualified,
starts a fresh invocation under normal rules rather than replaying a consumed resume payload or inheriting an old wait.
External effects are neither undone nor guaranteed exactly once.

For StateMachine integration, preserve its selected set, source/exit/state-visit identities, completed phase steps,
successful regions and still-owned lexical values. A retry must not repeat Exit, select again or initialize target values
before required Action success. Rebind its current child atomically; leaving the old physical ID until a later completion
is invalid when another sibling callback can arrive first. Parent cancellation, absorption, supersession and genuine
completion retain the existing resource/receipt disposal guards. Guard and loop incidents need their own eligible ordinary
boundary; a phase-leaf retry does not establish those paths.

Pure metadata admission/query remains independent of authored input activation and cannot resolve the intervention or
advance chart evaluation. Pending/selected reservations remain counted. A recovery blockage must not become an unhandled
event, false guard, empty source cohort or automatic loop retry. This contract implements no source-wave settlement;
shared source/store owner review and transitive closure proof remain separate pre-code/integration gates.

## Compatibility and proof boundary

Version private native fault/control data separately from the existing StateMachine visit/phase shape. Pure readers
validate without allocating or activating a policy from newer host settings. Legacy checkpoints preserve their effective
legacy behavior; mixed/future/malformed incident ownership must be refused. Old empty/missing fields are not historical
incident evidence. Retained scopes and current/planned visits must not be reset during upgrade or probing.

Qualification uses actual native work and durable control delivery: source fault/intervention, completed and waiting
siblings, input/operation/provenance identity, fresh successor, committed parent binding, ordinary scopes, cancellation,
absorption, lost-before-apply/lost-answer control, owner restart and stale/duplicate/conflicting/ended-parent refusal.
Read committed state and receipts independently; distinguish reader fixtures from actual new-writer/reload runs. Controls
must catch terminal parent fault, sibling cancellation, repeated successful work, reused source ID, lost provenance,
source-private-state/wait reuse, missed parent rebinding, early resolution, duplicate successor and premature business
progress. A precise retained guard is labelled as such, rather than called an independent work oracle.

No bounded leaf/known-parent result closes M0 or the full feature. All ordinary activation kinds, actual chart
phase/admission/guard/loop/trigger integration, source closure, normal product recovery/operator inspection, providers,
compensation composition and Studio remain required. Obtain applicable source/product architecture agreement before
production adoption. Focused independent source/raw acceptance and resulting-main verification remain delivery gates.
