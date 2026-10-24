# Source decisions

Baseline: main `553f35e37ebd5073079ecbfff94f1bed4ff26e04`. No unresolved design clarification remains. Implementation may expose a defect; observations, not this plan, decide whether a provider qualifies.

## Reuse the native witness

**Decision:** Extend the existing Workbench background-work fixture and share setup, preserving assertions.
**Rationale:** `Essentials/tests/Workbench/Tests/WorkbenchBackgroundActivityRecoveryTests.cs` already publishes Sequence → Flowchart → two background bodies → final activity through REST and proves stable logical identity, durable receipts, cancellation and restart. Its processes are sequential; a concurrently live survivor is missing evidence.
**Alternative:** A synthetic workflow/actor test would duplicate setup and miss real hosting.

## Select composition, keep semantics

**Decision:** Explicit in-process and clustered profiles, one package pin/definition per scenario.
**Rationale:** `WorkflowsRuntimeDistributedFeature` selects routing/placement and uses cluster identity; ownership and checkpoint fencing remain runtime concerns. `WorkbenchDataProtectionTests` documents shared membership and lock settings. Stock Workbench chooses distributed features, so it is not itself proof of the library default.
**Alternative:** Changing activity code or installing a second membership provider would not test substitution.

## Observe the original physical commit

**Decision:** Post-configure the existing coalescing inner checkpoint store in the test package.
**Rationale:** `RuntimeCheckpointCommitter` attaches `ExpectedFence`; `EfRuntimeCheckpointCommitStore` checks replay first, then stages the fence before participants inside its transaction. `ResponseReplayObservationFeature` supplies a precedent. `WorkflowsRuntimeApiFeature` installs persistence modes in post-configuration; explicit dependency ordering is required. Retain the captured holder's disposal ownership rather than discarding it.
**Alternative:** Policy interception is too early for the physical commit; a synthetic stale request or replayed marker would not prove the original engine write.

## Prevent cancellation from masking rejection

**Decision:** Suspend the original process, gate real successor acquisition, and hold original ownership heartbeat only for the measured stale commit.
**Rationale:** `WorkflowDrainOrchestrator` renews ownership in a fresh operation scope and cancels the drain when renewal loses ownership. The unchanged commit/token must reach `RuntimeExecutionFenceValidator` and report `StaleToken`, rather than cancellation or expiry. Holding outside a transaction permits real EF acquisition.
**Alternative:** Replacing cancellation tokens, returning fake renewal success, directly changing persisted ownership, or using only a fake clock weakens the process proof.

## Evidence has layers

**Decision:** Reuse distributed placement/EF and membership conformance tests for their actual contracts; identify layers in the report.
**Rationale:** Scope isolation, runnability, drain/incarnation and duplicate registration already have owning tests. The membership runtime tier includes modeled execution and cannot replace actual Workbench/EF results.
**Alternative:** Repeating every lower-layer test in an expensive process matrix adds noise without strengthening their assertions.

## Owned-process suspension on macOS

The initial lifecycle smoke run on .NET 10/macOS hung after SIGSTOP. A native sample showed the testhost signal thread repeatedly calling `SystemNative_WaitIdAnyExitedNoHangNoWait` / `SystemNative_WaitPidExitedNoHang`; the managed stack showed `ProcessWaitState.CheckChildren`, with the signal helper blocked in `ProcessWaitState.ReleaseRef` during disposal. This matches the mechanism documented in [dotnet/runtime #135323](https://github.com/dotnet/runtime/pull/135323): a stopped child can be reported by macOS `waitid(WEXITED | WNOHANG | WNOWAIT)` while `waitpid(WNOHANG)` cannot reap it, leaving the shared process-management lock held.

The interrupted run is not passing evidence. Its native/managed stacks and intervention record are retained with the local test logs. The test harness will supervise its POSIX Workbench child through a shell that remains running and reaps it, so the deliberately stopped host is not a direct managed child. Signals must still target the exact owned Workbench PID, retain a real OS stopped-state observation, and validate crash/graceful cleanup and restart ownership. No runtime dependency, production behavior, or machine-wide signal handling is changed by this fixture adjustment. Passing lifecycle tests are required before relying on it for takeover evidence.

## Activation cancellation is not shell lifetime

The preserved recovery baseline failed after both replacement bodies started and returned successful lease renewals. Both then wrote cancellation markers without receipt entry or a renewal-loss warning. These observations are retained in the local `native-hosting-baseline/shared-fixture-regression.log` and TRX; the current evidence does not identify the cause of the older intermittent timeouts.

Root and independent source inspection traced a concrete defect: `DefaultShellWarmup.WarmAsync` always stops its activation run after the initial pass. The pinned CShells 0.0.31 implementation passes that run token through `ShellRegistry` to shell initializers, and stopping the run cancels it. `RunShellTasksInitializer` passes it to `TaskManager.StartExecutingRegisteredTasks`, which currently links the task-state token to it for background and recurring execution. Normal completion of warmup can therefore cancel long-lived workers in an active shell. This is a race: the activation runner completes `InitialPass` before disposing its lifetime source; if disposal wins before Workbench calls `StopAsync`, cancellation is a no-op. A test must control cancellation while the real activation run still owns its token, rather than assume every successful warmup cancels it.

**Decision:** Keep cancellation of the start operation separate from shell-owned execution. Startup and start hooks must observe both caller cancellation and a concurrent manager stop. After successful start, background and recurring execution belongs to the shell and ends through explicit stop/disposal. Preserve graceful background-stop/drain ordering before execution-token cancellation, and preserve the shared stop task when one caller cancels only its wait. Validate with deterministic lifecycle regressions, real CShells warmup, existing event-drain tests and the original process recovery scenario before accepting the repair. Add observational activation-token records to distinguish this path from application shutdown and claim loss. No activation-run workaround or timeout increase is part of the repair.

## Explicit profile regression checkpoint (19 October 2026)

The shared fixture now selects InProcess or Clustered explicitly. InProcess removes distributed runtime/placement/transport features; Clustered adds all three explicitly and configures EF membership. One immutable scenario package, absolute durable database/key/lock paths, per-host content roots, and shared atomic journal records are retained. The original crash/receipt/cancellation assertions now use fixture-owned restarts and publish only once per scenario.

The rebuilt Workbench recovery suite passed 7/7 with zero failures/skips: four profile/receipt-order cases, cancellation/late-receipt rejection under both profiles, and lease-wait arithmetic. The suite took 18m22s on the shared machine under high load; this is correctness evidence, not a performance claim. Real process restarts preserve logical invocation/attempt identity and accepted terminal history. Concurrent survivor placement/transport and the original stale-commit fence remain unproved by this checkpoint.

Command: `dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~WorkbenchBackgroundActivityRecoveryTests'`. The local TRX/log are retained as `native-profile-recovery` in the session evidence directory. The historical timeout cause remains unproven.

## Physical commit selection refinement

Root and independent source review refined the planned stale-write boundary without changing FR-004. The first background result's `ActivityCompleted` commit carries the completed activity and receipt acceptance. `RuntimeCheckpointCommitter.IsMandatoryCheckpoint` makes background participants Immediate; an empty coalescer forwards that physical commit unchanged. Selection must verify the exact result reason, participants, original object/fingerprint and `ExpectedFence`, rejecting a synthetic flush.

The earlier plan's durable scheduler claim assumption does not match this path. `WorkflowDrainOrchestrator` opens a coalescing session before scheduler dispatch; `CoalescingWorkflowSchedulerWorkQueue.ClaimAsync` uses its overlay, and `RuntimeCheckpointCommitter.ResolveConsumedWorkItems` suppresses the durable consume participant. The corrected proof requires empty consumed scheduler participants and an unchanged/unclaimed EF scheduler row, plus an unchanged `ResultReady` receipt/digest and activity state after the actual stale-token exception. An overlay claim may be observed but must not be described as an EF claim. No production scheduling behavior is changed to manufacture evidence. This remains a source-reviewed selection, not an executed stale-write proof.

## Concurrent routing checkpoint (19 October 2026)

Both completion orders passed through two real simultaneously live Workbench processes. The quiescent routing owner was suspended; the survivor committed a real background receipt and forwarded a newly enqueued runtime-resumption `RunSchedulerWork` command through EF while membership still judged both hosts live. After killing the original owner, its placement lease was actually released before expiry; an independent fresh snapshot taken after the release showed the owner departed. The observer does not capture the pump's authorizing membership read; the existing owning-layer `FailoverTests` exercise that decision with a modeled fleet. The survivor acquired a greater placement token, leased/acknowledged that exact transport item, and completed the surrounding native sequence once without re-running either completed body. A later real native execution acquisition also had a greater fencing token. This routing takeover occurs after the original native drain released its execution lease; it does not exercise an original stale write.

Review tightened two evidence points: correlate the send to a command enqueued after the exact WorkId/PID receipt commit, excluding older matching sends; and read each registered scoped EF context's actual `Database.ProviderName` and SQLite `DbConnection.DataSource`, separately retaining configured paths. Tests check all four actual contexts, backend/provider types, fresh membership, distinct PIDs and identical immutable package bytes. Connection strings are not emitted; connections are not opened for observation. The shared file-lock path is configuration evidence.

Final strengthened command: `dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~NativeHostingConformanceTests|FullyQualifiedName~Explicit_cancellation_stops_held_bodies_and_survives_process_restart'`. Result: 4 passed, 0 failed/skipped, including the two default-observation-off profile cancellation cases. Logs/TRX are retained as `native-resolved-ef-conformance` in the session evidence directory. A preceding version checked configured paths and passed 2/2; the final 4/4 run supplies the stronger resolved-connection evidence.

The fixture's additional EF references are private test dependencies; lock refresh added their closure without changing existing resolved entries. No production behavior was changed for the observer. Original stale-write rejection, its adverse control, graceful handoff and complete lower-tier qualification remain pending.

## Original stale-write and graceful-drain validation (19 October 2026)

The original-commit observer is now implemented around the real API coalescer and retained EF holder. Raw EF observations encode scope, execution, activity, commit and scheduler identity predicates with `EfRelationalIdentity`; the initial diagnostic run exposed this fixture lookup requirement before the proof passed. Public and inner events share one projection and retain object identity, fingerprint, metadata, participants and cancellation-token identity. A fresh persisted successor is revalidated immediately before delegating the original commit to EF. Gates hold neither a transaction nor a root write lease.

The strengthened `native-stale-graceful-diagnostics` run passed both cases, zero failures/skips. In the stale case, original PID 80629 retained fence 2 and survivor PID 80883 acquired persisted fence 3 for execution `14UeO2zYKBV`. The original heartbeat was actually intercepted. The unchanged uncancelled commit received `StaleToken`, with no marker, an unchanged `ResultReady` receipt, Running activity and unchanged unclaimed scheduler row. Heartbeat cleanup propagated real cancellation with no delegated/fabricated renewal success. The scenario removes its selector after that single measured attempt before releasing the survivor, preventing the test hook from selecting later recovery work. Recovery completed with no incidents and one native final activity.

The graceful case uses the production SIGTERM/membership lifecycle and a bounded test-only background stop gate. A real owner-side release is correlated to the lease actually released after stop was requested; admission and receipt handling can renew/reclaim placement, so the first historical claim is not the handoff token. Fresh post-release membership must show the owner Draining/live and the survivor Active/live before lease expiry. A distinct workflow begins a real body on the directly addressed survivor while the owner is still held in shutdown. The owner then exits cleanly and both the new and previously accepted workflow complete on the survivor. Owning-layer tests, not this directly addressed request, prove routing refusal for a draining member.

Diagnostics remain evidence limits. The first broad fence-removal control timed out before both bodies started and is invalid as a fence test. A subsequent restored run reached and correctly rejected the stale write but failed its final empty-incidents assertion; the old assertion did not retain the incident details. The strengthened scenario keeps that assertion, records the full final state, observes heartbeat cleanup and disables its selector before recovery. Its later clean result does not establish the earlier incident's cause. The first graceful run observed a valid release but correlated it to an earlier lease, which the fixture now corrects without changing production behavior or increasing stop deadlines.

A narrower negative control bypassed only the actual `StageExecutionFenceAsync` call for `ActivityCompleted` / `BackgroundActivityResult` with exactly one acceptance. Initial admission remained fenced. `native-stale-targeted-fence-removed` failed at the required rejection assertion: original PID 82017 with fence 2 returned successfully while successor fence 3 was live, persisted its exact marker, changed the activity to Completed and accepted the receipt. That is a valid adverse result. The source is restored immediately by the command's exit trap and independently checked for an empty production diff; the rebuilt `native-stale-graceful-final` run then passed both cases, zero failures/skips. Its stale execution `14UeiJpKRKt` used original PID 86382/fence 2 and successor PID 86480/fence 3, with unchanged protected state and no final incidents. Its graceful execution `14UedfzM1PR` released owner PID 86060's actual placement token 4 before expiry; a separate admitted workflow started on survivor PID 86176 while the owner was still held in shutdown. Both completed. This establishes the local restore/control gate, while the final integrated matrix and delivery gates remain open.
