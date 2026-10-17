# Validation run guide

This is the required validation recipe, not a report of checks already passed. Exact commands/results and implementation commit are added during delivery.

1. Build only touched activity runtime, workflow publishing/runtime and EF test projects through the normal dotnet build-slot wrapper.
2. Run focused authoring/serialization tests: Inline legacy hashes, Background hash change, safe capability fingerprint, invalid structure/stateful/intrinsic/value policy rejection.
3. Run the real EF store suite against file-backed SQLite: fresh-context claims, expiry, stale fence, duplicate/conflicting receipt, scope isolation, admission/acceptance rollback, cancellation and checkpoint replay.
4. Run native integration with Sequence → Flowchart → two configured background leaves → join → final step. Hold both bodies at a barrier, release each order and inspect one logical final continuation and correct outputs.
5. Build Workbench and use its existing owned-process test helper, `Essentials/tests/Workbench/Tests/WorkbenchProcess.cs` (the C# counterpart of `e2e-tests/_ServerLifecycle.ps1`). Start isolated coordinator/worker processes against one EF database/scope, with two held worker bodies. Kill only processes launched by the witness. Restart against the same database at admission, claim, receipt and application boundaries.
6. Run explicit cancellation before claim, while both bodies are held, between results and before result application. Replay late completion after restart. Demonstrate shutdown leaves work reclaimable.
7. Run negative controls that remove worker fencing/expiry, pin/scope validation, receipt atomicity/duplicate suppression and native propagation replay guards. Each relevant test must fail. Restore code before final checks.
8. Run affected existing suites, architecture guard, generated maps check and final diff review. If maps change, regenerate the narrow affected maps and commit all changed generated outputs, including manifest. Record baseline main CI #2293 separately.
9. Independent reviewer checks production code and proof. Update #2914, spec tasks, PR and program status with links to exact evidence. Do not mark tests passed if blocked/unrun; do not mark spec Implemented before merge.

Operational documentation must include opt-in feature composition, worker concurrency/lease settings, diagnostic states, eligibility and output limits, schema migration, worker pause/drain, cancellation and rollback of hosts with pinned background artifacts still outstanding.
