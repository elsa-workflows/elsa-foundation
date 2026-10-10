# Quickstart: Review and Verify Startup Runner Adoption

**Status**: Reviewed design. This guide records the review path and expected proof; it does not claim implementation, preview, or stable-release acceptance.

## Read the contract first

1. Read the [feature specification](spec.md), [research decisions](research.md), [data model](data-model.md), and [startup profile contract](contracts/startup-profiles.md).
2. Use the glossary for [host/shell lifecycle terms](../../docs/glossary/elsa.md) and [general lifecycle concepts](../../docs/glossary/root.md); do not create parallel definitions.
3. Inspect the existing adapters and projections before changing them:
   - [Foundation eager activation](../../src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationHostedService.cs), [failure tracker](../../src/apps/Elsa.Foundation.Host/Shells/ShellActivationTracker.cs), [health payload](../../src/apps/Elsa.Foundation.Host/Health/ShellNotActiveReason.cs), and [health endpoints](../../src/apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs).
   - [Workbench eager activation](../../src/apps/Elsa.Workbench/Boot/EagerShellActivationHostedService.cs), [default-shell warmup](../../src/apps/Elsa.Workbench/Readiness/DefaultShellWarmup.cs), [readiness state](../../src/apps/Elsa.Workbench/Readiness/ShellReadinessState.cs), and [activation telemetry](../../src/apps/Elsa.Workbench/Readiness/ShellActivationTelemetry.cs).

## Acceptance walkthrough

Keep the three profiles independently reviewable:

- Foundation: ordered pre-listen first pass; default-on/explicit opt-out; ordinary recovery and EF refusal cadence; initial-versus-background fatal behavior; caller-token detachment after startup returns; bounded then subsequent shutdown join.
- Workbench eager: default-off, target selection/order/deduplication, serial pre-listen one-shot, continue-on-failure, and no retries.
- Workbench warmup: nonblocking service start; `ApplicationStarted` gate; discovery before one default-shell activation; independent telemetry/outcome; exact generation from the successful returned shell.

Verify externally settled terminal startup recovery and later live-readiness deactivation without automatic run restart. For Foundation's failure projection, also verify both no-notification cases: raw Active suppresses a new failure while retaining a prior row if no lifecycle event arrived, and a real Active lifecycle notification clears the row/base. Verify a suppressed callback does not inflate the next visible count.

## Existing verification targets

Run the affected test projects without inventing narrow test filters:

```bash
dotnet test tests/essentials/Modularity/Tests/Elsa.Modularity.Tests.csproj
dotnet test tests/essentials/Cluster/EntityFrameworkCore/Tests/Elsa.Cluster.EntityFrameworkCore.Tests.csproj
dotnet test tests/essentials/Workbench/Tests/Elsa.Workbench.Tests.csproj
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
```

Review the established startup/readiness tests, including `FoundationHostEagerActivationTests`, `EagerShellActivationTests`, `ShellReadinessTests`, and `HostOwnedServicesAreSharedWithShellsTests`. Preserve the existing Cluster boot, refusal, recovery, and Workbench process test methods and their assertions. Include deterministic gates for fatal failures, independent targets, handoff cancellation, external settlement, and later join after a bounded wait. A behavioral mutation must fail the relevant assertion, then be restored byte-for-byte before recording the passing run.

For rebuilt-host recovery, use the existing Cluster/SQLite process tests and current e2e runner guidance in [e2e-tests/README.md](../../e2e-tests/README.md); preserve the existing boot/refusal objectives. Do not substitute a mocked-runner-only test for actual host recovery.

Run the affected architecture and generated-map checks:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Follow [NuGet lock-file guidance](../../docs/reference/nuget-lock-files.md) when final stable pins change. Stable completion requires the final released package families, coherent reached locks, clean-cache locked restore, exact host evidence, and green resulting-main gates. Preview evidence is interim only. No result is accepted solely because this checklist's commands are listed.
