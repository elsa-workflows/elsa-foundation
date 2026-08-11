# Correction: diagnostics provider-evidence values were not driver topologies

Status: **CORRECTION.** Found 2026-08-11 while writing the diagnostics adapter leaf.
Companion to [`diagnostics-sqlite-split-basis.md`](diagnostics-sqlite-split-basis.md), whose edit list
this extends.

## The defect

`diagnostics.json`'s `requiredProviderEvidence` declared four values that **no driver can report and the
topology catalog rejects**:

| Provider | Declared (before) | Catalog / driver value |
|---|---|---|
| `sqlite` | `file-backed-distinct-connections-with-retained-ef-oracle` | `file-backed-distinct-connections` |
| `sqlserver` | `real-sqlserver-container-with-absolute-operational-budget` | `real-sqlserver-container` |
| `postgresql` | `real-postgresql-container-with-absolute-operational-budget` | `real-postgresql-container` |
| `mongodb` | `transaction-capable-replica-set-with-absolute-operational-budget` | `transaction-capable-replica-set` |

Three facts make this fatal rather than cosmetic:

1. `MatrixPlan.Create` (`Harness/ArtifactsAndMatrix.cs:343`) requires
   `RequiredProviderEvidence[provider] == request.ProviderTopology`.
2. `GroundworkProviderTopology`'s constructor
   (`tests/Elsa/Persistence/Groundwork/Testing/GroundworkProviderDriver.cs:48-51`) throws unless the
   description is in `TopologyCatalog` and valid for that provider. None of the four suffixed strings is
   in the catalog, so a driver could not be *written* to report one.
3. An adapter leaf that compares `driver.Descriptor.Topology.Description` against the requested topology —
   as `CheckpointCommitAdapter.OpenDriverAsync` does — fails closed on the mismatch.

**Consequence: no `diagnostics-durable-history` run could start on any provider**, independent of adapter
choice, physical form, or the gate question. The workload was unrunnable for a reason unrelated to its
`gate.diagnostics.absolute-budget-required` admission block, and the block was masking it.

## Why it happened

The suffixes encode the **gate regime** — which providers have a retained EF comparand and which need
absolute budgets — not the **topology**, which is what the field is consumed as. SQLite's topology is
`file-backed-distinct-connections` whether or not an EF oracle is retained alongside it; the retained
oracle is a property of the comparison, not of the provider's shape.

That the diagnostics workload was the only one to do this is confirmable mechanically: all twelve other
workloads declare exactly the four catalog values, twelve times each.

## The correction

Set the four values to the catalog strings, matching the other twelve workloads.

**No information is lost.** The gate regime was already stated, in full, in the same file's
`correctness.timingGate`:

> SQLite compares the retained same-provider EF oracle. SQL Server, PostgreSQL, and MongoDB require
> independently reviewed numeric absolute budgets plus an executable absolute-budget gate; neither exists
> yet.

The suffixes were duplication of that sentence, in a field that is not free text.

## What this change touches

| File | Change |
|---|---|
| `workloads/diagnostics.json` | the four `requiredProviderEvidence` values |
| `Contracts/WorkloadCatalog.cs` | `ExpectedSourceDigests["diagnostics.json"]` → `fb2c8de1…00286a` (SHA-256 over raw file bytes) |
| `OpenTelemetryTestHost.cs`, `StructuredLogsTestHost.cs` | doc comments that cited the old string as their file-backed-SQLite rationale |
| `divergence-ledger.md` | same citation |

**The golden vector is untouched**, and that is checkable rather than asserted:
`ComputeInputFingerprint` hashes only workload id, scenario id, seed, parameters and operation sequence,
so `requiredProviderEvidence` cannot affect it. `input.fingerprintSha256` and
`correctness.resultDigestSha256` are unchanged, so the hand-entered `GoldenVectors` entry — deliberately
independent of the generator — needs no regeneration.

No test asserts these values. `GroundworkPerformanceHandoffTests` and
`PerformanceWorkloadCorrectnessTests` assert the four **keys** and that each value is non-empty, never a
specific string.

## What this does not do

It does not unblock the workload. `diagnostics-durable-history` remains `blocked` under
`gate.diagnostics.absolute-budget-required`, and the Route 1 narrowing plus its independent review are
still required before anything runs. This correction only removes a second, previously unnoticed blocker
that would have surfaced as an opaque contract rejection *after* Route 1 landed.

It also does not address the remaining EF-side gap: `physicalFormsFor646` declares only two forms, both
describing Groundwork's record-stream/document-catalog shape, so an **EF measurement set still has no
admissible form label**. Borrowing a Groundwork label would put a false claim in the retained artifact,
which reviewers read to know what was measured. That needs its own decision and is recorded here so it is
not rediscovered a third time.
