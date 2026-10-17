# Validation run guide

The recipe below is followed by the recorded delivery evidence. A pending row is not a passed gate.

1. Build only touched activity runtime, workflow publishing/runtime and EF test projects through the normal dotnet build-slot wrapper.
2. Run focused authoring/serialization tests: Inline legacy hashes, Background hash change, safe capability fingerprint, invalid structure/stateful/intrinsic/value policy rejection.
3. Run the real EF store suite against file-backed SQLite: fresh-context claims, expiry, stale fence, duplicate/conflicting receipt, scope isolation, admission/acceptance rollback, cancellation and checkpoint replay.
4. Run native integration with Sequence → Flowchart → two configured background leaves → join → final step. Hold both bodies at a barrier, release each order and inspect one logical final continuation and correct outputs.
5. Build Workbench and use its existing owned-process test helper, `Essentials/tests/Workbench/Tests/WorkbenchProcess.cs` (the C# counterpart of `e2e-tests/_ServerLifecycle.ps1`). Start an owned coordinator-only host against one EF database/scope, then enable two held worker bodies on restart. Kill only processes launched by the witness. Restart against the same database at admission, claim, receipt and application boundaries.
6. Run explicit cancellation before claim, while both bodies are held, between results and before result application. Replay late completion after restart. Demonstrate shutdown leaves work reclaimable.
7. Run negative controls that remove worker fencing/expiry, pin/scope validation, receipt atomicity/duplicate suppression and native propagation replay guards. Each relevant test must fail. Restore code before final checks.
8. Run affected existing suites, architecture guard, generated maps check and final diff review. If maps change, regenerate the narrow affected maps and commit all changed generated outputs, including manifest. Record baseline main CI #2293 separately.
9. Independent reviewer checks production code and proof. Update #2914, spec tasks, PR and program status with links to exact evidence. Do not mark tests passed if blocked/unrun; do not mark spec Implemented before merge.

Operational documentation must include opt-in feature composition, worker concurrency/lease settings, diagnostic states, eligibility and output limits, schema migration, worker pause/drain, cancellation and rollback of hosts with pinned background artifacts still outstanding.

## Delivery evidence — 2026-10-17

Tracked by [#2914](https://github.com/elsa-workflows/elsa-foundation/issues/2914) and [PR #2915](https://github.com/elsa-workflows/elsa-foundation/pull/2915). The integrated production revision is `6648fd041`; the recorded source revisions below distinguish the EF/host proof from the later in-memory parity correction. Temporary mutation edits were restored in isolated worktrees before integration.

| Check | Recorded result |
|---|---|
| Full activity runtime suite, `21cc902e4` | 485 passed, 0 failed. Later production changes are confined to EF/in-memory admission/deletion and a Core metadata key; those changes have their own full affected suites below. |
| Full publishing API suite, `21cc902e4` | 776 passed, 0 failed; includes metadata enrichment, template placement, capability, hash and validation regressions. |
| Full workflow runtime suite, `6648fd041` | 2,338 passed, 0 failed, including the final in-memory initial-admission/deletion parity regressions. |
| Full Sequence and Flowchart suites, `f363bd575` | 22 and 121 passed respectively, 0 failed. |
| Full Runtime EF suite, `c39018d06` | 969 passed, 0 failed. Includes the final stale-old-worker assertion; the subsequent production change affects only the in-memory implementation. |
| First-checkpoint admission and EF composition, isolated `c9d455792` (integrated as `98eaa36d4`) | 54 passed across the two affected EF classes. Coalesced first Upsert plus admission succeeds; missing state and deleted identity after reacquisition reject admission. Both execution-state contracts resolve to EF stores. |
| Rebuilt Workbench process journey, `f363bd575` | 3/3 passed: both completion orders and explicit REST cancellation across restart. This run includes the coalescing regression correction in `98eaa36d4`; all host restarts use the same rebuilt production binaries. |
| Architecture guard, `6648fd041` | 576 passed, 0 failed. The earlier fixture-folder failure is corrected; the earlier feature-composition database-lock failure did not recur. |
| Generated maps and solution filters | Both final local checks passed after refreshing the task-status map included with this evidence record. Both also passed in CI at `f363bd575`. Earlier CI found the Workbench solution filter missing the fixture; the refresh is included. |
| Independent review | Production worker, coordinator, codec, EF lifecycle and process witness reviewed in separate lanes. Review caught the in-memory deletion/recreation parity gap; `6648fd041` fixes it. Root review also retained rejection when the workflow-state store is absent. The final EF/tombstone and Workbench witness review found no further concrete defect. |

Run the affected suites from the repository root through the normal build-slot wrapper:

```bash
dotnet test Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-restore
dotnet test Essentials/tests/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
dotnet test Essentials/tests/Activities/Sequence/Tests/Elsa.Activities.Sequence.Tests.csproj --no-restore
dotnet test Essentials/tests/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj --no-restore
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-restore
dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj --no-restore --filter 'FullyQualifiedName~WorkbenchBackgroundActivityRecoveryTests'
bash tools/architecture/restore-ci-project-graph.sh --locked-mode
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
```

The Workbench project reference rebuilds the actual host and the ordinary test-activity package. Each journey owns its temporary database, package version, journal and processes. The test kills only those owned process trees and restarts the same build against the same database. It covers admission before a consumer starts, interrupted bodies, a receipt committed before notification, terminal history, and REST cancellation while both bodies are held. Barriers establish overlap and ordering; elapsed time is a bounded wait, not a throughput measurement.

### Negative controls

| Production control removed temporarily | Failing observation | Restored result |
|---|---|---|
| Coordinator ownership fence staging | `Old_coordinator_fence_cannot_accept_ready_receipt_or_write_native_participants_after_successor_acquires_ownership` accepted a stale-owner commit instead of throwing. | All five checkpoint-atomicity tests passed. This control predates the additional admission fence-presence guard. |
| Awaiting the committed renewal response before receipt recording | `Completed_body_waits_for_an_in_flight_renewal_to_publish_before_recording_receipt` failed because the receipt used the stale claim. | 1/1 passed with `067c6a128` integrated. |
| Pinned executable validation | `Mismatched_pinned_executable_hash_is_rejected_before_activation` activated the body (expected 0 executions, actual 1). | 1/1 passed; mutation performed at isolated `e018c31b4`. |
| Active EF persistence scope (`Scope()` forced to tenant A) | Tenant B read tenant A's accepted row in `Background_admission_receipt_replay_and_terminal_acceptance_are_checkpoint_scoped`. | 1/1 passed after restoration. A separate fixture-only scope mutation is excluded from this production-control evidence. |
| Native background cancellation participant | `Cancellation_with_both_background_bodies_held_tombstones_both_claims` observed ResultReady instead of Cancelled. | 1/1 passed after restoration. |
| Terminal workflow completion replay guard | `CompletionReplay_AfterWorkflowCheckpointIsTerminal_DoesNotReenqueueEitherStage` unexpectedly enqueued another WorkflowCompleted checkpoint. | 1/1 passed after restoration. |
| Same-claim canonical receipt equality | Conflicting content no longer threw in `Background_admission_receipt_replay_and_terminal_acceptance_are_checkpoint_scoped`. | 1/1 passed after restoration. |
| Receipt expiry boundary (`>=` changed to `>`) | `Background_expired_claim_cannot_record_a_result_but_successor_can` no longer threw at exact lease expiry. | 1/1 passed after restoration. |
| Exact active worker claim | The same expiry/successor test accepted the old worker's receipt after a successor had claimed the work. | 1/1 passed after restoration; strengthened assertion integrated as `c39018d06`. |
| Receipt acceptance transaction rollback | Inserting `transaction.CommitAsync()` before the generic exception handler's rollback made `Marker_failure_rolls_back_receipt_acceptance_activity_state_and_continuation_intent_together` find Accepted instead of ResultReady after a marker failure. | 1/1 passed before the mutation and 1/1 after restoration; the temporary copy of the existing root test and all production mutations were removed. |

### Limits

This is a bounded native production-path proof, not a claim that every runtime replay issue is closed. In particular it does not close the broader late-completion work in #2232. Bodies remain physically at least once, so external side effects need idempotency. The process witness runs concurrent worker bodies in one host at a time and proves recovery in replacement processes; it does not establish simultaneous multi-host placement or actor-provider substitution. SQLite provides the exercised durable database; provider model/migration checks do not establish live SQL Server, PostgreSQL or MySQL fault-recovery evidence. Initial migrations follow the documented pre-release regeneration policy and do not promise an in-place database upgrade. Main CI issue #2293 remains a separate baseline report; unrun or failing CI checks are not counted as passed.
