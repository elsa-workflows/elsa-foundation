# Groundwork E3 clean-break baseline

This report freezes the Elsa source baseline for Groundwork issue #269 at
`4418bb9e38641ec92960e7cf27efbd2e583cda04`. The migration is intentionally a clean break against fresh
catalogs. No v1-to-v2 data migration or compatibility path is part of this baseline.

## Source inventory

The machine-readable inventory is [groundwork-e3-baseline.json](groundwork-e3-baseline.json). Regenerate it
only when deliberately accepting a new baseline:

```bash
python3 tools/groundwork/generate-e3-baseline.py \
  --write \
  --artifact docs/reports/groundwork-e3-baseline.json
```

Verify the checked artifact against the source tree with:

```bash
./tools/groundwork/verify-e3-baseline.sh
```

The frozen source contains seven v1 Groundwork package identities, 25 discovered manifest files totaling
5,784 lines, 52 direct `LogicalIndexDeclaration` construction sites, 28 direct
`BoundedQueryDeclaration` construction sites, and zero `AcceptScan`/`GwAllowAcceptedScans` markers. The
JSON artifact records every package consumer and every declaration site with repository-relative paths and
line numbers.

## Performance baseline status

The proof command currently printed by issue #269 was executed unchanged:

```bash
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- matrix medium
```

It exited 2 before measuring anything:

```text
error: matrix requires --workload.
```

The current harness requires workload, provider, provenance, native-plan evidence, output, and a real
adapter-host command. `workload-vectors` succeeds and includes `checkpoint-commit`, `bookmark-lookup`,
`queue-drain`, and `outbox-drain`, but
`benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/BenchmarkAdapterFactory.cs` registers only the
`checkpoint-commit` adapter leaf. The adapter-host README explicitly describes every other workload as a
blocked run. Consequently no honest round-trip measurements for the requested four-workload set can be
published from this revision:

| Workload | Baseline result |
|---|---|
| `checkpoint-commit` | Not measured by the issue command; a full provider/provenance cohort is required and documented as tens of minutes. |
| `bookmark-lookup` | Blocked: no adapter leaf. |
| `queue-drain` | Blocked: no adapter leaf. |
| `outbox-drain` | Blocked: no adapter leaf. |

## Existing red debt

The full-solution baseline run performed alongside this slice already has unrelated v1 failures. They are
recorded here so later E3 comparisons do not misattribute them to the clean-break work:

- two SQL Server dashboard failures: one zero-count/shared-state-looking failure and one deterministic
  `GW-PHYSICAL-018` invalid projected-metadata failure;
- the benchmark `RoutingStructureMaterializations` assertion reports cache enabled equal to cache disabled.

This baseline slice does not repair or suppress those failures.
