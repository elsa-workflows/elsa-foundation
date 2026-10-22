# Container Output Captures Resolve Through Producer-Visible Variable Frames

Status: accepted (2026-07-23; approved for [issue #1004](https://github.com/elsa-workflows/elsa-foundation/issues/1004))

This decision refines the accepted output-capture slice of
[ADR 0046](0046-output-binding-coercion-uses-pinned-value-representations.md), operationalizes the
container-frame identity described by [ADR 0027](0027-scoped-variable-references-include-declaring-scope.md),
and remains constrained by the atomic-result and causal-value-flow rules in
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md).
It records an accepted architecture decision within the draft constitution's provisional gates; it
does not ratify or amend the constitution. ADR 0027 remains proposed; this decision accepts only the
stable declaration address, per-activation frame identity, and ancestor-visibility rules needed for
output-capture targets rather than ratifying ADR 0027 wholesale.

## Context

The first activity output-capture authoring slice lets an author bind a named activity-result
projection to a workflow-scope variable. Publication compiles that edge into a
`RuntimeOutputCapture` whose `ValueId` names the workflow variable, and runtime applies the encoded
value to the workflow execution's canonical root variable frame in the same checkpoint as the
activity completion. That target is unambiguous because one workflow execution owns one root frame.

A container-scoped variable has two different identities:

- the authored declaration address: declaring scope node identity plus stable variable reference key;
- the runtime storage owner: one concrete activation's variable frame.

One authored container can activate repeatedly, recursively, or concurrently. A loop body can
activate once per iteration, and parallel branches can either own distinct frames or share a visible
ancestor frame. Publication therefore cannot replace a container declaration address with one
concrete frame identity: those frames do not exist until runtime. Reusing a root-style `ValueId`
would also collapse shadowed declarations and repeated activations onto one apparent destination.

The runtime must resolve a capture relative to the activity invocation that produced the result. A
global search for the latest matching container execution would violate ADR 0045's causal-lineage
rule and would become nondeterministic under loops, retries, and parallel completion.

The earlier [spec 060](../../specs/060-runtime-activity-output-capture/spec.md) and
[spec 061](../../specs/061-runtime-activity-input-resolution/spec.md) remain superseded where they
model independently writable output slots or execution-local memory references. This decision
preserves only their durable-availability and deterministic-failure objectives. An activity still
completes with one atomic result; named outputs are read-only projections from that result.

## Decision

### 1. Keep output capture as an explicit engine-owned binding edge

An authored output capture is an explicit, inspectable edge from one named projection of an
activity's atomic result to a variable declaration. It is graph-visible through the authored node
and pinned executable even when Studio presents it inline in the activity inspector.

The edge is not:

- an independently writable activity-output slot;
- an activity-code or expression side effect;
- an ambient name-based variable write; or
- a second variable persistence route.

Publication applies the same type, representation, conversion, protection, and persistability gates
used by workflow-scope captures. Runtime reads only the committed completion projection selected by
the pinned edge.

### 2. Pin a declaration-relative variable target, not a concrete frame or overloaded `ValueId`

The executable target contract for a variable capture carries, at minimum:

- the target variable's stable reference key;
- the authored declaring scope identity, using the workflow-scope sentinel for the root frame; and
- an explicit variable-target kind so runtime does not infer variable semantics from a `ValueId`
  prefix or optional metadata.

The exact runtime type and serialized property names belong to the implementing specification, but
the semantic shape is a role-specific target such as
`RuntimeVariableWriteTarget(DeclaringScopeId, VariableKey)`.

Publication never pins a runtime frame ID, activation ID, activity execution ID, or iteration ID for
a container target. Those are runtime facts. A legacy workflow-root `ValueId` may remain readable
for already-published artifacts, but new container semantics must not encode a relative target into
an ambiguous string convention.

The target participates in executable inspection and the behavioral artifact fingerprint. Moving
the producing node outside the target's lexical visibility invalidates the authored edge rather
than silently rebinding it to a same-named variable.

### 3. Resolve the target through the producer invocation's visible causal lineage

When the producing activity completes successfully, runtime resolves the pinned declaration address
against the active variable frames visible to that concrete producer invocation:

1. The workflow-scope sentinel resolves only to that workflow execution's active root frame.
2. A container scope identity resolves only through the producer's structural ancestry and visible
   frame chain.
3. Resolution must yield exactly one active frame that owns the declared variable key.
4. A missing, closed, unrelated, sibling-only, multiply matched, or undeclared target fails
   deterministically.

Runtime must not scan all activity executions for a matching scope, select the globally latest
activation, or fall back to a same-named variable in another frame. Retries and resumptions retain
the logical invocation and its causal ancestry, so redelivery resolves the same semantic target or
observes that the completion was already committed.

This is the output-capture counterpart of the existing `Set` intrinsic's explicit scoped target
resolution. The implementation should share a role-appropriate frame-resolution primitive rather
than duplicate ancestry rules in each writer.

### 4. Commit the frame mutation atomically with the producing completion

After conversion and storage encoding, runtime applies the value to an immutable successor revision
of the resolved frame. The changed workflow state or container-owner activity state participates in
the same runtime checkpoint commit as:

- the producing invocation's successful completion;
- its atomic result and named projections;
- the authored outcome; and
- any other capture state owned by that completion.

A container-variable capture updates the canonical variable frame owned by the concrete container
activation. It does not create a `DurableValueState` row as a shadow variable store. Frame revision
checks remain the runtime defense against stale concurrent mutation; conflict handling must preserve
the checkpoint store's existing retry and convergence semantics.

### 5. Enforce lexical, iteration, and concurrency safety at publication

Publication accepts a container target only when the declaration is lexically visible from the
producing node and the target kind is writable:

- descendants may capture into a visible mutable ancestor container variable;
- sibling, descendant-only, or unrelated scopes are invalid targets;
- loop-owned current-item and index entries remain read-only under
  [ADR 0028](0028-loop-body-runs-in-a-per-iteration-variable-scope.md) and cannot be capture targets;
- a mutable loop-local declaration uses the normal container-frame model and receives the frame of
  the concrete visible activation;
- potentially concurrent captures into the same shared frame variable fail publication unless an
  explicit deterministic merge or reduction governs them; and
- data leaving a structured scope still uses an explicit typed return, collection, selection,
  merge, or reduction. Capturing into an ancestor variable is an explicit state mutation, not an
  implicit scope-return mechanism.

Runtime repeats the visibility, ownership, declaration, active-frame, and revision checks as
defense in depth. It does not use completion timing as a conflict-resolution policy.

### 6. Expose only semantically eligible targets in authoring

Studio may remove the workflow-only restriction only after the backend target contract and
validation are available. The picker offers writable variables visible from the selected activity's
lexical position; it excludes read-only iteration entries and targets rejected by static
concurrency analysis.

Existing invalid references remain visible in repair and diagnostic surfaces so moving a node does
not silently discard or retarget an authored decision.

## Alternatives considered

### Keep output capture workflow-scope-only permanently

This remains a valid product subset and the compatibility fallback, but it was rejected as the final
architecture because lexical container frames already provide a deterministic owner when resolution
is relative to the producer invocation.

### Pin one concrete container execution during publication

Rejected because concrete activations do not exist during publication and one declaration may
produce many valid runtime frames.

### Select the latest matching container execution at runtime

Rejected because completion order is not causal identity. The result would be nondeterministic under
parallelism, loops, retries, and resumptions and would violate ADR 0045.

### Store container captures as ordinary durable values

Rejected because it would create a second variable truth beside the canonical frame, lose lexical
liveness and close semantics, and require later reads to reconcile two stores.

### Lower every capture to a separately scheduled `Set` node

Rejected as the canonical representation. It would change graph topology, failure timing, and the
atomic completion boundary. Output capture remains a first-class visible binding edge whose frame
mutation uses the same scoped-write semantics as `Set` within the producing completion checkpoint.

## Consequences

- `RuntimeOutputCapture` needs a first-class destination contract capable of representing a relative
  variable target; `ValueId` and metadata alone are insufficient for new container semantics.
- The runtime output-capture projection must produce variable-write intents for root and container
  destinations, and checkpoint composition must be able to carry the changed frame owner in the
  producing completion commit.
- The scoped frame resolver should become a shared runtime primitive used by `Set` and output capture
  without exposing authored Design models to Runtime.
- Publication validation must add lexical visibility, writable-target, shadowing-address,
  concurrency, and merge/reduction checks for capture edges.
- Executable inspection and hashing must expose and fingerprint the relative target.
- Studio must filter by semantic eligibility rather than merely dropping `workflowScopeOnly`.
- Existing workflow-scope capture artifacts and behavior remain valid.

## Required implementation evidence

The implementing Speckit unit must cover:

- repeated activations of one container declaration without cross-instance writes;
- nested containers and explicit ancestor targeting;
- same-named shadowed variables addressed by stable scope and reference key;
- sequential loop iterations and parallel iterations;
- rejection of loop-owned read-only item/index targets;
- shared-ancestor parallel capture rejection without merge/reduction;
- retry, resume, redelivery, stale-frame revision, and already-committed completion behavior;
- closed, invisible, sibling, missing, and undeclared frame failures;
- inline, explicit-null, and externalized encoded values without a shadow variable store;
- atomic completion-plus-frame mutation and unchanged workflow-root capture compatibility;
- executable inspection and behavioral-hash sensitivity; and
- Studio round-trip, target filtering, and invalid-reference repair behavior.

## Follow-up

- Plan and implement this decision as a focused Runtime Execution Seam Speckit unit.
- Reconcile the remaining useful test objectives from specs 060/061 without restoring their
  superseded active-output or memory-reference mechanisms.
- Update issue #1004 with the committed ADR/implementation links when the branch is published.
