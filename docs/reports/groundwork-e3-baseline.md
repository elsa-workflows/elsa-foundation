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

Verify the checked artifact against its recorded Git snapshot with:

```bash
./tools/groundwork/verify-e3-baseline.sh
```

That command is a permanent before-state integrity check: it does not compare the frozen artifact to the
changing E3 source tree. Generate a separate current/after inventory at any migration checkpoint, then
verify that artifact against the current checkout:

```bash
python3 tools/groundwork/generate-e3-baseline.py \
  --write \
  --artifact artifacts/groundwork-e3-current.json
python3 tools/groundwork/generate-e3-baseline.py \
  --check-current \
  --artifact artifacts/groundwork-e3-current.json
```

Keeping before and after artifacts separate means the cutover never needs to delete or disable its own
baseline guard.

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

The current harness requires one workload, provider, provenance, native-plan evidence, output, and a real
adapter-host command. `workload-vectors` succeeds and includes `checkpoint-commit`, `bookmark-lookup`,
`queue-drain`, and `outbox-drain`. The AdapterHost now registers real public-runtime leaves for all four,
and its SQLite vertical suite executes the full frozen correctness scenarios plus every measured operation.
The full correctness run passed 3/3 in 10 minutes 9 seconds on this checkout.

The evidence boundary remains intentionally fail-closed. `checkpoint-commit` has no required native routes,
but the other three workloads require real provider-specific raw plans. The current `capture-plan` command
can create only the routeless checkpoint document; no routed plan-capture leaf exists yet. Consequently no
honest retained round-trip measurements for the four-workload set can be published from this revision:

| Workload | Baseline result |
|---|---|
| `checkpoint-commit` | Executable leaf; no new retained matrix was run because this slice has no complete four-workload evidence set. |
| `bookmark-lookup` | Executable leaf; retained matrix blocked on real native plans for both required routes. |
| `queue-drain` | Executable leaf; retained matrix blocked on real native plans for both required routes. |
| `outbox-drain` | Executable leaf; retained matrix blocked on the required `list-claimable` native plan. |

After building the Release AdapterHost once and capturing all required `medium`-scale plans without
rebuilding, validate and print the exact per-workload commands with:

```bash
python3 tools/groundwork/run-e3-medium-baseline.py \
  --provider sqlite \
  --evidence-dir "$STAGING" \
  --out "$ARTIFACTS"
```

The runner requires these exact evidence documents:

```text
checkpoint-commit.sqlite.native-plan.json
bookmark-lookup.sqlite.native-plan.json
queue-drain.sqlite.native-plan.json
outbox-drain.sqlite.native-plan.json
```

Replace `sqlite` consistently with `postgresql`, `sqlserver`, or `mongodb` for another explicitly selected
driver. The driver owns fresh isolated provider connections; credentials and connection strings never enter
the command or artifacts. Add `--execute` to launch all four medium matrices after the printed commands and
provenance have been reviewed.

The precise #269 proof correction is therefore: replace the non-executable single command
`matrix medium` with the repository-relative runner above, and make real routed native-plan capture for the
selected provider an explicit prerequisite. Do not treat missing route evidence as zero work or a benchmark
result.

## Existing red debt

The full-solution baseline run performed alongside this slice already has unrelated v1 failures. They are
recorded here so later E3 comparisons do not misattribute them to the clean-break work:

- two SQL Server dashboard failures: one zero-count/shared-state-looking failure and one deterministic
  `GW-PHYSICAL-018` invalid projected-metadata failure;
- the benchmark `RoutingStructureMaterializations` assertion reports cache enabled equal to cache disabled.

This baseline slice does not repair or suppress those failures.
