# One Artifact Store with Reference-Derived Lifetime

Status: accepted (2026-07-10; ratified in the same grilling session as ADR 0038)
Amended: 2026-07-13 by spec 092 to include retained workflow-execution roots and race-safe GC
Plan of record: `docs/plans/content-addressed-executables-and-inspector.md` and
`specs/092-domain-owned-apis/`

The transient test-run executable store is retired: all executables live in a single content-addressed
store, scope and expiry move to the Source Reference, and artifact lifetime is derived from reference
liveness plus retained workflow-execution roots.

## Context

The transient test-run executable store (`InMemoryTransientWorkflowExecutableStore`,
`WorkflowExecutableScope.TransientTestRun`) conflicts with content-addressed executables (ADR 0038): a
test run of a draft behaviorally identical to a published version would mint the same artifact id in a
second store ("one id, two objects"), and scope/expiry are per-publish facts living on the artifact —
exactly the category ADR 0038 moves to references.

The original reference-only rule is insufficient once a workflow execution has pinned an executable.
Publication or test-run references control whether new executions may start, but an existing execution
must continue to inspect and resume from its exact pinned artifact after those references are retired.
Deleting that artifact would leave otherwise valid continuation state permanently unresolvable.

Artifact creation and root creation are also separate durable writes in some providers. A collector that
checks for roots and then deletes without a grace period or final conditional recheck can race a publish,
test run, restore, or execution checkpoint and remove an artifact while it is becoming reachable.

## Decision

Unify on a single artifact store and move scope and expiry to the Source Reference. A Test Run creates
an expiring reference (source: the draft snapshot) instead of a transient artifact.

Artifact lifetime is derived from the union of two durable root sets:

1. artifact IDs named by live Source References; and
2. artifact IDs pinned by retained workflow-execution records.

The workflow-execution record itself is the retention root. Elsa does not create a duplicate execution
Source Reference. The root applies for every retained execution status, including pending, running,
suspended, completed, canceled, and faulted. Completion does not release the root; removal under the
workflow-execution retention policy does.

Runtime persistence must expose a provider-efficient distinct pinned-artifact query. Loading every full
workflow-execution record into application memory during each sweep is not an acceptable implementation
of that contract.

Deleting an executable through ordinary lifecycle operations means retiring its publication or test-run
references. Physical deletion follows only through GC after no live reference and no retained execution
points to the artifact.

GC must additionally protect artifacts inside a configurable creation/staging grace period and close the
check-then-delete race with a final conditional root check, provider transaction, or equivalent deletion
guard. A root created concurrently with a sweep must win over deletion. Expired or retired reference
records may be pruned independently; trigger projections do not count as artifact-retention roots.

## Considered Options

- Keep two stores with segregated transient and durable artifacts. Rejected because the same artifact
  id could exist in both, undermining the content-addressed model, and scope would remain
  artifact-level state.
- One store with reference-carried scope/expiry and derived artifact lifetime. Accepted because it
  repeats the ADR 0038/0039 move — per-publish facts belong on the reference — and turns the transient
  store into a retention policy.
- Mirror every workflow execution as another Source Reference. Rejected because the retained execution
  record already pins the exact artifact and is the lifecycle authority; duplicating it creates a second
  record that can drift and confuses start authority with continuation retention.
- Keep reference-only GC and rely on executions to finish before references retire. Rejected because
  completed executions remain inspectable, suspended executions may resume later, retention duration is
  independent of publication duration, and failures or administrative unpublish can retire references at
  any time.

## Consequences

`WorkflowExecutableScope` leaves the artifact; dispatch and the test-run handler read scope/expiry
from the reference they dispatch through.

GC is no longer correctly described as a reference-only two-query sweep. It may still prune references
and artifacts in separate phases, but artifact eligibility is computed from live references, retained
execution pins, and creation grace, followed by a race-safe final deletion check.

`IWorkflowExecutionStateStore` (or a narrower Runtime-owned query seam) must support distinct retained
artifact IDs without materializing every execution. Persistence implementations must keep that query
consistent with saving and removing workflow-execution records.

Collectors must be conservative when a retention-root query or conditional deletion check fails: an
artifact remains stored and the sweep is retried. This can temporarily leak storage but cannot break a
retained execution.

Free equivalence signal: when a draft's test run resolves to the same artifact id as a published
version, Studio can report "this draft is behaviorally identical to published vN" without any diffing.
