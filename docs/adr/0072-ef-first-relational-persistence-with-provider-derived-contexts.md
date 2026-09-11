---
status: proposed
date: 2026-09-10
decision_context: Spike PR #1622, completed Secrets EF pilot #1626, and verdict PR #1655; revision direction authorized by Sipke Schoorstra on 2026-09-11, with acceptance of the final text still pending.
---

# EF-first relational persistence with provider-derived contexts

Status: proposed (2026-09-11 revision). Sipke authorized revising the ADR in this bounded
direction after reviewing the pilot verdict. That authorization is **not** acceptance of this final
text; acceptance requires an explicit decision after exact-head review.

Program goal: `none/free-flow`. If accepted, this ADR narrows
[ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) for
individually admitted relational modules. It does not schedule a replacement wave, switch a host
default, delete a Groundwork adapter, or decide Runtime persistence.

[ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) and the active
[Zero-EF persistence program goal](../program-goals/zero-ef-persistence.md) remain authoritative until
this ADR is explicitly accepted. Acceptance must reconcile both sources in the same decision change.

Primary evidence:

- [Secrets EF pilot program #1626](https://github.com/elsa-workflows/elsa-foundation/issues/1626)
- [Pilot verdict and conditional replacement plan](../reports/secrets-ef-persistence-pilot-verdict-2026-09.md)
- [Provider-derived contexts versus FluentMigrator spike #1622](https://github.com/elsa-workflows/elsa-foundation/pull/1622)
- [Human decision and governance reconciliation #1628](https://github.com/elsa-workflows/elsa-foundation/issues/1628)

---

## Context

ADR 0042 made Groundwork the only first-party durable persistence family in Elsa Foundation, apart
from the later vendor-owned OpenIddict exception. It removed a parallel EF implementation estate and
the provider-project multiplication that accompanied it.

That decision raised a testable question: for shape-simple relational modules, can a conventional EF
Core implementation provide enough ecosystem familiarity, tooling, and model-drift protection to
justify a second first-party persistence lane without recreating the Elsa 3 provider-project matrix?
The product preference remains to minimize Elsa-owned persistence code, but the pilot evidence must
decide whether EF actually does so rather than assuming it.

Nuplane and CShells make the packaging and lifecycle constraints load-bearing:

- features are discovered and activated dynamically;
- migrations must travel with the feature that owns the schema;
- shell-scoped services are recreated on activation and reload;
- CShells does not run shell-scoped `IHostedService` instances;
- operators need both runtime and out-of-process migration modes;
- the domain contract must not expose EF, Groundwork, provider SQL, or `IQueryable`.

The spike in PR #1622 compared provider-derived EF contexts with FluentMigrator for a
Secrets-shaped module. Provider-derived contexts won that EF-internal comparison on owned-code cost,
operational familiarity, model-drift protection, and migration locking. The spike did not establish
that EF would own less code than Groundwork. It also established that one `DbContext` type owns one
model snapshot per assembly, so three provider migration sets require three derived context types.

The completed Secrets pilot then tested that direction in product code. It proved:

- one provider-blind `ISecretRepository` contract can be served by either EF or Groundwork;
- one module package can carry SQLite, SQL Server, and PostgreSQL derived contexts and migrations
  while referencing EF Core Relational rather than provider engines;
- runtime `AutoMigrate`, startup `Validate`, and out-of-process apply can use the same artifacts;
- shell activation and reload require `IShellInitializer` in addition to the host lifecycle;
- a shell can select EF or Groundwork for Secrets, never both;
- each selected family must own its own composition and evidence path;
- provider-specific OCC, physical types, and storage mechanics can preserve fixed module-level
  conflict, normalization, and search-key contracts;
- Workbench can remain Groundwork-default while an EF composition is opt-in.

The measured cost comparison disproved the code-reduction premise for Secrets. Excluding the 401
lines of generated Unicode casing data, its EF module has 1,278 hand-written source lines versus 510
for Groundwork, about 2.5 times as many. Excluding about 1,350 pilot-only test lines, it retains about
3,187 test lines versus 690, about 4.5 times as many. It also carries three provider
migration/snapshot sets and operator tooling. Runtime keeps Groundwork, so the shared Groundwork
layer cannot be retired by moving ordinary modules. An EF-first decision must therefore rest on
explicit benefits other than reducing owned code.

The pilot did **not** prove existing-domain data conversion, arbitrary cross-module multi-engine
composition, MongoDB parity, the current Foundation Host directory-feed route, or Runtime hot-path
suitability.

## Problem

Elsa Foundation needs a relational persistence policy that:

1. Uses mainstream EF Core only when measured module-specific benefits justify its additional owned
   code and provider-migration surface.
2. Preserves provider-blind domain contracts and provider-appropriate persistence semantics.
3. Works with dynamically enabled features without a host-owned global migration catalog.
4. Supports runtime and operator-controlled migration modes over one artifact set.
5. Prevents wrong-provider apply, cross-module migration-history collisions, and dual-family
   registration.
6. Makes conversion, rollback, composition, and evidence obligations explicit before replacing an
   existing Groundwork adapter.
7. Keeps Groundwork where document, Mongo, Runtime, or operational workload semantics make it the
   better family.
8. Does not infer broad product policy from a successful one-table pilot.

## Decision process

### Step 1 — Preserve the standing boundary during investigation

ADR 0042 remained authoritative throughout the spike and pilot. A scoped architecture-ratchet
exception allowed the pilot to run; it did not silently establish product policy. Groundwork
remained the Workbench default.

### Step 2 — Compare schema strategies with executable evidence

PR #1622 evaluated:

- provider-derived EF contexts with generated migrations;
- FluentMigrator migrations with EF data access and a separate model-drift gate.

Provider-derived contexts were selected for the pilot. FluentMigrator reduced generated snapshot
volume but added a second schema DSL, runner stack, and drift service while still needing
provider-conditional types and OCC behavior.

### Step 3 — Run a bounded product pilot

Program #1626 implemented the bounded Secrets EF technical pilot through four phases: EF policy and
module, dual migration modes, opt-in shell composition, and selected-family evidence ownership.
Corrective PRs closed review and operator gaps rather than treating the first green implementation
as sufficient.

### Step 4 — Audit the pilot against product-policy criteria

The final report evaluated provider support, contract neutrality, migration lifecycle, dynamic
composition, architecture containment, Workbench compatibility, and residual risks. It classified
the technical pilot as successful and the broader rollout as undecided.

### Step 5 — Separate direction, final text, and rollout authority

On 2026-09-11, Sipke authorized revising ADR 0072 toward a bounded EF-first relational lane. This
records that direction and the pilot lessons. Three distinct gates remain:

1. **Revision direction:** authorized.
2. **Final ADR text:** still proposed until explicitly accepted after exact-head review.
3. **Module rollout:** separately admitted and planned; ADR acceptance alone schedules nothing.

## Decision criteria

An acceptable policy must satisfy all of these:

- **Measured ownership:** compare EF and Groundwork source, tests, migrations, tooling, and shared
  infrastructure; do not claim code reduction where the evidence shows added ownership.
- **Contract neutrality:** domain callers remain independent of persistence family and engine.
- **Dynamic ownership:** migrations and lifecycle behavior travel with the feature.
- **Operator safety:** apply, validate, diagnostics, locking, and rollback are explicit.
- **Provider honesty:** dialect, OCC, types, normalization, indexing, and query behavior are tested
  per provider.
- **Compositional clarity:** one selected family owns registration and evidence for one domain in a
  shell.
- **Reversibility:** an existing implementation is not deleted before conversion and rollback are
  proven.
- **Evidence proportionality:** a simple-domain pilot cannot authorize Runtime or document-store
  replacement.

## Decisions

### D1 — EF Core is an allowed first-party relational family for admitted modules

Once this ADR is accepted, new or existing modules may use first-party EF Core only after they pass
the admission gate in D2. This is a bounded lane, not a repository-wide rewrite and not a declaration
that EF is best for every durable workload.

“EF-first” means: for an admitted, shape-simple relational module, evaluate the standard EF recipe
before inventing or expanding another Elsa-owned persistence framework. It does not mean automatic
conversion of existing Groundwork modules.

### D2 — Admission is per module and precedes implementation

A module is eligible only when all of these are demonstrated:

1. Its persistence contract is provider-blind and bounded; callers consume no `DbContext`,
   `IQueryable`, provider SQL, or EF entity.
2. Relational persistence is the intended product family; MongoDB or document-family parity is not
   a mandatory promise for that module.
3. Transaction, concurrency, ordering, query, tenancy, retention, and consistency semantics can be
   written as module-owned tests.
4. The workload is not a Runtime/G8 hot-path or dependent on Groundwork operational primitives.
5. An ownership comparison records source, tests, migrations, tooling, shared-layer effects, and
   expected schema churn. Existing modules use their actual Groundwork adapter as the baseline. New
   modules use the closest comparable Groundwork module or, when that estimate could change the
   decision, a bounded spike of both recipes. Explicit non-code-size benefits justify the added EF
   ownership, and three provider-derived migration sets remain reviewable.
6. For an existing module, data conversion, mixed-version behavior, cutover, rollback, evidence
   retention, and operational ownership are explicit acceptance gates.

Failing a condition means keep Groundwork or open a focused architecture decision. It does not mean
weakening the gate to preserve a rollout plan.

No second module is pre-approved by this ADR. Issue
[#1654](https://github.com/elsa-workflows/elsa-foundation/issues/1654) owns the candidate and
transaction-boundary inventory after acceptance.

### D3 — The domain feature owns its relational model and migrations

An EF persistence module references `Microsoft.EntityFrameworkCore` and
`Microsoft.EntityFrameworkCore.Relational`. Provider engines are supplied by the host's selected
composition, not referenced directly by the module.

The domain feature logically owns:

- the shared model and store implementation;
- the provider-derived contexts;
- each provider's migrations and model snapshot;
- feature registration and selected-family guards.

That logical ownership includes compatibility, review, release, and evidence responsibility. If a
provider generator emits provider-dependent compiled types, the physical derived context and
snapshot may live in an existing host provider package or companion already pulled by it. Do not
recreate the Elsa 3 matrix of one provider project per domain merely to preserve one-package
physical colocation.

### D4 — Each relational provider gets a derived context and snapshot

One `DbContext` type cannot own several provider snapshots in one assembly. Each supported
provider gets a derived type, for example:

- `SecretsSqliteDbContext`
- `SecretsSqlServerDbContext`
- `SecretsPostgreSqlDbContext`

Shared model configuration stays in the module's base context or configuration types. Design-time
generation and runtime apply both target the same derived type and migration set.

### D5 — Migration lifecycle covers both host and shell activation

The shared EF policy must support the actual hosting lifecycle:

- an `IHostedService` covers ordinary host-scoped startup where applicable;
- an `IShellInitializer` covers shell activation and reload because CShells does not execute
  shell-scoped hosted services.

Both paths resolve the provider-derived context selected for that shell and apply the configured
policy. Registration alone is not evidence; activation, reload, and failure behavior are tested.

### D6 — Runtime and out-of-process modes use the same artifacts

Every admitted module supports:

- **AutoMigrate:** apply pending migrations during the applicable host/shell lifecycle.
- **Validate:** fail closed when migrations or required persisted projections are stale.
- **Out-of-process apply:** target the provider-derived context through `dotnet ef` or an equally
  narrow operator host.

The modes use the same compiled migration artifacts. Runtime policy is configurable; an environment
may disable automatic apply and require operator-controlled migration.

### D7 — History, provider pairing, and concurrent apply fail safely

Each module owns a distinct migrations history table, such as
`__EFMigrationsHistory_ElsaSecrets`. Different modules must not share the default history table.

Apply refuses to run when the live `Database.ProviderName` does not match the selected derived
context. Concurrent apply uses EF's migration lock path or an explicitly equivalent lock; callers
must not bypass it with an unlocked custom migrator.

### D8 — Observable invariants stay fixed while provider mechanics vary

Do not assume `IsRowVersion()`, one Guid affinity, one JSON type, runtime Unicode casing, or one
index/query shape works across providers.

The domain module fixes the observable uniqueness, paging, conflict, tenancy, normalization,
retention, and redaction semantics in provider-blind contracts and shared conformance tests. Each
provider implementation then proves its own enforcement mechanism and physical encoding for:

- optimistic-concurrency token and the fixed conflict behavior;
- physical types and null/default behavior;
- indexes and bounded query plans that preserve the fixed uniqueness and paging semantics;
- the fixed normalization semantics and versioned persisted search-key algorithm;
- diagnostics and storage details that preserve the fixed retention and redaction semantics.

Persisted normalization/search-key algorithms are versioned compatibility contracts. A mapping table
is not regenerated in place when runtime or Unicode data changes; a new algorithm id, backfill, and
old/new lookup-continuity proof are required.

### D9 — One persistence family is selected per domain per shell

EF and Groundwork implementations of the same domain are mutually exclusive and fail fast regardless
of registration order. Domain contracts do not gain caller switches or dual-write behavior.

Composition and evidence follow the selected family:

- EF-selected composition proves the EF feature and omits the Groundwork store feature/ledger row.
- Groundwork-selected composition retains its Groundwork feature and evidence obligations.

Neither family can require the other family's ledger as a condition of readiness.

### D10 — Cross-module relational-engine topology is deferred, not implied

Each EF persistence feature binds to exactly one provider-derived context in a shell. The Secrets
pilot proved that boundary for one module.

This ADR does not decide whether one shell may safely bind different EF modules to different
relational engines. Default host packs may choose one relational engine consistently. A multi-engine
host must not be claimed or standardized until #1654 inventories connection ownership, transaction
boundaries, migration ordering, diagnostics, and operator UX and a follow-up decision records the
contract.

### D11 — Groundwork remains for document, Runtime, and specialized operational workloads

Groundwork remains the first-party document/Mongo family, outside these EF contexts. A module that
requires a document store keeps its Groundwork adapter or adds one with its own evidence. This ADR
does not authorize another first-party Mongo implementation.

Runtime checkpoint, execution state and logs, scheduler queues, durable command inboxes, timers,
outbox, mailbox and agent ownership, placement, transport, leases, fencing, and distributed locks
remain on Groundwork for this direction. Reconsidering them requires a dedicated ADR plus workload
evidence for correctness, contention, crash recovery, idempotency, and performance.

OpenIddict remains at the vendor persistence boundary established by ADR 0042. Do not merge its
vendor `DbContext` with Elsa IAM contexts.

### D12 — Default changes and retirement are separate decisions

Adding an EF implementation does not switch a checked-in host default. A default change requires a
separate PR with production-shaped evidence for that host.

For an existing domain:

1. add and prove the EF option;
2. rehearse conversion, cutover, rollback, and mixed-version behavior;
3. switch the default separately, if explicitly approved;
4. retain Groundwork through the rollback window;
5. retire Groundwork only after a separate compatibility and evidence decision.

No dual-write is introduced. The Groundwork adapter is the rollback path until retirement.

### D13 — Shared EF infrastructure stays a small policy surface

`Elsa.Persistence.EntityFramework` may own history naming, provider pairing, migration policy,
locking guidance, and host/shell lifecycle integration. It must not become another universal
persistence framework, accumulate domain entity mappings, or require modules to inherit from an
Elsa-specific base context.

Consumer-owned `DbContext` types remain first-class under
[framework §2.9](../../.specify/memory/constitution-framework.md#29-persistence-base-context--application-level)
and
[Elsa §E2.5](../../.specify/memory/constitution.md#e25-elsadbcontextbase--opt-in-capability-not-requirement).

### D14 — FluentMigrator remains a measured escape hatch

Provider-derived contexts are the default for this lane. Revisit FluentMigrator only when measured
module-by-provider snapshot churn becomes the dominant maintenance cost.

Any revisit requires:

- a provider matrix for physical types and migration behavior;
- an EF model-versus-database drift gate;
- migration locking and operator tooling;
- evidence that the reduced snapshot surface outweighs the second schema DSL and runner stack.

It is not a shortcut around provider-specific semantics.

### D15 — Production-readiness claims require the production-shaped route

The pilot's package-feed composition proof is valid for the bounded technical verdict. It does not
prove the current Foundation Host/Nuplane directory-feed route can safely share unsigned
`CShells.Abstractions` identity or reject a zero-feature shell.

The required outcome and evidence in
[#1644](https://github.com/elsa-workflows/elsa-foundation/issues/1644) must be complete before that
directory-feed route is called production-ready; issue closure records that evidence. Issue
[#1653](https://github.com/elsa-workflows/elsa-foundation/issues/1653) owns a real Secrets HTTP CRUD
and restart journey with EF selected. These evidence gates do not themselves authorize another
module or a default switch.

## Consequences

If accepted:

- ADR 0042 is narrowed from “Groundwork-only first-party persistence” to allow the bounded EF
  relational lane above.
- The shipped Secrets EF module becomes the first conforming implementation, while Workbench stays
  Groundwork-default.
- Architecture guards retain a small explicit first-party EF allowlist instead of treating the
  pilot exception as policy.
- Every admitted relational module maintains provider-derived contexts and migration snapshots for
  its supported providers.
- Existing-domain replacements carry conversion and rollback work that the greenfield Secrets pilot
  did not need.
- Groundwork remains a first-party family; this ADR does not set a date for its removal.
- Snapshot and provider evidence cost becomes visible per module rather than hidden in a shared host
  migration estate.
- EF adds owned code for Secrets, and is expected to do so for similar modules while Runtime keeps the
  shared Groundwork layer alive. Admission therefore requires a measured comparison and an explicit
  benefit case other than code-size reduction.

Costs and risks:

- generated migrations remain a module × provider review surface;
- provider-specific behavior requires separate tests and operational diagnostics;
- supporting two implementation families increases composition and compatibility responsibility
  during each transition window;
- the admission gate can correctly conclude that Groundwork remains the better fit.

## Alternatives considered

### Keep Groundwork as the only first-party family

This remains the standing policy until acceptance. It minimizes framework diversity and preserves
document/provider neutrality. The Secrets pilot showed that EF can serve the same domain contract
with familiar relational tooling and model-drift protection, but at greater owned-code cost for this
module. The proposed lane therefore depends on per-module evidence of benefits other than code-size
reduction; without that case, keeping Groundwork is the correct result.

### Shared host migrations

Rejected. Nuplane has an open feature set; a host-owned migration catalog cannot know every module
that may later be installed. Schema artifacts travel with the owning feature.

### One context with several migration folders

Rejected. EF binds one model snapshot to one `DbContext` type per assembly. Provider-derived
contexts are the supported isolation boundary.

### One provider project per module

Rejected as the default. It recreates the Elsa 3 project matrix. Provider engines belong to the
host composition unless generated provider-dependent code makes an existing host-provider companion
necessary.

### FluentMigrator plus EF data access

Deferred under D14. It reduces generated snapshots but adds a second schema language, runner, drift
gate, and locking surface while remaining provider-conditional.

### Dual-write EF and Groundwork

Rejected. It creates ambiguous authority, divergence and recovery semantics, and a much larger
operational surface. Conversion is explicit and Groundwork remains the rollback path.

### Convert Runtime with the same recipe

Rejected for this decision. Secrets is a shape-simple business record; Runtime stores include
leases, queues, fencing, outbox, placement, and crash-recovery semantics. They need a separate ADR
and workload evidence.

## Decision and rollout record

| Date | State | Record |
|---|---|---|
| 2026-09-10 | Proposed | PR #1623 drafted the provider-derived EF direction from spike #1622. |
| 2026-09-11 | Pilot concluded | Program #1626 and report PR #1655 recorded a successful bounded technical pilot and deferred product policy. |
| 2026-09-11 | Revision authorized | Sipke authorized revising ADR 0072 in the bounded direction: separately admitted shape-simple relational modules, Groundwork retained for Runtime/document/specialized workloads. |
| Pending | Final decision | Sipke reviews the exact final text after review convergence and explicitly accepts, revises, or rejects it. A revision returns to exact-head review. |
| Pending | Rollout | If accepted, #1654 may inventory candidates and create separately authorized module issues. No module is scheduled by this ADR alone. |

## Follow-ups and ownership

- [#1628](https://github.com/elsa-workflows/elsa-foundation/issues/1628): review and explicitly
  accept, revise, or reject the final ADR text; a revision returns to exact-head review. Acceptance
  reconciles ADR 0042 and the Zero-EF program goal.
- [#1623](https://github.com/elsa-workflows/elsa-foundation/pull/1623): on acceptance, record the
  accepted status and merge only after its gates and separate merge authority; on revision, update
  it and repeat exact-head review; on rejection, close it without merge and record the reason.
- [#1622](https://github.com/elsa-workflows/elsa-foundation/pull/1622): record an explicit disposition
  for every final outcome. Acceptance closes it as superseded by the product pilot and this decision
  while retaining its permalink; revision or rejection states whether further spike evidence is
  intentionally retained or closes it with the reason. Do not merge duplicate spike product code.
- [#1644](https://github.com/elsa-workflows/elsa-foundation/issues/1644): make the current
  Foundation Host/Nuplane directory-feed route production-ready.
- [#1653](https://github.com/elsa-workflows/elsa-foundation/issues/1653): prove EF-selected Secrets
  through a production-shaped HTTP CRUD and restart journey.
- [#1654](https://github.com/elsa-workflows/elsa-foundation/issues/1654): after acceptance, inventory
  module eligibility, transactions, conversion, rollback, and multi-engine topology before creating
  implementation issues.
- [#1657](https://github.com/elsa-workflows/elsa-foundation/issues/1657): eliminate the concurrent
  `dotnet ef` Tooling build-host race found by the post-main pilot gate.
- [#646](https://github.com/elsa-workflows/elsa-foundation/issues/646): retain broad native-provider
  and performance evidence for persistence workloads.

The broader rollout remains `none/free-flow` until an accepted governance record deliberately
creates or selects a program-goal bucket.
