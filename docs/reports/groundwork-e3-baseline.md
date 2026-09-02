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

The original #269 proof command was executed unchanged:

```bash
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- matrix medium
```

It exited 2 before measuring anything:

```text
error: matrix requires --workload.
```

That remains useful historical evidence that the naked harness command is not an executable proof. The
current #646 catalog contains thirteen workloads and fifteen exact adapter registrations. Its control plane
is now emitted by the built AdapterHost and consumed by the repository runner:

```bash
python3 tools/groundwork/run-e3-medium-baseline.py \
  status
```

At this checkpoint the machine-readable status reports five timing-ready registrations and ten blocked
registrations. Timing-ready means only that the workload is admitted and its native-plan path is complete
or explicitly routeless; it is not a measurement or verdict. The ready registrations are checkpoint,
IAM, recovery, the SQLite EF Secret oracle, and the Groundwork Secret target. Diagnostics correctness is
available, but timing remains blocked by `gate.diagnostics.absolute-budget-required`; its Groundwork plan
capture accounts for captured and explicitly blocked routes separately. The other eight runtime/distributed
registrations remain blocked by `capture.native-plan.not-implemented`.

The runner deliberately separates the evidence phases:

```text
status -> capture -> correctness -> measure -> compare -> gate
```

Each phase is a dry run until `--execute` is supplied. `capture`, `correctness`, and `measure` require one
exact workload/provider/adapter/form/measurement-set tuple and derive its current version, topology, seed,
input fingerprint, and phase readiness from `describe-matrix`. Evidence filenames include the measurement
set (`<workload>.<provider>.<measurement-set>.native-plan.json`). `measure` revalidates every route and raw
plan, refuses a blocked registration, and checks for unrelated build/test processes immediately before
timing. `compare` and `gate` consume retained measurement sets independently; correctness is never inferred
from timing and timing is never inferred from plan capture.

`describe-matrix` schema v2 also identifies the source revision embedded into both the AdapterHost and the
benchmark harness by the .NET SDK. Every runner phase requires those revisions to match a clean current
HEAD, so an older Release output cannot be presented as current-source evidence. The C# entry points
canonicalize output directories independently of the Python wrapper and reject repository descendants and
symlink aliases before provider resolution or writes. Direct execution additionally admits only the canonical
Release AdapterHost after a schema-v2 handshake, and package provenance must exactly match the provider
package names and versions in `Directory.Packages.props`. Comparison, gate, and blocked-report result paths
remain confined to the admitted external output tree.

The precise #269 proof correction is therefore: use the repository runner's explicit phases, select one
current registry tuple, and make complete routed native-plan evidence an explicit prerequisite for timing.
Do not treat missing or explicitly blocked route evidence as zero work or a benchmark result.

## Existing red debt

The full-solution baseline run performed alongside this slice already has unrelated v1 failures. They are
recorded here so later E3 comparisons do not misattribute them to the clean-break work:

- two SQL Server dashboard failures: one zero-count/shared-state-looking failure and one deterministic
  `GW-PHYSICAL-018` invalid projected-metadata failure;
- the benchmark `RoutingStructureMaterializations` assertion reports cache enabled equal to cache disabled.

This baseline slice does not repair or suppress those failures.
