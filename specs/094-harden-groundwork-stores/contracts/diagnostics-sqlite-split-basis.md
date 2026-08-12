# Proposal: scope the diagnostics workload to SQLite so the EF oracle can be harvested

Status: **PROPOSAL — needs independent review.**
Proposed by the EF-Core-oracle scoping analysis
([`docs/reports/ef-core-oracle-scoping-2026-08.md`](../../../docs/reports/ef-core-oracle-scoping-2026-08.md), PR #1279).
Reviewer: **unassigned.** No `GatePolicy.Replacement` is involved, so `ProposedBy != ReviewedBy` is not
mechanically enforced here — but the workload contract is frozen supplier evidence for #646, so this
change carries the same independent-review discipline as
[`runtime-absolute-budget-basis.md`](runtime-absolute-budget-basis.md).

This document is the **proposal half**. It authorizes no edit on its own.

## The problem

`diagnostics-durable-history` is blocked under `gate.diagnostics.absolute-budget-required`, and the block
is widely read as "the EF ratio gate needs re-review". **That reading is wrong**, and it has been costing
the program the one piece of evidence that is not recoverable.

[`performance-handoff.md`](performance-handoff.md) is explicit about what the block is actually for:

> SQLite compares the retained same-provider EF diagnostics oracle. SQL Server, PostgreSQL, and MongoDB
> have no same-provider EF diagnostics oracle and therefore require workload-specific numeric absolute
> operational budgets plus correctness digest, native diagnostic record/catalog plans, Groundwork
> physical-form evidence, provider-work/round-trip/storage evidence, and queue shed/drain/restart
> outcomes. The policy shape is ratified, but no numeric budgets or executable absolute-budget gate are
> yet approved.

So SQLite — the **only** provider that has the EF oracle, and therefore the only provider that can
produce the comparison whose absence is irreversible — is already gradeable under the existing default
`OrdinaryStore` policy. It is blocked purely as collateral, because `requiredProviders` is
`["sqlite", "sqlserver", "postgresql", "mongodb"]` and the workload is **atomic across all four**.

The ordering constraint that governs this program says harvesting the EF ↔ Groundwork comparison must
precede deleting EF, because every other step is recoverable and destroying the oracle is not. The
current block inverts that priority: it holds the irreversible half hostage to numeric budgets for three
providers that have **no oracle to lose**.

## The core insight

> **Correction (2026-08-11).** This section originally cited the four `requiredProviderEvidence` values,
> which then carried `-with-retained-ef-oracle` / `-with-absolute-operational-budget` suffixes. Those
> suffixes were a **defect**, not a design: the field is consumed as a driver topology identifier, and no
> driver could report those strings — see
> [`diagnostics-provider-topology-basis.md`](diagnostics-provider-topology-basis.md). They have been
> corrected to the catalog topologies. The insight below survives, but its evidence is a different field.

The contract already distinguishes the two evidence regimes, per provider — in the workload's
`correctness.timingGate`:

> SQLite compares the retained same-provider EF oracle. SQL Server, PostgreSQL, and MongoDB require
> independently reviewed numeric absolute budgets plus an executable absolute-budget gate; neither exists
> yet.

One provider has a comparand; three do not and need budgets instead. **The contract already knows these
are two different obligations; only the admission gate treats them as one.**

## The proposal

Narrow the workload's *required* provider set to SQLite, while retaining all four provider-evidence
declarations as the durable record of the deferred obligation.

1. `requiredProviders` becomes `["sqlite"]`.
2. `requiredProviderEvidence` keeps **all four** keys, unchanged. The three non-SQLite entries stop being
   a run requirement and become the written record of what Route 2 still owes. Nothing is deleted, so no
   obligation can be lost by this change.
3. `benchmarkAdmission` becomes `{"status": "ready", "reason": "benchmark.ready"}`.
4. The workload count stays **13**, both coverage-ledger rows (`diagnostics-structured-log-store`,
   `diagnostics-open-telemetry-store`) stay, and the exact ledger denominator stays **34**.

### What this authorizes

A SQLite EF-vs-Groundwork ratio verdict for both diagnostics suboperations, under the **existing**
default policy — `GatePolicy.DefaultFor(GateClass.OrdinaryStore)` = p95 ≤ 1.25×, throughput ≥ 80%,
p99 ≤ 2× (`Harness/Gates.cs:45`). No new gate machinery. No reviewed replacement policy file. No numeric
budget.

### What this does not authorize

Any verdict, timing, physical-form selection, or ledger advancement for SQL Server, PostgreSQL or
MongoDB. Those three remain deferred pending Route 2 (numeric absolute budgets, independently reviewed,
per [`runtime-absolute-budget-basis.md`](runtime-absolute-budget-basis.md)'s ratified pattern). This
proposal does not shrink that obligation; it stops that obligation from blocking a different one.

## Why the golden vector does not change

This is the load-bearing property that makes the change cheap, and it must be verified rather than
trusted.

`ReproducibleWorkloadScenario.ComputeInputFingerprint` (`ReproducibleWorkloadScenarioCatalog.cs:433`)
hashes exactly `WorkloadId`, `ScenarioId`, `Seed`, `Parameters`, `OperationSequence`.
`ComputeResultDigest` (`:445`) hashes those plus the derived observations. **Neither
`requiredProviders` nor `Version` is an input.**

Narrowing the provider set is therefore *semantically inert* with respect to the frozen vectors:

- `input.fingerprintSha256` stays `448b4f1251861cc5629a6aed316a5ed2112ed14309da5b500838ad43f9513667`
- `correctness.resultDigestSha256` stays `d27a2436f75cf5bb44054e5e284631d4a00656223b5f2ba5ff0573e1fde4e7f7`

so the independent literal `GoldenVectors` entry (`:44`) is **not touched**. That matters: those values
are deliberately hand-entered so that "a generator change cannot update both the expected and actual
hashes together" (`:36-38`). A change that required regenerating them would forfeit exactly the
protection they exist to provide. This change does not.

### Trap: do not touch the seed

The seed is the string `spec094-diagnostics-durable-history-v1.1`. It **encodes the version**. Anyone
bumping `version` and then "tidying" the seed to match would change both hashes and be forced to
regenerate the independent golden vector — defeating its purpose, for a change that alters no
measurement input. **Leave the seed byte-identical.**

## The hash that does change

`WorkloadCatalog.ExpectedSourceDigests` (`:290-295`) pins a SHA-256 over the **raw bytes** of each
workload file (`SHA256.HashData(source)`, `:61-63`). Editing `diagnostics.json` invalidates it:

```bash
# current, matches the pin
sha256sum specs/094-harden-groundwork-stores/workloads/diagnostics.json
# 16ba05d98250fa5917baf40116b454a1106a3247cde2a94444a97cd49b53f8ad

# after editing, recompute and paste into ExpectedSourceDigests["diagnostics.json"]
```

Because the pin is over raw bytes, **whitespace and line endings are part of the contract.** Re-serializing
the file with a different formatter changes the digest even when the JSON is semantically identical.

## Open question for the reviewer: bump `version` or not?

This is the one genuine judgement call, and it should be ruled on rather than assumed.

`ReproducibleWorkloadScenarioCatalog.Scenario(...)` (`:377-384`) **hardcodes `"1.1.0"` for every
successor**, and `ValidateFrozenContract` asserts `actual.Version == successor.Version`
(`WorkloadCatalog.cs:156`). So bumping diagnostics alone to `1.2.0` requires threading a per-scenario
version through the factory and all eleven call sites.

| Option | Cost | Argument |
|---|---|---|
| **Keep `1.1.0`** *(recommended)* | none | The measurement contract is byte-identical: same seed, parameters, operation sequence, fingerprint, digest. Only the required-provider *scope* narrows, which is admission metadata, not a measurement input. Record the change in `performance-handoff.md` prose instead. |
| Bump to `1.2.0` | factory signature + 11 call sites, and a seed that now misstates its own version | Honours the handoff contract's convention that "the v1.0 hashes remain historical supplier evidence and are not silently reinterpreted" — though that sentence is about *hashes*, which do not move here. |

## Exact lockstep edit list

The block is enforced at four layers, all reading one code-owned switch, and the JSON and the code
**cross-assert each other**. Any subset of these edits leaves the tree red.

| # | File | Change | Fails otherwise as |
|---:|---|---|---|
| 1 | `specs/094-harden-groundwork-stores/workloads/diagnostics.json` | `requiredProviders` → `["sqlite"]`; `benchmarkAdmission` → `ready` / `benchmark.ready`. Keep seed, all four `requiredProviderEvidence` keys, both `coverageRows`, and (recommended) `version`. | — |
| 2 | `Contracts/WorkloadCatalog.cs:91-92` | The all-four-in-contract-order requirement becomes per-workload. | `"must require SQLite, SQL Server, PostgreSQL, and MongoDB in contract order"` |
| 3 | `Contracts/WorkloadCatalog.cs:290-295` | `ExpectedSourceDigests["diagnostics.json"]` → new `sha256sum`. **Recompute at the time of the edit:** the provider-topology correction already moved this pin once, to `fb2c8de1…00286a`, so the value in this table's sibling documents is not the one to copy. | `"does not match the frozen Spec 094 #646 source contract"` |
| 4 | `Workloads/ReproducibleWorkloadScenarioCatalog.cs:61-70` | Remove `DiagnosticsWorkloadId` from `TryGetBlockedReason`. This is the single source all four layers consult. | Workload stays blocked at catalog, matrix, comparison and gate |
| 5 | `Harness/ArtifactsAndMatrix.cs:609`, `:622` | No edit expected — both delegate to #4. Verify, do not duplicate the switch. | Silent second source of truth |
| 6 | `Harness/Gates.cs:90` | No edit expected — delegates to #4. Verify. | as above |
| 7 | `tests/Elsa/Architecture/GroundworkPerformanceHandoffTests.cs:152-158` | `requiredProviders` and `requiredProviderEvidence` assertions become per-workload; they currently assert all four for **every** workload inside one loop. | Arch test red |
| 8 | `tests/Elsa/Architecture/GroundworkPerformanceHandoffTests.cs:161` | Drop the `diagnostics-durable-history` blocked expectation. | Arch test red |
| 9 | `tests/Elsa/Persistence/Groundwork/Conformance/Tests/PerformanceWorkloadCorrectnessTests.cs:99`, `:127-129` | Same admission flip. | Conformance test red |
| 10 | `benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost` | EF diagnostics adapter leaf, plus the route-capture half of `capture-plan` (currently *"not started — refuses rather than fakes"*). | `matrix` refuses; a missing adapter is a blocked run, never a simulated result |

Items 1–9 are the unblock and are small. **Item 10 is the real engineering cost**, roughly 3–5 days,
and is the only item that touches measurement behaviour.

## Verification

In order:

1. `workload-vectors` must print an **unchanged** fingerprint and result digest for
   `diagnostics-durable-history`. This is the proof that the narrowing is semantically inert; if either
   value moves, the seed or a parameter was touched and the change must be rejected, not re-pinned.
   ```bash
   dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- workload-vectors
   ```
2. Workload-catalog, performance-handoff and diagnostics-conformance suites green.
3. The complete architecture suite, after `dotnet restore Elsa.Server.slnx --force-evaluate`.
4. Only then a real SQLite cohort, from a **dedicated clean checkout** — every child re-verifies a clean
   `HEAD` with `--untracked-files=all`, so any edit during the run aborts the remaining children. Build
   both projects, confirm the two copies of the harness assembly hash identically, `capture-plan`, then
   `matrix` detached. Never rebuild between staging and running.

## What must not be done

- **Do not touch the seed.** See the trap above.
- **Do not add a workload-JSON property.** `WorkloadCatalog.cs:76-77` is a closed allow-list of exactly
  seventeen names; an eighteenth throws `"contains an unknown contract property"`. The deferred-provider
  record must reuse `requiredProviderEvidence`, not a new field.
- **Do not drop the three non-SQLite `requiredProviderEvidence` keys.** `ParseProviderEvidence` closes
  that object against all four providers (`:108`), and dropping them would also erase the written record
  of the outstanding Route 2 obligation.
- **Do not split this into a fourteenth workload.** The count is asserted at `:66-67`, and a new id would
  additionally require entries in `Expected`, `ExpectedNativeRoutes`, `ExpectedLedgerMapping`,
  `GoldenVectors` and `Successors`, plus a ledger-denominator amendment. Narrowing one field is strictly
  cheaper and loses nothing.
- **Do not author a reviewed replacement gate policy file.** SQLite runs under the class default; a
  replacement would add a governance dependency this route exists to avoid. (It would also be the first
  such file in the tree — worth knowing, but not here.)
- **Do not substitute a generation-over-generation baseline** for the three deferred providers.
  `Comparison.Compare` (`Harness/ArtifactsAndMatrix.cs:498-511`) blocks when
  `source.CommitSha != candidate.CommitSha`, so every such run returns `Blocked` today. See the
  2026-08-06 Tier C correction in [`runtime-absolute-budget-basis.md`](runtime-absolute-budget-basis.md).
- **Do not express a per-workload absolute ceiling through `GatePolicy.DefaultFor`.** It keys on
  `GateClass`, not workload. `RatifiedBoundedReadPathP95Milliseconds` is the cautionary precedent:
  ratified at 40 ms, enforced by nothing, with five bounded-read workloads silently carrying the 150 ms
  durable-write ceiling instead. `ProtocolAndGateTests.Ratified_bounded_read_ceiling_is_declared_but_enforced_by_no_construction_path`
  fails if any construction path starts applying it. Route 2 will meet this wall; Route 1 does not.

## Residual risk

**The deferred reason code needs a home.** Removing `diagnostics-durable-history` from the blocked switch
removes the *workload-level* block. The three non-SQLite providers still must not be gradeable. The
proposal is that `gate.diagnostics.absolute-budget-required` survives as a **provider-scoped** deferral
recorded in `requiredProviderEvidence` and `performance-handoff.md`, rather than a workload-level
admission block. A reviewer should confirm this does not create a path by which a SQL Server artifact
could be admitted; if the enforcement cannot be made provider-scoped cleanly, that is an argument for
the fourteenth-workload variant despite its cost, and this proposal should be revised rather than
stretched.

**Ledger honesty.** Verified against [`coverage-ledger.json`](../coverage-ledger.json) at this head: it
holds 35 entries (the exact denominator is 34; `runtime-diagnostics-settings` is the separate in-memory
contract that supplies no evidence for either durable diagnostics row). Both diagnostics rows currently
carry `status: implemented`, `performanceVerdict: null`, and `providerEvidence` empty for all four
providers.

The ledger schema needs **no change** for this route: `providerEvidence` is already a per-provider map, so
a SQLite-only cohort populates `sqlite` and leaves `sqlserver`, `postgresql` and `mongodb` empty — which
is exactly the honest representation of what was measured. That is a further argument for narrowing over
splitting.

The obligation is that neither row may be advanced to a state implying four-provider coverage, and that
spec 144's T011 import records one provider, not four. A row with a `performanceVerdict` and three empty
evidence arrays must not read as complete.

## Governance

Needs, before any edit lands:

- a reviewer distinct from the proposer, recorded here;
- a ruling on the `version` question above;
- confirmation that the provider-scoped deferral in **Residual risk** is enforceable;
- ratification recorded against #646 and #642, then imported by spec 144's T011.

Until then `diagnostics-durable-history` correctly remains blocked, and the SQLite EF oracle correctly
remains undeleted.
