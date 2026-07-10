# One Artifact Store with Reference-Derived Lifetime

Status: accepted (2026-07-10; ratified in the same grilling session as ADR 0038.
Plan of record: `docs/plans/content-addressed-executables-and-inspector.md`.)

The transient test-run executable store is retired: all executables live in a single content-addressed
store, scope and expiry move to the Source Reference, and artifact lifetime is derived from reference
liveness.

## Context

The transient test-run executable store (`InMemoryTransientWorkflowExecutableStore`,
`WorkflowExecutableScope.TransientTestRun`) conflicts with content-addressed executables (ADR 0038): a
test run of a draft behaviorally identical to a published version would mint the same artifact id in a
second store ("one id, two objects"), and scope/expiry are per-publish facts living on the artifact —
exactly the category ADR 0038 moves to references.

## Decision

Unify on a single artifact store and move scope and expiry to the Source Reference. A Test Run creates
an expiring reference (source: the draft snapshot) instead of a transient artifact. Artifact lifetime
is derived — an artifact is retained while any live reference points at it and swept once only expired
or retired references remain (dangling-image pruning). Deleting an executable means retiring
references; `deletedAt` becomes a reference fact, and the artifact follows by GC.

## Considered Options

- Keep two stores with segregated transient and durable artifacts. Rejected because the same artifact
  id could exist in both, undermining the content-addressed model, and scope would remain
  artifact-level state.
- One store with reference-carried scope/expiry and derived artifact lifetime. Accepted because it
  repeats the ADR 0038/0039 move — per-publish facts belong on the reference — and turns the transient
  store into a retention policy.

## Consequences

`WorkflowExecutableScope` leaves the artifact; dispatch and the test-run handler read scope/expiry
from the reference they dispatch through.

GC is a two-query sweep (delete expired/retired references, then delete unreferenced artifacts), not
new distributed machinery.

Free equivalence signal: when a draft's test run resolves to the same artifact id as a published
version, Studio can report "this draft is behaviorally identical to published vN" without any diffing.
