# 133 — Schema-admission skip-if-current + apply batching (Cold-Start Readiness, program unit 3)

## Goal

Cut Groundwork runtime schema admission out of the warm-boot path. Today every boot — even one against an
already-current database that applies **zero** operations — pays a **full inspection/validation walk** per
physical target: it reads the durable applied-state snapshot, deserializes it, and re-validates every storage
unit against the live database with per-route `PRAGMA` round-trips. This unit adds an opt-in **skip-if-current**
fast path: a boot records an Elsa-owned **applied-plan fingerprint** after a successful admission, and a later
boot whose composed plan fingerprint matches that stamp skips the walk entirely, replacing it with a single
indexed scalar read.

The companion **apply-batching** win (collapsing the fresh-database 873-operation apply into fewer transactions)
is analysed here and deferred to a Groundwork-package change with a concrete proposal, because the transaction
boundary is entirely inside the Groundwork executor and cannot be collapsed Elsa-side without reimplementing it
(see "Apply batching" below).

Program: First-Request / Cold-Start Readiness (`docs/program-goals/first-request-cold-start-readiness.md`), unit
3. Baseline evidence: spec 129 + `docs/reports/cold-start-readiness-2026-07.md`.

## Measured evidence (this unit)

Captured on the reference `GroundworkAllFeaturesDeploymentSchema` (8 families, SQLite, WAL via
`SqliteConnectionFactory`), same machine, same moment, under heavy fleet load (1-min load average ~550 — walls
are inflated **~50–100×** versus a quiet machine; the trustworthy signal is the **ratio**, not the magnitude):

| Case | Operations | Wall (load ~550) | Note |
|---|---:|---:|---|
| Fresh-database apply (`AutoApplyOnStartup=true`) | 873 | ~17,400 ms | one-time first-boot cost; the batching target |
| Warm-boot walk (already admitted) | 0 | ~4,000–17,500 ms (median ~5,300 ms) | full re-read + per-route re-validation every boot |
| Single applied-plan fingerprint read (skip-path shape) | 0 | ~0–3 ms | one indexed scalar `SELECT` |

The warm walk is **~5 s loaded / est. ~50–150 ms quiet**; the skip path is **~1 ms**. That is a **~1000×+**
reduction on the warm path and far exceeds the ">100 ms saved" bar this program set for keeping a stamp. The
deterministic op count is unchanged (873), pinned by `ColdStartSchemaOperationCountTests`.

## Design

### Skip-if-current (implemented, Elsa-side)

**Fingerprint scope.** One stamp per **physical target** — the `(manifest identity, provider name)` pair — which
is exactly the granularity at which Groundwork records applied state (`groundwork_physical_schema_state` is keyed
`(manifest_id, provider_name)`) and the granularity at which the initializer admits (one `InspectRuntimeAdmission`
call per target). The reference host composes all 8 families into one aggregate target, so it has one stamp row;
a host that splits providers gets one row per provider.

**Fingerprint content** (`GroundworkAdmissionSkipStamp`), all four must match for a skip:

- `TargetFingerprint` — Groundwork's authoritative physical-target fingerprint (`PhysicalSchemaTarget.Fingerprint`).
  It covers **everything the diff-plan walk compares**: manifest contents, storage-unit routes, projected columns,
  index sets, and provider identity. It is the same value Groundwork's own applied-state compare-and-swap uses.
- `CompositionFingerprint` — the wider Elsa host-selection fingerprint (selected features, contributor manifest
  versions, naming-policy identity, durable requirements). A superset guard.
- `ProviderVersion` — a provider upgrade can change how the same target physicalizes.
- `FormatVersion` — bumping it invalidates every previously written stamp.

Because these cover the walk's full **plan** input surface, a stale skip from a plan change is impossible — any
change moves a fingerprint and the boot falls through to today's full walk.

**Storage.** An Elsa-owned table `elsa_groundwork_admission_stamp`, written and read by
`SqliteGroundworkAdmissionStampStore`. It is deliberately **not** in Groundwork's `groundwork_*` namespace and
**never** touches the frozen legacy `SchemaVersion` stamp (per the house rule that `SchemaVersion` is a frozen
legacy stamp, not a migration/skip lever). Reading it is one indexed scalar lookup; a missing table (fresh
database) reads as "no stamp".

**Flow** (`SqliteGroundworkDocumentStoreInitializer`, only when the opt-in flag is set):
1. Read the stamp for this target. If present and it `Covers` the current composed plan → **skip** the walk, log,
   and continue to session setup (which uses the in-memory composed source, not the admission result).
2. Otherwise run today's `InspectRuntimeAdmissionAsync` (which admits/auto-applies as before). If it reports
   ready, **write the stamp** and continue.

### Config default: opt-in (off). Argued.

The program's rule is "on by default **only if** the fingerprint provably covers the walk's full input surface."
The walk's input surface is the composed plan **plus the live provider state** (the walk re-validates the live
schema for drift). The fingerprint provably covers the plan, but by construction it **cannot** cover live provider
state, so it cannot detect schema changed **out-of-band while the host was down**. Enabling the skip therefore
trades per-boot drift re-validation for the fast path. That is a conscious operational reduction, so the switch
defaults **off** (`SkipSchemaInspectionWhenPlanUnchanged`, opt-in on the SQLite runtime and unified persistence
shell features). When enabled, no stale skip from a plan change is possible; only during-downtime out-of-band
drift is traded away — which is re-detected the moment any plan change forces a walk, or when the switch is off.

### Crash-safety invariant (pinned by test)

The stamp is an optimization token, never a correctness gate. The initializer writes it **only after** the
Groundwork apply has durably committed and reported ready. A crash **between apply and stamp write** leaves no
stamp, so the next boot finds no stamp, cannot skip, and re-walks — which re-admits idempotently (0 operations
against the already-applied schema) and then writes the stamp. Test:
`GroundworkAdmissionSkipStampTests.Crash_between_apply_and_stamp_write_leaves_no_stamp_and_the_next_boot_re_walks`.

### Locked apply protocol unchanged

Skip-if-current adds a stamp **before/after** the admission call and never alters it. When the stamp does not
cover the plan, admission runs exactly as today, including the safe-only auto-apply authorization that denies
destructive/semantic-migration operations (`specs/094-harden-groundwork-stores` locked apply protocol). Batching
is not attempted Elsa-side (below), so approval semantics are untouched on every path.

## Apply batching (analysed; deferred to Groundwork with a proposal)

Fresh-database admission applies 873 operations as **873 separate transactions**: `PhysicalSchemaApplication`
`.ApplyAsync` calls `IPhysicalSchemaExecutor.ApplyOperationAsync` once per operation, and the SQLite executor
opens its own `BeginTransactionAsync` → apply DDL → `INSERT` operation row → `CommitAsync` → durability re-read
**per operation**, plus a post-create `PRAGMA` validation of each created object. Both the coordinator and the
executor live in the `Groundwork.Core` / `Groundwork.Sqlite` packages (verified against the consumed
`0.0.1-preview.80` assemblies, not only the clone). The transaction boundary is therefore **entirely
Groundwork-internal**; Elsa constructs the executor but cannot collapse the boundary without reimplementing every
DDL/validation path (a DRY and house-rule violation). Connection-pragma tuning does not help either: the
initializer already runs WAL + `synchronous=NORMAL`, so commits are not per-operation fsyncs — the fresh-DB cost
is per-operation CPU/round-trip overhead (create + validate-read-back + durability re-read), which only a
batch-apply path can remove.

**Proposed Groundwork change (follow-up):** add a batch-apply entrypoint to `PhysicalSchemaApplication` /
`IPhysicalSchemaExecutor` that runs all authorized non-destructive operations of one plan inside a **single
transaction** with a single durability barrier and defers per-operation validation to one end-of-batch
`ValidatePhysicalSchemaOperation`, preserving the exact same plan authorization (safe-only) and applied-state
compare-and-swap. This keeps approval semantics and idempotency while turning 873 transactions into 1. It must be
implemented in the Groundwork package (`~/Projects/ValenceWorks/Groundwork`, a different revision than the
consumed preview and shared with other sessions — **not edited by this unit**), then consumed via a package bump.

Because skip-if-current removes admission from the common warm/restart/redeploy path, and fresh-database apply is
a one-time-per-database cost, deferring batching does not block the program's warm-boot success criterion
("warm-boot schema phase < a few hundred ms").

## Non-goals (this unit)

- No batching Elsa-side (Groundwork follow-up, above). No change to the 873 fresh-DB op count.
- No change to `SchemaVersion` (frozen legacy stamp) and no reuse of Groundwork's internal `groundwork_*` tables
  as the skip lever.
- No change to admission behavior when the switch is off — the walk runs exactly as today.
- Skip-if-current is wired for the SQLite provider (the reference/measured path). PostgreSQL/SQL Server/Mongo keep
  today's full walk; they can adopt the same `GroundworkAdmissionSkipStamp` seam with a provider-specific stamp
  store as a follow-on.

## Files

- `src/Elsa/Persistence/Groundwork/Unified/Composition/GroundworkAdmissionSkipStamp.cs` — provider-neutral stamp.
- `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkAdmissionStampStore.cs` — SQLite stamp table read/write.
- `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkDocumentStoreInitializer.cs` — skip decision + stamp write.
- `src/Elsa/Persistence/Groundwork/Sqlite/DependencyInjection/SqliteGroundworkDocumentStoreRegistration.cs`,
  `.../Sqlite/Unified/DependencyInjection/GroundworkSqliteUnifiedRegistration.cs`,
  `.../Sqlite/SqliteGroundworkRuntimePersistenceShellFeature.cs`,
  `.../Sqlite/Unified/SqliteGroundworkUnifiedPersistenceShellFeature.cs` — opt-in flag wiring.
- `tests/Elsa/Persistence/Groundwork/Tests/GroundworkAdmissionSkipStampTests.cs` — stamp store, `Covers` matrix,
  crash-safety invariant.

## Verification

- `GroundworkAdmissionSkipStampTests` (5 assertions incl. crash-safety) + `ColdStartSchemaOperationCountTests`
  (op count still 873) — both green.
- Full `tests/Elsa/Persistence/Groundwork/Tests` project.
- Full-solution build.
