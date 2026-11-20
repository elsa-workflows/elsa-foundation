# Validate native hosting conformance

Run from the repository root on .NET 10 with local NuGet dependencies available. Real Workbench tests build the host and fixture, use fresh owned directories and ephemeral loopback ports. POSIX is required for suspension. Keep the shared build-slot wrapper enabled; a queued build is not hung.

For the inspection-repair integration and failed-request diagnostic change, run the complete affected Workbench project. This includes the focused diagnostic cases and the real-process matrix below; do not add their repeated results together as unique tests.

```bash
dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --logger 'trx;LogFileName=native-diagnostics-workbench-full.trx' --results-directory /tmp/elsa-native-hosting-results
```

For a fast diagnostic development check, select `FullyQualifiedName~NativeHostingScenarioTests` with the same project/configuration. The non-JSON HTTP400 case must fail immediately with method, path, status, raw body and owned-host output, and send exactly once. To prove that assertion, temporarily remove only the body from the shared sender's failure message, require the focused case to fail at its missing-body assertion, restore the source and require the whole diagnostic class to pass. Never commit the mutation. This tests diagnostic retention, not the historical HTTP400 cause.

The process-only subset is useful when diagnosing a specific recovery scenario:

```bash
uptime
dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~WorkbenchBackgroundActivityRecoveryTests|FullyQualifiedName~NativeHostingConformanceTests|FullyQualifiedName~NativeHostingGracefulHandoffTests' --logger 'trx;LogFileName=native-integrated-matrix.trx' --results-directory /tmp/elsa-native-hosting-results
```

For a new run, record `git rev-parse HEAD`, any source/configuration diff, the command output and build-artifact hashes beside the TRX. The [integrated source record](../../docs/reports/native-hosting-conformance.md#integrated-source-and-retained-artifacts) identifies the recorded run; do not assign its identity to a later build.

The implemented recovery subset covers both completion orders, accepted-work recovery and cancellation. Concurrent clustered cases identify two actual PIDs and actual scoped EF connections. See the [evidence report](../../docs/reports/native-hosting-conformance.md) for the current boundary.

The stale-write scenario captures the original engine-generated checkpoint at both the public coalescing and real EF boundaries. It requires the same object, fingerprint and uncancelled token, a live persisted successor, exact `StaleToken` rejection, and unchanged activity, receipt, scheduler and marker state. Its negative control and complete recovery assertions are acceptance gates; expiry, cancellation or replay does not count. See the report for the latest executed evidence.

Fixture registration and disposal tests run separately:

```bash
dotnet test Essentials/tests/Workbench/BackgroundActivities/Tests/Elsa.Workbench.BackgroundActivities.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
```

The graceful-handoff case uses SIGTERM and an opt-in bounded stop gate. It observes the real owner's `Draining` membership and successful placement release, directs a new request to the active survivor, and requires both the new and already accepted workflows to complete. Automatic exclusion of a draining member is covered by the owning-layer `DrainingTests`, not by that directly addressed request.

Run affected runtime, runtime EF, distributed runtime/EF and membership tests selected in `tasks.md`; record concrete filters/results in the final report. These are contract/EF evidence, not all process-level evidence. Run architecture and map freshness:

```bash
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
git diff --check
```

If map bytes changed, deliberately regenerate affected maps and stage every changed output including `docs/maps/manifest.json`. Do not change unrelated workflow settings or rerun an unexplained timeout without retained diagnostics.

For the negative control, temporarily bypass only the actual EF checkpoint fence stage for the selected `ActivityCompleted` / `BackgroundActivityResult` commit with exactly one background receipt acceptance in an isolated/local validation state. Run the focused stale-owner case and require failure because its original stale write persisted. Restore the safeguard, verify the diff, rebuild and require that case to pass. Never commit/push the bypass. An unrelated failure is not a valid control.

Retain TRX/logs, source SHA, configuration/profile, PIDs, durable evidence and commands. Inspect current-head and post-merge CI/Maps; record skipped jobs as skipped. Green runs do not explain historical timeout causes or qualify untested providers/network conditions.

The 7–8 November 2026 execution-core PR2 recheck used these same commands. The unpushed commits were rebased onto main `c1f35d2dc73201df0e3890e1f0c851d9ddd52a37`; the test tree is `9812e60f54`. Results, quarantined-not-counted rows, and NOT RUN reasons are in [docs/reports/native-hosting-conformance.md](../../docs/reports/native-hosting-conformance.md#pr2-recheck-78-november-2026). That recheck does not replace the October source pins above.

## SQLite native/BPMN composition milestone

The [active smaller goal](../../docs/program-goals/unified-execution.md#active-milestone--8-november-2026) uses the existing real-process examples, not a new sample host. These commands start private Workbench instances on ephemeral loopback ports, publish real native/BPMN artifacts through HTTP, use private SQLite stores, and stop only their own hosts. They need the .NET 10 SDK, Python 3 and access to the pinned fixture archives and package feeds. No existing user database or deployed server is used.

Restore the hash-checked archived build inputs, then run the two root human-work examples:

```bash
python3 tools/bpmn/restore_evidence.py
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release -p:RestoreLockedMode=true -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~RunBpmnWorkbenchTests&DisplayName~Root'
```

Expected: two cases, completion and cancellation. Each publishes a native root `RunBpmn` invoking a direct BPMN human task, waits, kills/restarts its own host, then completes or cancels. Cancellation refuses a late human completion with Conflict. A later restart preserves the terminal state.

The service-worker examples run a real published native worker under that parent/model boundary:

```bash
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~RunBpmnChildFaultHeldRetryTests'
```

Expected: three cases covering successful held retry, final fault and exhausted retry; the parent has exactly one truthful terminal outcome.

For the pending-result recovery cut:

```bash
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~RunBpmnPendingParentResultCrashTests'
```

This witness deliberately retains a genuine in-flight outbox delivery claim across process death. It must observe the parent still waiting before the one-minute claim expires, then exactly one continuation within thirty seconds of eligibility. The private `hold-claim` file mode belongs only to this test; ordinary hosts leave the hold setting unset. The runtime lease is never shortened. This is distinct from the composite `callActivity` recovery examples, which do not admit direct-plane callActivity.

Set `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY` to a new private output directory and `ELSA_BPMN_HOST_SOURCE_REVISION` to `git rev-parse HEAD` before the build/run to retain source-bound binary hashes, observations and owned-store snapshots. The pending-result receipt records observed claim-wait/settlement time and durable source/input row counts. These are single-host diagnostic observations, not throughput or production-latency promises. Preserve failures and raw TRX separately from successful results; never relabel artifacts from a previous build.

Profile: SQLite, built-in host, root composition. These commands are executable developer examples, not Studio visual acceptance, other-provider/multi-host qualification, R3, ETL or complete Program #2892 delivery. Their expected outcomes are assertions; current execution results and limitations belong on the active PR and program issue. The historical source-bound counts above remain historical.
