# Groundwork E3 evidence closeout

This report records the final current/after inventory and the outcome of the two remaining evidence
checks for Groundwork issue #269. It deliberately keeps source closure, correctness, and performance
evidence separate.

## Current/after inventory

The durable current/after artifact is [groundwork-e3-current.json](groundwork-e3-current.json). It was
generated against Elsa `main` at commit `3e694377ff6a73dc6b80e34991222b1f8cd47509` (Groundwork
`0.4.0-preview.1`). Its manifest contains **16 files / 1,907 source lines**, with zero effective v1
package consumers, zero `LogicalIndexDeclaration` sites, zero `BoundedQueryDeclaration` sites, and
three reviewed accepted-scan markers.

The inventory generator now resolves a `PackageReference`'s explicit version, `VersionOverride`, or
central version before counting it. This matters because Groundwork provider package IDs are reused by
v2: matching package names alone would falsely report current `0.4.0-preview.1` references as v1.
The frozen artifact remains unchanged and verifies at the recorded `4418bb9e38641ec92960e7cf27efbd2e583cda04`
source snapshot:

```text
./tools/groundwork/verify-e3-baseline.sh
E3 baseline verified: 7 v1 packages, 25 manifest files / 5784 lines, 52 logical-index sites, 28 bounded-query sites, 0 AcceptScan markers
```

The current artifact verifies against the current checkout with:

```bash
python3 tools/groundwork/generate-e3-baseline.py \
  --check-current --artifact docs/reports/groundwork-e3-current.json
```

## Historical search for checkpoint round-trip evidence

The search covered the local Git history and all refs, the historical measurement commits and files,
the Elsa pull requests and comments that introduced the benchmark, and GitHub Actions runs/artifacts for
the relevant commits.

The result is:

| Candidate | What exists | Why it is not an exact before round-trip artifact |
|---|---|---|
| Elsa PR #1193 / commit `8ce409776` | v1 checkpoint adapter, report, and two artifact-manifest files | The report is latency-only; its protocol predates exact round-trip fields. Raw process files were not committed. |
| Elsa commit `93e441b72e4dcefb91527ad63828d712216794fe` | Exact v1 source commit and PR-linked report provenance exist remotely | The committed manifests bind raw process-file hashes, but those process files are absent from the repository and no retrievable Actions artifact was found. |
| Elsa commit `5597cf505` | Later exact provider-command-count instrumentation | It changed the evidence contract after the v1 measurement; no replacement v1 cohort was retained. |
| Current Elsa `main` / `3e694377f` | v2 correctness adapter and session observer source | `CheckpointCommitAdapter.Operations` intentionally throws because the five measured operations are not implemented; the harness refuses a synthetic sequence. |

The historical report's v1 figures are therefore useful latency context only. They were measured against
Groundwork `0.0.1-preview.103`, not the final E3 v1 pin, and they contain no per-operation provider
round-trip counts. The current v2 tree contains no process artifacts. Reusing those values as #269's
before/after round-trip comparison would overstate the evidence.

The current benchmark command also remains non-executable without a workload and retained native-plan
evidence; this is an expected fail-closed guard, not a measurement:

```bash
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- matrix medium
```

Observed result: `error: matrix requires --workload.` (exit 2).

## Checkpoint evidence decision

An exact before/after round-trip comparison is **not recoverable from retained evidence** in this
repository state. The v1 latency report cannot be upgraded after the fact, and the v2 adapter must not
invent operations or counts. The truthful replacement evidence is:

- the current/after source inventory above;
- the frozen checkpoint workload contract and digest in
  `specs/094-harden-groundwork-stores/workloads/runtime.json`;
- the v2 adapter's fail-closed measured-operation guard and its separately runnable correctness command;
- the historical v1 latency report retained as non-round-trip context only.

Closing the #269 round-trip acceptance checkbox still requires a product-owner decision: either accept
this documented evidence waiver, or schedule implementation of the five v2 measured operations and a
fresh, exact-instrumentation before/after cohort. No performance verdict is claimed here.
