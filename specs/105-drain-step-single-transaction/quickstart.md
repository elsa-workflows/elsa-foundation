# Quickstart / Verification

## Build

```bash
dotnet build src/Elsa/Elsa.csproj
```

## Targeted tests

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
```

## What to observe

- A single-commit drain step on a claim-capable provider records one durable checkpoint transaction and no separate `CompleteClaimAsync`, and the work item is gone.
- A stale-claimant commit (successor reclaimed, fencing token advanced) fails claim-lost and persists nothing; the successor-owned item survives.
- A renewed claim (owner/token unchanged) still consumes successfully.
- A handler that faults before committing still ack-deletes via the legacy fault path and poisons exactly once.
- A coalesced segment converges to an empty durable queue; `RuntimeCheckpointFold.Fold` unions consumed work items across the segment.
