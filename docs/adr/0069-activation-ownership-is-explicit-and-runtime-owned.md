---
status: accepted
date: 2026-08-18
decision_context: Spec 151 (issue #1304), FR-B-006 as amended by the 2026-08-15 architect review and the 2026-08-17 takeover decision
---

# Activation Ownership Is Explicit And Runtime-Owned

Supersedes in part [ADR 0043](0043-publication-slots-define-start-authority.md), whose authority model —
the slot as the sole start authority, and its revisioned compare-and-swap — is unchanged and still in
force. What this ADR replaces is **where that ledger lives, who owns a slot, how ownership is decided, and
who may take one from someone else.**

## Context

ADR 0043 gave publishing a `(WorkflowDefinitionId, SlotName)` slot as the only thing that grants a workflow
permission to start new work. That was correct while publishing was the only way an executable became live.

It is no longer the only way. An engine can now acquire executables by **importing content-addressed artifacts
from a mounted source** and reconciling them at boot, with no design side and no publish pipeline present. Two
independent paths can therefore want the same definition live, and the question ADR 0043 never had to answer
becomes the central one: *when two sources both want a slot, who gets it, and how does the ledger know?*

Two properties of the answer are forced by constitution §E2.2. The activation ledger is a **runtime** concept —
it decides what may start, which is a runtime responsibility — so it cannot stay in publishing. And the runtime
must not learn publishing's vocabulary in order to arbitrate, because a runtime that knows what "publishing"
means is a runtime that depends on the design side it is supposed to be separable from.

## Decision

### 1. One activation ledger per engine, runtime-owned and neutrally named

`IWorkflowActivationAuthority` in `Elsa.Workflows.Runtime.Core` is the definition-keyed ledger of what is live.
The publish pipeline and the artifact reconciler share it; there is one physical storage unit
(`workflowActivationSlot`) behind it.

Publishing's `IPublicationSlotStore` was **deleted, not relocated**. Relocating it would have moved a contract
named for one of its two callers into the domain that must not know that caller exists. Deleting it and
introducing a neutral contract is also what makes an overlap between the two paths *structurally* detectable:
two ledgers cannot conflict with each other, they can only both be right.

Deletion rather than migration is affordable because the product is pre-1.0 with no consumers of the old unit
(spec 151 research R2). The orphaned `publishingPublicationSlot` storage unit is recorded as a clean break in
`HistoricalSchemaUpgradeTests`, not as an upgrade path.

### 2. Ownership is an explicit field, never inferred from identifiers

`WorkflowActivationSlot` carries a `WorkflowActivationSource(Kind, SourceId?)`. Ownership is read from that
field and from nothing else.

Activation ids do carry readable prefixes (`import:{sourceId}:…`), and sniffing them would have worked on the
day it was written. It was rejected anyway: an activation id is an opaque string, so a guard that parses one
turns a log-formatting change into a silent authority bug. Two reconciliation sources pointed at different
folders are different owners, which the `SourceId` half expresses and a prefix test would collapse.

### 3. One coordinator owns the complete lifecycle — activation *and* deactivation

`IWorkflowActivationCoordinator` runs the whole sequence in both directions. Callers keep what is genuinely
theirs — publishing keeps compilation, policy and its `PublicationRecord` attempt journal; the reconciler keeps
closure validation and import — and neither holds a copy of the activation sequence.

This finished as a correction, not as a design flourish. Until it did, the coordinator owned activation while
`PublicationProjectionReconciler` owned retraction, and **both had to independently know the same ordering
invariant** (recurrences materialized and validated before any binding is written). They drifted: one was
updated, the other was not, and the divergence was invisible because a test double reproduced the retired
shape. `IPublicationProjectionPreparer` and `PublicationProjectionReconciler` were therefore deleted rather
than re-synchronised — a second path that must stay in step is a defect generator, and no comment prevents the
next divergence. The deleted reconciler's test objectives moved onto the coordinator's deactivation path.

### 4. There is no delivery-intent ledger, deliberately

ADR 0043 decided that publishing records durable `PublicationProjectionIntent` entries, delivered idempotently
and converged by a reconciler. None of that exists: the contract, its models, its stores and the
`publishingProjectionIntent` storage unit are gone.

The coordinator's sequence is in-process and compensating, so a failure leaves nothing half-done and the
**recovery unit is the caller's next attempt**. There is consequently no delivery record to converge, and
keeping one would be worse than useless — a `public` contract that nothing writes to looks supported, and
invites a composer to write to it.

### 5. Publishing outranks reconciliation through an explicit intent; reconciliation never reclaims

An activation request carries a `WorkflowActivationOwnershipIntent`. The default, `RespectExistingOwner`,
refuses a foreign-owned slot with the `ForeignSource` conflict. `TakeOver` claims it and becomes its owner.

**The mechanism is the decision here, not just the outcome.** The authority honours the declared intent and
never inspects who declared it. A test of the shape `if (source.Kind == "publishing")` inside Runtime would
invert §E2.2 and make the activation ledger responsible for knowing its callers. So "publishing wins" is not a
rule the runtime holds; it is a consequence of publishing — and only publishing — passing `TakeOver`, which it
does because a publish is an explicit operator command and the publish pipeline is the only layer that knows
that. The reconciler never passes it, which is the other half: a shell reload cannot quietly revert an
operator's publish.

When reconciliation is refused, `IArtifactForeignOwnerPolicy` decides how loudly to report it. Its answer space
is a closed pair — skip (the default) or reject. **Takeover is deliberately not an answer**, because a policy
that could authorise reconciliation to seize a foreign slot would reopen exactly the failure above.

Reconciliation decides whether to reclaim by reading `slot.Source` **alone** — no journal, no memory of what it
activated last time. That works because deactivation clears ownership (`Source = null`), pinned by
`WorkflowActivationAuthorityTests.Deactivation_clears_ownership_so_the_slot_becomes_claimable_again`. So
**unpublishing is what hands a slot back to a mount**: it makes the slot unowned, and the next reconciliation
pass claims it like any other.

### 6. There is no external deactivation surface

`DeactivateAsync` is an in-process contract method. Its only production caller is publishing's unpublish
handler. Runtime.Api exposes activation slots as **reads only** and has no deactivation request, handler or
endpoint; adding one is a spec change, not a natural extension.

A runtime-only engine therefore has no deactivation surface at all, and that is the intended shape. It
re-reconciles through a shell reload, which re-runs its startup tasks (FR-B-008) — not through an operator
mutating the ledger over HTTP.

## Two consequences that will look like mistakes

### The takeover asymmetry inverts the usual declarative-controller precedence

Terraform and Kubernetes resolve this the other way: the declarative source re-asserts over imperative drift,
because the declared state is the standing intent. Here the imperative act wins permanently and the declarative
source never takes it back. Read against that background it looks like a bug.

It is correct **only because reconciliation seeds rather than enforces**. The reconciler runs once per shell
activation and again on shell reload; between those points it is not watching anything, so an operator's
publish is not drift from a continuously-asserted state — it is the most recent decision, made by a human,
about a slot no one is currently claiming.

**If reconciliation ever becomes continuous, this decision must be revisited.** A continuous reconciler that
still never reclaims would leave a mount permanently unable to serve a definition it declares, with no signal
beyond a warning; a continuous reconciler that *does* reclaim would silently revert operators. Neither is the
present design, and the choice between them is not made here.

### The absence of a default `IDistributedLockProvider` is a safety property

The reconcile pass is a `[SingleNodeTask]` guarded by `IDistributedLockProvider`, and **no default
implementation is registered anywhere in the framework**. This looks exactly like an oversight, and the obvious
fix makes it dangerous: a process-local stand-in satisfies DI, behaves perfectly on one node, and then lets two
nodes reconcile the same mount concurrently — the precise condition the single-node guard exists to prevent.

Absence fails at container validation, at boot, and cannot be shipped past. That is the property being bought.
A host composes any `Elsa.Locking.*` provider; a file-system one suffices for a single host, a multi-node
deployment needs a genuinely distributed one. It is **not** expressed as a `DependsOn` because that would pin
one provider choice, and the design-side reconcilers carry the identical requirement the same way.

## Considered options

- **Move `IPublicationSlotStore` into Runtime under its existing name.** Rejected: it would make the runtime
  responsible for a concept named "Publication", which is the §E2.2 coupling the split exists to prevent.
- **Keep two ledgers and detect conflicts by comparing them.** Rejected: two ledgers cannot disagree about who
  is live, they can only both be locally consistent, so the overlap becomes undetectable rather than merely
  unresolved.
- **Infer ownership from the activation-id prefix.** Rejected: identifiers are opaque, and a parser over them
  converts a formatting change into a silent authority failure.
- **Let publishing keep its own retraction path and document the shared ordering invariant.** Rejected: that is
  what was in place when the two paths drifted. Deletion makes the divergence unrepresentable; a comment only
  records it.
- **Keep the durable projection-intent ledger for restart recovery.** Rejected: the compensating in-process
  sequence leaves nothing half-done, so the ledger has no work to do, and a live-looking public contract with
  no writer is a liability.
- **Teach the authority that publishing outranks reconciliation.** Rejected: it would put a design-side concept
  inside the runtime ledger. The equivalent behaviour is reached by letting the caller declare an intent the
  ledger honours blindly.
- **Give the foreign-owner policy a takeover answer.** Rejected: it would let a deployment turn reconciliation
  into a claimant, which is a hole in the never-reclaim invariant rather than an extension of it.
- **Expose deactivation on Runtime.Api so a runtime-only engine can retract.** Rejected: shell reload already
  re-reconciles, and a mutable ledger endpoint would create a second way to change activation that no publish
  journal records.

## Consequences

- One ledger and one coordinator means a definition has exactly one answer to "what is live and who owns it",
  and one implementation of the ordering invariant that keeps it true.
- A mount and an operator can contend for the same definition, and the loser is told: reconciliation reports a
  named foreign-owner skip at warning level rather than failing silently. An operator who replaces a mounted
  file while publishing holds the slot needs that message to understand why nothing changed.
- Handing a slot back to a mount has exactly one route — unpublish. There is no reclaim command, and none
  should be added without revisiting decision 5.
- Composing artifact reconciliation without a locking feature does not start. That is intentional and should
  not be "fixed" by registering a stand-in.
- Ownership is now durable state, so a persistence provider must carry the `Source` field and its CAS
  semantics; a provider that dropped it would silently make every slot claimable by anyone.

## Linked decisions and evidence

- [ADR 0043 — Publication Slots Define Start Authority](0043-publication-slots-define-start-authority.md)
- [ADR 0038 — Artifact hash is purely behavioral and executables are content-addressed](0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)
- [ADR 0040 — One artifact store with reference-derived lifetime](0040-one-artifact-store-with-reference-derived-lifetime.md)
- [Runtime extension-point catalog](../../src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md)
- [Artifact reconciliation catalog](../../src/Elsa/Workflows/Runtime/Reconciliation/EXTENSION_POINTS.md) and
  [README](../../src/Elsa/Workflows/Runtime/Reconciliation/README.md)
- [Publishing extension-point catalog](../../src/Elsa/Workflows/Publishing/EXTENSION_POINTS.md)
- Implementation record: [spec 151](../../specs/151-executable-artifact-reconciliation/spec.md),
  [issue #1304](https://github.com/elsa-workflows/elsa-foundation/issues/1304)
