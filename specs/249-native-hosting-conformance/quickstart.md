# Validate native hosting conformance

Run from the repository root on .NET 10 with local NuGet dependencies available. Real Workbench tests build the host and fixture, use fresh owned directories and ephemeral loopback ports. POSIX is required for suspension. Keep the shared build-slot wrapper enabled; a queued build is not hung.

```bash
uptime
dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj --filter 'FullyQualifiedName~WorkbenchBackgroundActivityRecoveryTests|FullyQualifiedName~NativeHostingConformanceTests'
```

Expect both completion orders, accepted-work recovery and cancellation to preserve one native final continuation. Clustered cases must identify two actual PIDs. The fence case must show an unaccepted original commit reaching real EF with its original uncancelled token, then `StaleToken` and the greater persisted successor token. Expiry, cancellation or replay does not count.

Run affected runtime, runtime EF, distributed runtime/EF and membership tests selected in `tasks.md`; record concrete filters/results in the final report. These are contract/EF evidence, not all process-level evidence. Run architecture and map freshness:

```bash
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
git diff --check
```

If map bytes changed, deliberately regenerate affected maps and stage every changed output including `docs/maps/manifest.json`. Do not change unrelated workflow settings or rerun an unexplained timeout without retained diagnostics.

For the negative control, temporarily bypass only the actual EF checkpoint fence stage in an isolated/local validation state. Run the focused stale-owner case and require failure because its original stale write persisted. Restore the safeguard, verify the diff, rebuild and require that case to pass. Never commit/push the bypass. An unrelated failure is not a valid control.

Retain TRX/logs, source SHA, configuration/profile, PIDs, durable evidence and commands. Inspect current-head and post-merge CI/Maps; record skipped jobs as skipped. Green runs do not explain historical timeout causes or qualify untested providers/network conditions.
