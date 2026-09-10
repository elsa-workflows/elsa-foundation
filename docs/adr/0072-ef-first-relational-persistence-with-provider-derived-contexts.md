---
status: proposed
date: 2026-09-10
decision_context: Product discussion plus Claude second-opinion correction plus spike PR #1622 (Variant A); drafted at Sipke Schoorstra's request. Remains proposed until he accepts.
---

# EF-first relational persistence with provider-derived contexts

Status: proposed (2026-09-10). Drafted from the persistence spike and the product discussion;
**not accepted** until Sipke accepts.

Program goal: `none/free-flow`. This is a product-direction record, not an implementation unit of
[Zero-EF Persistence](../program-goals/zero-ef-persistence.md). Acceptance would narrow
[ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) for new simple
modules; it does not rewrite Groundwork runtime stores.

Spike evidence: [PR #1622](https://github.com/elsa-workflows/elsa-foundation/pull/1622)
(`spikes/persistence-ef-vs-fluentmigrator/`). Variant A (provider-derived EF contexts) won against
FluentMigrator for a Secrets-shaped pilot.

---

## Context

Elsa 3 persisted through store abstractions with concrete EF Core and Mongo adapters. Schema
evolution multiplied as *module × provider* projects: each domain that needed SQLite, SQL Server,
and PostgreSQL shipped three migration assemblies, and the owned persistence surface grew with every
new module.

Elsa 4 / Groundwork V2 solved the multi-provider schema problem on one path — `StorageUnit` plus
`Schema.Apply`, including Mongo — and that remains the current relational-plus-Mongo
implementation family in this repository
([ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md),
[ADR 0065](0065-groundwork-persistence-targets-are-named-and-lanes-bind-to-them.md)). The cost was
Elsa wrapping: adapters, evidence, and host composition around Groundwork became heavy relative to
what a shape-simple module actually needs. The product preference is to **minimize owned
persistence code** for those modules.

Nuplane makes a further constraint load-bearing. The host reconciles NuGet packages, loads
assemblies, and CShells discovers features. There is no provider-pack primitive. An open feature
model means **shared host EF migrations are unworkable**: the host cannot own a single migrations
assembly that enumerates every module that might later be enabled.

Two apply modes are both required:

- **Runtime auto-migrate** after a feature is enabled and the CShells/Nuplane host reloads.
- **CI/CD out-of-process migrate** (and optionally validate) so an environment can start with
  auto-migrate off and refuse to run if migrations are pending.

Most customers run one engine. Fine-grained multi-engine composition must remain possible.

Spike [PR #1622](https://github.com/elsa-workflows/elsa-foundation/pull/1622) compared two schema
approaches for a Secrets-shaped pilot (one table, OCC, per-module history, provider guard,
`MigrateAsync` vs FluentMigrator). Variant A won on owned-code cost, operational familiarity, and
the EF 9 migrate lock. Claude's correction, confirmed by generating two folders from two derived
types: **one `DbContext` type owns one `ModelSnapshot` per assembly**. Three provider migration
sets cannot live on a single context type.

## Problem

The repository needs a persistence direction that:

1. Uses EF Core as the relational family without recreating the Elsa 3 module×provider explosion.
2. Leaves Mongo as an optional second family, not a tax on every module.
3. Travels with the feature so Nuplane can enable a module without a host-owned migration catalog.
4. Supports both in-process auto-migrate and out-of-process CI/CD apply.
5. Does not silently apply the wrong dialect, share one migrations history across modules, or
   assume a single OCC mapping for every provider.
6. Does not freeze a Groundwork rewrite of the runtime hot path (checkpoint, queue, placement)
   into the same decision.

ADR 0042's "Groundwork only" completion criterion is the standing product rule until this ADR is
accepted. This draft records the intended narrowing, not a silent reinterpretation.

## Decisions

### D1 — Relational family is EF Core; Mongo is an optional second family

New simple domain modules persist relationally with EF Core. Mongo is optional and separate: not
every module needs it, and Mongo migrations/drivers do not ride inside these DbContexts.

The **runtime hot path stays off this migration** until a later ADR. Groundwork V2 remains the
current relational-plus-Mongo path for runtime checkpoint, queue, and placement.

**Groundwork freeze for domains that move to EF.** Stop adding new Groundwork adapters and stop
expanding evidence ledgers for those domains. Keep Groundwork where it already is, including
runtime, until that later ADR. This is a freeze of *new* Groundwork work on the moving domains,
not a deletion of existing Groundwork modules in this PR.

### D2 — One EF module package per domain module; migrations travel with the feature

Not one migrations project per provider. The module package references
`Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Relational` only. The host
brings exactly one provider package (SQLite, SQL Server, or PostgreSQL in the default packs).

Generated migrations travel with the feature so enabling it in Nuplane also enables the schema it
owns. A host-wide migrations project cannot enumerate an open feature set, and a design-time-only
assembly that nothing loads at apply time cannot hold the only copy. Snapshots ship in the
assembly that owns the derived context (the module when they compile against Relational; otherwise
the host's one provider package — D3).

### D3 — Provider-derived DbContext types

One `DbContext` type cannot own three provider snapshots. Each SQL provider gets a derived
context with its own `Migrations/` folder and `ModelSnapshot`.

Shape:

- `SecretsDbContext` — shared model and store configuration (module package).
- `SecretsSqliteDbContext` / `SecretsSqlServerDbContext` / `SecretsPostgreSqlDbContext` — each
  with its own migrations and snapshot.

The host registers the derived context that matches its provider. Design-time generation and
runtime apply both target that derived type.

Derived types and their snapshots ship where Nuplane can load them with the feature while the
**module** package still references Relational only (D2). If a provider generator emits types
that need the provider package to compile, that derived context and its snapshot live in the
host's **one** provider package (or a companion that package already pulls) — not in two extra
module-owned provider projects. That is the Elsa 3 matrix this decision refuses.

### D4 — Design-time tooling stays out of the module package

`IDesignTimeDbContextFactory` implementations and `dotnet-ef` live in a tooling or host-provider
project, not in the module package that Nuplane loads. The module stays free of provider engines
and of `Microsoft.EntityFrameworkCore.Design`. Generation is invoked from that tooling project
and writes into the per-provider `Migrations/` folders that ship with the derived context (D3).
The spike kept generated files in the tooling project for isolation; the product assembly that
Nuplane (or the host's one provider package) loads at apply time must contain them.

A repository script generates migrations per provider. CI fails on pending model changes **per
provider** (each derived context's snapshot against that provider's model).

### D5 — Per-module migrations history table

Each module uses its own history table, for example `__EFMigrationsHistory_<Module>`
(`__EFMigrationsHistory_ElsaSecrets` in the spike). Two modules in one database must not share
`__EFMigrationsHistory`. The table name is configured on the provider `Use*` options, not as a
magic property of the base context type.

### D6 — Dual apply modes

**In-process (Nuplane / feature enable).** After feature enablement and CShells reload, a
persistence feature's startup or post-reload hook resolves the provider-derived `DbContext` and
calls `Database.MigrateAsync()`. EF Core 9 already takes `IHistoryRepository.AcquireDatabaseLockAsync`
on that path. Hosts that call `IMigrator.Migrate` or apply pending migrations themselves must take
the same lock or they race.

**Out-of-process (CI/CD).** Apply with `dotnet ef database update --context <DerivedContext>`
against the environment connection string, or a tiny host that news the derived context and
calls `Migrate()`. The app may start with auto-migrate **off** and fail closed if migrations are
pending.

Both modes are required. Neither is a substitute for the other.

### D7 — Provider guard

Refuse apply when the live `Database.ProviderName` does not match the derived context's expected
provider. The spike's `ProviderGuard.Ensure` throws before SQL in that case. A Sqlite file opened
through a PostgreSQL-derived context must not run PostgreSQL migrations.

The host still selects the derived context from configuration. The guard is the last line of
defense, not the composition mechanism.

### D8 — Shared policy package, not a second Groundwork

A small shared package — name flexible, `Elsa.Persistence.EntityFramework` is the working title —
owns history-table naming, the provider guard, migrate-lock guidance, and startup policy
(auto-migrate on vs fail-if-pending). Keep it a **policy surface**. It is not a second Groundwork,
not a universal Elsa `DbContext` base that modules must inherit, and not a place to accumulate
entity configuration.

Framework [§2.9](../../.specify/memory/constitution-framework.md#29-persistence-base-context--application-level)
already forbids mandating an application base `DbContext`. Module-owned derived contexts remain
first-class. Any `ElsaDbContextBase` reuse is opt-in ([constitution §E2.5](../../.specify/memory/constitution.md#e25-elsadbcontextbase--opt-in-capability-not-requirement))
and is not required by this ADR.

### D9 — OCC / types are per provider

Do not assume `IsRowVersion()` as a cross-provider default. The spike showed the Sqlite provider
inserting `NULL` into an `IsRowVersion` column, which then fails `NOT NULL`. Use an explicit
concurrency-token strategy per provider (the spike stamped a 16-byte GUID in `SaveChanges` and
mapped `IsConcurrencyToken()`; SQL Server may keep `rowversion` later). The same caution applies
to Guid affinity and JSON column types.

### D10 — Pilot first; no big-bang Groundwork rewrite

**Secrets** (or an equivalent shape-simple module) is the first product implementation of this
pattern. New modules prefer it. Existing Groundwork modules are not rewritten in the same unit.

This ADR does not implement Secrets EF product code. Acceptance unblocks a Secrets pilot
implementation plan.

### D11 — FluentMigrator is deferred

The spike preferred Variant A. Revisit FluentMigrator only if N-module × 3-provider snapshot
review becomes the dominant cost. If it is revisited, a CI EF model-drift gate is mandatory
(`IMigrationsModelDiffer` plus `IDatabaseModelFactory` as in the spike's Variant B). Without that
gate, EF's model and FluentMigrator's schema diverge silently.

FluentMigrator is not dialect-free: Guid, JSON, and OCC still needed `IfDatabase` in the spike.
Taking it later is not an escape from the provider matrix; it is a second schema DSL plus a
runner stack plus a drift service.

## Consequences

**Once accepted:**

- ADR 0042's "Groundwork as the only first-party durable family" is narrowed: new simple modules
  ship EF Core with provider-derived contexts; Groundwork remains for runtime checkpoint, queue,
  and placement until a later ADR, and stays frozen for domains that move to EF.
- Secrets (or equivalent) becomes the first implementation plan. This ADR is the direction; that
  plan is the work unit.
- Module authors maintain one module package plus three derived-context migration folders, not
  three provider projects. Snapshot volume grows with modules × providers; D11 is the escape
  hatch if that volume dominates.
- Hosts pick one relational provider package. Multi-engine remains a composition concern, not a
  reason to put three providers in one module.
- Architecture guards that currently ratchet toward zero first-party EF will need a scoped
  allowlist for these module packages, analogous to the OpenIddict vendor exception already in
  ADR 0042. That allowlist change is part of the pilot, not this record.

**Deferred, not decided here:**

- `shells.json` provider presets.
- Oracle in the default packs — out until demand.
- Runtime hot-path persistence family. Groundwork stays until a dedicated ADR.
- Mongo adapters for modules that need a document family; those stay a second family.

## Alternatives considered

**Keep or simplify Groundwork for all domains.** Rejected for new simple modules. Groundwork V2
already solved multi-provider schema including Mongo, but Elsa wrapping is the cost this direction
exists to avoid. Runtime keeps Groundwork; the freeze (D1) stops expanding that wrapping onto
Secrets-shaped domains.

**Shared host migrations.** Rejected. Nuplane's open feature model has no closed module catalog
the host can migrate on behalf of. Migrations travel with the feature (D2).

**FluentMigrator schema plus EF for data access** (spike Variant B, PR #1622). Deferred, not
chosen (D11). One handwritten migration set is smaller than two generated snapshots for a single
table, but the owned surface is a second DSL, a runner stack, and a mandatory drift gate. FM is
still provider-conditional. `MigrateAsync` already locks; FM does not buy a lock for free.

**One context type with three migration folders.** Rejected. EF Core binds one `ModelSnapshot` to
one `DbContext` type per assembly. Confirmed in the spike by generating two folders from two
derived types. That constraint is why D3 exists.

## Follow-ups

1. Sipke accepts or amends this ADR. Status stays `proposed` until then.
2. After acceptance: Secrets (or equivalent) EF pilot implementation plan. No product EF code in
   this PR.
3. After the pilot: scoped EF-surface allowlist, per-provider CI model-drift on the derived
   contexts, and a generate-migrations script in the repo.
4. `shells.json` provider preset — later.
5. Oracle default-pack demand — later, if any.
6. Runtime checkpoint/queue/placement family — later ADR; Groundwork until then.
7. Delete or promote `spikes/persistence-ef-vs-fluentmigrator/` after the ADR is accepted and the
   pilot no longer needs it.

## Linked decisions and evidence

- [PR #1622](https://github.com/elsa-workflows/elsa-foundation/pull/1622) — Variant A vs Variant B
  spike; recommendation, LOC-ish counts, footguns, Nuplane hook points.
- [ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) — standing
  "Groundwork only" rule this draft would narrow.
- [ADR 0065](0065-groundwork-persistence-targets-are-named-and-lanes-bind-to-them.md) — named
  Groundwork targets; still the composition model for Groundwork lanes.
- Groundwork V2 (`StorageUnit` + `Schema.Apply`) — current relational-plus-Mongo path, frozen for
  new simple modules that move to EF, retained for runtime until a later ADR.
- Framework §2.9 / Elsa §E2.5 — consumer-owned `DbContext` types remain first-class; no mandated
  Elsa base context.
- Framework §2.20 — provider packages stay in the host; the module package does not reference
  provider engines.
