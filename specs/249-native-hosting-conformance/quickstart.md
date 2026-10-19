# Validate native hosting conformance

Run from the repository root on .NET 10 with local NuGet dependencies available. Real Workbench tests build the host and fixture, use fresh owned directories and ephemeral loopback ports. POSIX is required for suspension. Keep the shared build-slot wrapper enabled; a queued build is not hung.

```bash
uptime
dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~WorkbenchBackgroundActivityRecoveryTests|FullyQualifiedName~NativeHostingConformanceTests|FullyQualifiedName~NativeHostingGracefulHandoffTests'
```

The implemented recovery subset covers both completion orders, accepted-work recovery and cancellation. Concurrent clustered cases identify two actual PIDs and actual scoped EF connections. See the [evidence report](../../docs/reports/native-hosting-conformance.md) for the current boundary.

The stale-write scenario captures the original engine-generated checkpoint at both the public coalescing and real EF boundaries. It requires the same object, fingerprint and uncancelled token, a live persisted successor, exact `StaleToken` rejection, and unchanged activity, receipt, scheduler and marker state. Its negative control and complete recovery assertions are acceptance gates; expiry, cancellation or replay does not count. See the report for the latest executed evidence.

Fixture registration and disposal tests run separately:

```bash
dotnet test Essentials/tests/Workbench/Fixtures/BackgroundActivities.Tests/Elsa.Workbench.Tests.BackgroundActivities.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
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
