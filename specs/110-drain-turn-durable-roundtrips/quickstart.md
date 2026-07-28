# Quickstart / verification — spec 110

## Reproduce the characterization

```
dotnet test benchmarks/Elsa/Workflows/Runtime/Benchmarks/Elsa.Workflows.Runtime.Benchmarks.csproj \
  --filter "FullyQualifiedName~DurableRoundTripDiagnostics" \
  --logger "console;verbosity=detailed"
```

Expected (deterministic across iterations; see [research.md](./research.md) for the full table):

| Scenario | checkpoint commits | durable queue ops | executable reads |
| --- | --- | --- | --- |
| 2-node · Immediate | 12 | 41 | 10 |
| 2-node · Coalesced | 2 | 4 | 10 |
| hot-loop×10 · Immediate | 66 | 194 | 46 |
| hot-loop×10 · Coalesced | 1 | 2 | 46 |

Reading: under Coalesced the per-turn durable commit + queue storm is already folded away; the
executable reads (~5×activity-count) are the only per-turn durable round-trip left.

## Phase 1 acceptance (once the read cache lands)

- Re-run the diagnostic with the cache enabled: `executable reads` for a single-artifact workflow drops
  to ~1; every other column is unchanged.
- Byte-identical guardrail: the committed checkpoint state and activity outputs are identical with the
  cache on and off.
- Full projects green:
  `dotnet test tests/Elsa/Workflows/Runtime/... tests/Elsa/Persistence/Groundwork/... tests/Elsa/Activities/Runtime/...`
