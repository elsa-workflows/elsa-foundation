# Quickstart: Durable Diagnostics Persistence

## Planning validation

```bash
rg -n "NEEDS CLARIFICATION|\[FEATURE|\[###|TODO|TBD" specs/091-groundwork-diagnostics-persistence
```

## Focused test progression

```bash
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj
```

The second project is created by this work unit. Add shared conformance and lifecycle projects to this list as the task phase fixes their exact paths.

## Required provider certification

Run the same highest-seam diagnostics fixture against:

1. SQLite on a real database file;
2. SQL Server in the pinned integration container;
3. PostgreSQL in the pinned integration container; and
4. MongoDB in the pinned replica-set integration container.

For each provider, retain test evidence for schema validate/apply, restart, concurrent writers, acknowledgement loss, exact retention, cross-scope isolation, and bounded plans for scale-bearing operations.

## Performance gate

Use the ratified EF-versus-Groundwork diagnostics workload and dataset, not a smoke profile, for promotion. Groundwork passes only when p95 is at most 1.25x EF, throughput is at least 80% of EF, and p99 is at most 2x EF. Record environment, provider versions, schema digest, workload digest, sample counts, database work/round trips, and raw artifacts.

## Final dependency audit

```bash
rg -n "EntityFrameworkCore|Persistence\.EFCore|DbContext|Migration" src/Elsa/Diagnostics tests/Elsa/Diagnostics
rg -n "Groundwork" src/Elsa/Diagnostics/*/Core
```

The first command may match temporary oracle code until the removal phase; it must have no diagnostics EF implementation/dependency matches at completion. The second command must remain empty throughout.

## Delivery rule

Work on the feature branch, implement by user-story slice with tests first, obtain an independent review, push the organization-owned branch, merge the reviewed PR, and leave the repository with all required checks green.
