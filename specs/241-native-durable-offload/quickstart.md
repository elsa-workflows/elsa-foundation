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

## Self-review corrections — 2026-10-17

CI on `539df7cce` exposed 16 failures beyond the separate main baseline: nine EF command budgets, six schema inventory assertions, and the worker cancellation/receipt race. The earlier local results below remain historical evidence, not proof that this CI head was green.

The reviewed correction source is `69b9f2fab7ff820bafc16d89944779e229e0ee59`. The results below were obtained against those changes, with the narrower EF metadata run following its additional test cases. The architecture run uses the same commit in an isolated checkout with the complete CI dependency graph already restored.

Four review passes, including final root verification, examined the current source, actual CI log and process witness. The correction batch:

- Records a monotonic workflow marker in the admission checkpoint and gates cancellation participants on durable admission evidence. The normal workflow-completed projection preserves that marker. Inline runs no longer query the background ledger on terminal or ordinary activity cancellation.
- Reuses the exact claimed-dispatch context to avoid repeating the drainer's terminal-state query in completion handlers. Direct, unclaimed and mismatched-claim calls retain their terminal replay guard. This addresses the remaining three fork/join reads; no command budget was raised.
- Re-reads a receipt conflict in a fresh persistence scope. A confirmed cancellation is an expected worker no-op; other conflicts still escape. Deterministic barriers cover cancellation between body completion and receipt persistence, as well as stale-claim rejection.
- Adds the new runtime schema family and payload column to the explicit readability, finalization and four-provider column inventories.
- Strengthens the real-process cancellation witness: it captures actual claims before cancellation and submits them through the real EF store in the replacement process. The witness requires the cancellation-tombstone conflict, matching claim identity, retained Cancelled state with no result, and no final continuation. JSON evidence is published by atomic rename.

The second pass caught the terminal projection dropping the new marker and a timing assumption in the new test fixture. Both were corrected. The third independent source review found no further actionable defect. Final verification also corrected the new EF test fixture's registration order: persistence decorators must wrap the selected EF stores. The local loop completed with no remaining actionable finding. Fresh CI and the CLA check on the updated PR remain independent merge gates.

| Correction validation | Result |
|---|---|
| Original EF command budgets | 10/10 passed, budgets unchanged. |
| Full readability suite | 117/117 passed. |
| EF tooling finalization and four-provider payload-column classes | 27/27 passed. |
| Full activity runtime suite | 491/491 passed. |
| Full workflow runtime suite | 2,340/2,340 passed. |
| Full Runtime EF suite | 969/969 passed before the additional balanced-admission assertions. |
| Native EF admission metadata and both completion orders in Durable/Balanced modes | 7/7 passed; admission preserves source reference/correlation and completion retains the background marker. |
| Rebuilt Workbench process witness with post-restart stale receipts | 3/3 passed, including both recovery orders and cancellation followed by late receipt submission from the replacement process. |
| Correction-specific negative controls | Both failed when the production safeguard was removed, then passed after exact restoration: cancellation-conflict handling and terminal marker preservation. |
| Generated maps and solution filters | Passed. The new test file required refreshing only `docs/maps/test-map.md`; the manifest and other maps were unchanged. |
| Architecture guard | 576/576 passed against `69b9f2fab` in the isolated checkout. |

Additional correction checks:

```bash
dotnet test Essentials/tests/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~EfCommandBudgetTests
dotnet test Essentials/tests/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj --no-restore
dotnet test Essentials/tests/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj --no-restore --filter 'FullyQualifiedName~EfToolingFinalizationTests|FullyQualifiedName~ModulePayloadColumnTests'
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-restore --filter FullyQualifiedName~NativeBackgroundActivityFlowchartEfTests
```

For the cancellation control, temporarily disabling the worker's `BackgroundActivityWorkConflictException` catch made `Cancellation_before_receipt_is_treated_as_a_fenced_result_without_waking_the_coordinator` fail with the cancellation-tombstone conflict. For the metadata control, omitting the marker copy in `PreserveSystemMetadata` made `CheckpointHandler_PreservesPinnedRunKindAndBackgroundAdmissionMarkerThroughCompletion` fail because the marker was absent. Each test passed after restoration. These controls supplement the original ten controls below.

## Intermittent CI follow-up — 2026-10-17

[CI run 39064697466](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39064697466/job/117489778092) at `b224d180b` no longer reports the previous 16 failures. Runtime EF passed 971/971. The remaining failure is the right-first Workbench recovery case timing out while waiting for the real receipt marker. Architecture and core-only downstream jobs were skipped; their local evidence above is not a claim that those CI jobs passed.

The existing three process cases passed locally without code changes. That result does not explain or resolve the intermittent CI failure. Independent source investigation found no demonstrated journal-path or hold/release ordering defect. An in-flight renewal can delay a completed body's receipt, but the failed run provides insufficient evidence to attribute this timeout to that mechanism.

The follow-up adds test-fixture diagnostics only: a marker for passing the body release gate, latest renewal/receipt observations isolated by workflow/work/claim identity, and a bounded journal snapshot on timeout. The original `.receipt` commit witness is written before diagnostic committed-stage output. The 90-second timeout, production worker/store behavior and recovery assertions are unchanged. Diagnostic filesystem failures do not replace activity/store outcomes. These observations add file I/O and can affect timing; an `entered` stage alone does not prove that provider I/O is stalled, because a later diagnostic write may itself be delayed. The body-released marker precedes transition serialization and disposal.

Diagnostic source revision: `7222eff8fa4fdefe46335ff9c4b70e3419261ff4`. The final rebuilt background-recovery class passed 3/3; the unchanged class at `b224d180b` also passed 3/3. During diagnostic development, the full Workbench suite passed 39/42, including all three background-recovery cases. Its failures were:

- `CompositionActivationObservationTests.Failed_candidate_reports_the_retained_ready_generation_without_error_details`: expected `reload-failed`, received `ready`.
- `CompositionHostAttestationProbeTests.All_six_bundle_files_and_process_override_have_no_generation_bound_marker`: the invalid candidate did not report reload failure.
- `CompositionHostAttestationProbeTests.Shell_reload_can_advance_while_startup_bound_host_configuration_stays_old`: the reloaded blueprint did not contain the new marker `402`.

All three also failed individually on this Mac against the final build. They use separate processes/content roots and do not enable the background fixture. Their common fixed 1.2-second wait for JSON file-watcher propagation is an investigation lead, not an established cause. Machine load varied substantially, and neither parallel load nor this diagnostic change is established as the cause. These failures are retained as separate local observations; the full suite is not reported green. They were not failures in CI run 39064697466.

Generated maps, solution filters and the final diff check passed. Two independent investigations and a follow-up source review found no demonstrated cause of the original CI receipt timeout. The diagnostic review corrected the placement of the commit witness, isolated observations by claim identity, and documented observation limits. Fresh CI is tracked on PR #2915. The intermittent failure remains under investigation until its cause is evidenced; a passing rerun alone is not a causal fix.

## Original delivery evidence — 2026-10-17

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
