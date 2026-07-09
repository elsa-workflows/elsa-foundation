# One artifact store with reference-derived lifetime

The transient test-run executable store (`InMemoryTransientWorkflowExecutableStore`, `WorkflowExecutableScope.TransientTestRun`) conflicts with content-addressed executables (ADR 0038): a test run of a draft behaviorally identical to a published version would mint the same artifact id in a second store, and scope/expiry are per-publish facts living on the artifact. We unify on a single artifact store and move scope and expiry to the Source Reference: a Test Run creates an expiring reference (source: the draft snapshot) instead of a transient artifact. Artifact lifetime is derived — an artifact is retained while any live reference points at it and swept once only expired or retired references remain (dangling-image pruning). Deleting an executable means retiring references; `deletedAt` becomes a reference fact, and the artifact follows by GC.

## Consequences

- `WorkflowExecutableScope` leaves the artifact; dispatch and the test-run handler read scope/expiry from the reference they dispatch through.
- GC is a two-query sweep (delete expired/retired references, then delete unreferenced artifacts), not new distributed machinery.
- Free equivalence signal: when a draft's test run resolves to the same artifact id as the published version, Studio can report "this draft is behaviorally identical to published vN" without any diffing.
