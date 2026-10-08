# Spec 123 corrective evidence — 2026-10-08

Root accepts the local C003–C007 evidence below for corrective issue #2497. The final focused, six affected-project, rebuilt-host and architecture checks passed on the pinned candidate. Map freshness passes after the scoped generated-map refresh. PR/native-provider/resulting-main delivery gates remain pending. Artifact names identify retained control-room evidence; SHA-256 values support comparison with the original receipts. The adjacent [JSON receipt](cap-recovery.json) carries structured outcomes.

## Candidate source pins

| File | SHA-256 |
|---|---|
| `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs` | `88f0783aa63bae63c014708a160751d27d38158261b5ad1b24693f42f3200a06` |
| `src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingSession.cs` | `788abc8d45ee1ea9679a038f6b4bfd3ba83c40e690bb14e2e15c9899b9d160b1` |
| `src/essentials/Workflows/Runtime/Services/WorkHandlers/ReplaySafeFusionDriver.cs` | `786ae3c6bae9a2acbad27089e007de3c238ff4181e333a400021385b7410327f` |
| `src/essentials/Workflows/Runtime/Services/WorkHandlers/WorkflowScheduleActivitySchedulerWorkHandler.cs` | `77a39c4ed199ed069fb40b552dec062595fc9627d66377d43235ecfd29e3caee` |
| `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfReplaySafeFusionCapRecoveryTests.cs` | `aa9bbc880d7bcddfe561aa44939bd744cad0c8fc8deab9df426ce5c1e434e7e8` |

The candidate preserves the pre-existing public `AdvanceInnerQueueAsync(bool, CancellationToken)` signature. Its internal three-argument core carries the new anchor-preservation option. An independent source review accepted this compatibility wrapper; final focused and suite receipts include these pins.

## Before/fix evidence

The ActivityStarted cap-fold defect was reproduced against unchanged production source: the fresh-provider recovery test expected `Completed` and observed `Running`. This was an executed recovery assertion, not a build or fixture failure. Artifacts: `baseline-activitystarted/baseline-restore.log` SHA-256 `3d75145da2daee5f01478b9145426a3c17347f7c7da6030cb87b16a9dc4c4cf4`; `baseline-activitystarted/results/baseline.trx` SHA-256 `7d5ece5e8972a25f9978cad58dc65c7cf71bfb5efc97347c6e58dcd24e7cfd0c`.

The queue-order control also has a red/green pair. Before the correction, same-time out-of-order continuation insertion caused the durable dequeue to return the wrong FIFO head. The corrected provider-order reconciliation passed the same test. Red log/TRX: `queue-order-red3.log` SHA-256 `605c469881f65fad6bd9872fa506d69a4aea2dddea557fecdf2f8403dbee2fb7`; `queue-order-red3/queue-order-red3.trx` SHA-256 `fa57125cbb6cfbaa1874852edbb6330c86285d0276db76e6758e1b78bc8096fb`. Green log/TRX: `queue-order-green.log` SHA-256 `9f5b897bdbe090c18b37d0d5cf81a9db1b6d76ed4f09f72dfde791d1b990c3fa`; `queue-order-green/queue-order-green.trx` SHA-256 `ff5780c59481bee684b4c13c42711048588cbfc004fa14a956f5f0224558da7a`.

A separate unchanged-main control used the pinned 14-case fixture and failed four anchor-presence assertions at the pre-backup boundary: ActivityStarted and nested D2 at inner-store-return and decorator-return. The nested case retained only the current child rather than the parent and earlier successor anchors. These are missing-anchor bite checks, not four additional recovery-stall claims. Receipt `root-unchanged-main-cuts-v1/receipt.json` SHA-256 `2b53bcedebfa93cdebce86feea709cfbbbc36eec69d75438864d06e3c9775a13`; TRX SHA-256 `60bfd0ddcd1860aba3334ee653ba79b269785c1234fd14868d288730d349f891`; disposition SHA-256 `7b22d3883611145c4e797cc8bc5d738dc320a0c701ec902381c3b6cf52b824e2`.

## Focused final matrix

The pinned focused command was `dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-restore -m:1 --filter 'FullyQualifiedName~EfReplaySafeFusionCapRecoveryTests' --logger 'trx;LogFileName=matrix.trx'`. The final run passed 15/15 (zero failed or skipped). Receipt `root-focused-matrix-v9/receipt.json` SHA-256 `f7358f27d5d8b854ff589b106fd4db2800a53ad896b2e9ca87d5e979f4e345aa`; TRX SHA-256 `386c3bea212c42bdf82fcbec857499ee02a0a18841d1891417e18a1ff03635fb`; log SHA-256 `40838829ea2d91be7a5e99a9db4fe1a5080e57f4f7c0a54e19ca58b98ba2a380`.

Ten recovery cases cover five cut points at both the inner durable-commit return and coalescing-decorator return: cap-two `ActivityStarted`; cap-one `ActivityAttemptClaimed`; cap-one typed ReplaySafe and fusable intrinsic `ActivityScheduled`; and nested D2 `ActivityCompleted` occurrence 2. Each uses a fresh provider and normal resumption without manual work injection. Five controls cover out-of-order/same-time queue reconciliation; an under-cap span without a mid-span durable boundary; anchor-enqueue failure; anchor-enqueue cancellation; and a later inner checkpoint failure.

The last control verifies the durable Schedule anchor both when the failing ActivityStarted commit is attempted and after the exception unwinds the coalescing commit store, before scheduler fault handling runs. At that observation boundary the anchor is still present and any observed activity statuses remain `Scheduled`; the state may still be buffered, so an empty status collection is allowed. After the runner handles the injected failure, the test separately verifies the queue is acknowledged and one ScheduleActivity is recorded as poisoned. This is a failure-unwind observation, not a simulated process crash.

A prior v8 test run passed 14 and failed its new failure case because it looked for the anchor only after `RunAsync`, when the normal handler-fault ack/poison path had removed it. Retained v8 receipt SHA-256 `4a0c84c7d3f3f38124657fafd7c4719f76b26959944403170197e9dcd35b89bf`; TRX SHA-256 `ee20766017cd697b797e766eb09867af1215ff2679588c827ca7e725763aed65`; log SHA-256 `86905424cb97ce34aa57e04b98603a294b916c7bbe1ea02e84d0099e06a8f7f2`; boundary/failure disposition SHA-256 `3fb60b03175bc24c5aa7762715e3cc94565d21f5ba686034743dbd73ab6c218a`. The failure was reclassified from source evidence: the runtime drainer acknowledges and poisons a handled handler fault; that is distinct from an abrupt process interruption. The v9 observer samples before that handling and passes.

The retained earlier nested-recovery failure was a fixture identity collision: a deterministic fresh harness reused an activity ID already owned by its parent Sequence, producing poison. The corrected fixture uses a disjoint recovery identity namespace. This earlier failed attempt is not evidence of another product defect. `nested-recovery-v1/results/nested-recovery-v1.trx` SHA-256 `c4d06bfbdb09edbbbd408d9fb8f383e08be3b743a2f0f74863daddfdebfcae30`; disposition note `nested-fixture-collision-disposition.md` SHA-256 `61578b2a0db159fe4f3be01e59764b5d1277de9d80174d055bdbf33cd8efd8e5`.

## Queue-operation observations and limits

The ten named boundary observations recorded enqueue, `ListAsync` page-call, and dequeue deltas. They are scheduler-queue API and page-call counts, not SQL command counts, physical round trips, query-count guarantees, or timing measurements:

| Boundary | Capture point | Enqueue | List pages | Dequeue |
|---|---|---:|---:|---:|
| ActivityStarted, cap 2 | Inner return | 1 | 1 | 0 |
| ActivityStarted, cap 2 | Decorator return | 1 | 1 | 1 |
| ActivityAttemptClaimed, cap 1 | Inner return | 0 | 0 | 0 |
| ActivityAttemptClaimed, cap 1 | Decorator return | 0 | 0 | 0 |
| Typed ActivityScheduled, cap 1 | Inner return | 1 | 1 | 0 |
| Typed ActivityScheduled, cap 1 | Decorator return | 1 | 1 | 1 |
| Intrinsic ActivityScheduled, cap 1 | Inner return | 1 | 1 | 0 |
| Intrinsic ActivityScheduled, cap 1 | Decorator return | 1 | 1 | 1 |
| Nested D2 ActivityCompleted occurrence 2 | Inner return | 0 | 0 | 0 |
| Nested D2 ActivityCompleted occurrence 2 | Decorator return | 0 | 0 | 0 |

The under-cap control observed no additional ScheduleActivity/StartActivity/InvokeActivity enqueue for its tested span without an intervening durable boundary. At a continuing boundary, missing anchors may require an enqueue and a refresh through the provider's paged queue listing. These bounded observations do not establish constant database cost, general SQL reduction, latency improvement, or performance under other workloads.

## Affected suites and earlier integration evidence

The final affected-project receipt passed 3,540/3,540 tests with zero failures or skips: Runtime 2,125; Resumption 21; Runtime EF 919; Activities Runtime 352; Sequence 18; Flowchart 105. Receipt `root-full-affected-v2/receipt.json` SHA-256 `a5cd6291557354dfc86fd022a33c30dab7edeb18de0661d33a3b3e5f7e4169d3`. TRX hashes: Runtime `d94c7f3116be7534fb7bcc533ad1b60d1a82421b3ac176bde737673d48f91728`; Resumption `3663bf868e30bb135c6c4d9f9fabeb8c95476b5bedb5c845ff27b382cef848fa`; Runtime EF `a134819f5561849bd08ba73077460ce11cd9292c7989e162769c639130fe8caf`; Activities Runtime `5aa5f72b2798ed3cc1bfe262872eccc378a046e67d364d3a77d752f479c9d112`; Sequence `691e982bcb4febbffa7bd168053d42053e4a2a15ffa481a55c3ed17dd062872a`; Flowchart `0b9739bd3a273f03921caf7b25a6f30e38265410bc22a277e0727f9c9f5584a7`.

The final candidate Workbench build passed, then a fresh isolated Coalesced cap-two host passed HTTP methods, HTTP echo, Sequence, the valid REST companion and WorkflowFlow. The host was stopped and its isolated database retained. Final architecture verification passed 635/635 after the canonical dependency-graph restore prerequisites. These final outcomes supersede the earlier-pin build/architecture results for local acceptance; those earlier artifacts remain retained.

An earlier backend attempt passed HTTP methods, HTTP echo, and Sequence, then failed `single-outcome/Test-SetOutput.ps1` with HTTP 400. Root reproduced the same failure on unchanged baseline source `8a2a97d4e31857779da547797eed10f478dd8874`, and reconciled it with existing issue #1419; it is failed historical evidence, not a candidate repair claim. Later valid REST companion and WorkflowFlow controls passed on an earlier candidate host/pin. Their receipt is `root-candidate-backend-controls-v2/receipt.json` SHA-256 `95a3f141d88eed5e9bda1c4c9059589350a6ad8e8eb49be12459aaac510fb62f`. The final-pinned host repeated all five valid controls successfully. The valid REST companion checks Alice Smith and effective Coalesced/cap-two readback; its success does not independently assert an incident count. WorkflowFlow asserts zero incidents.

## Remaining gates

The initial map freshness check identified exactly two changed generated files, spec-status-map and test-map. A scoped maps refresh updated those snapshots, and the subsequent freshness check passed. The generated findings report has no changes; the manifest is byte-identical. PR review, hosted/native-provider verification, merge and resulting-main checks are still required. Local proof does not close #2497 or its parent feature.
