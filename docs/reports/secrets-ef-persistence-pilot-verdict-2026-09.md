# Secrets EF persistence pilot verdict and conditional replacement plan

Status: concluded technical pilot; product-direction decision pending.

Evidence cut: `main` at `0a6a6595b9905048ce694d849d4fb080993fe727` (2026-09-11).

Program: [#1626](https://github.com/elsa-workflows/elsa-foundation/issues/1626).

## Verdict

The Secrets EF pilot **succeeded as a narrow technical pilot for a shape-simple relational
module**. It proves that Elsa can add an EF-backed Secrets implementation without changing
`ISecretRepository`, carry SQLite, SQL Server, and PostgreSQL migrations with one module package,
use the same migrations for runtime and out-of-process apply, select EF or Groundwork per shell,
and keep Workbench on its Groundwork default.

That verdict does **not** accept proposed ADR 0072, make EF the repository default, prove the
Foundation Host/Nuplane directory-feed route production-ready, prove MongoDB parity, or say that
Runtime checkpoint, queue, placement, outbox, timer, or lock workloads should move to EF. The
standing rule remains [ADR 0042](../adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md)
until the human decision in [#1628](https://github.com/elsa-workflows/elsa-foundation/issues/1628)
changes it.

The product conclusion is therefore conditional:

> Reuse the Secrets recipe for eligible relational modules only after ADR 0072 is explicitly
> accepted or revised into that direction. Do not invent another persistence framework. Keep
> Groundwork for the Runtime hot path and for domains whose document/Mongo or operational needs
> make it the better fit.

## What shipped

| Phase | Merged work | Result |
|---|---|---|
| 0 + 1: policy and first module | [#1624](https://github.com/elsa-workflows/elsa-foundation/pull/1624), merge `805a289a` | Added the small `Elsa.Persistence.EntityFramework` policy package and `Elsa.Secrets.Persistence.EntityFrameworkCore`; kept domain contracts provider-blind; added one shared model with provider-derived contexts, per-provider migrations, a provider guard, module-specific history, and the reviewed EF-surface exception. |
| 2: dual migration modes | [#1633](https://github.com/elsa-workflows/elsa-foundation/pull/1633), merge `b619cbf0`; hardening [#1638](https://github.com/elsa-workflows/elsa-foundation/pull/1638), merge `077a07ea`; operator closeout [#1643](https://github.com/elsa-workflows/elsa-foundation/pull/1643), merge `5441cfc4` | Made runtime auto-migrate/validate and `tools/ef/dual-migrate.sh` use the same migration artifacts; added shell-aware initialization, locking/provider checks, operator diagnostics, and model/projection validation. |
| 3: opt-in composition | [#1634](https://github.com/elsa-workflows/elsa-foundation/pull/1634), merge `2c5d87ae`; audit closeout [#1651](https://github.com/elsa-workflows/elsa-foundation/pull/1651), merge `6787ec0a` | Let a host select `SecretsEntityFrameworkCore` or `SecretsGroundworkPersistence`, never both; retained the Groundwork-default Workbench; proved PostgreSQL host composition; hardened projection repair, paging, diagnostics, and rollback behavior. |
| 4: conditional evidence ownership | [#1646](https://github.com/elsa-workflows/elsa-foundation/pull/1646), merge `addcb178`; evidence closeout [#1652](https://github.com/elsa-workflows/elsa-foundation/pull/1652), merge `0a6a6595` | Kept the 35-row Groundwork-selected composition while adding the 34-row EF alternate that omits only `secrets-repository`; separated EF and Groundwork CI ownership; corrected current-state contracts and recorded the acceptance evidence. |

The spike that selected provider-derived contexts over FluentMigrator remains in
[#1622](https://github.com/elsa-workflows/elsa-foundation/pull/1622). Its merge/retention disposition
belongs to the ADR decision in #1628 rather than to this report.

## Integrated evidence

The final integrated baseline is the post-merge `main` commit `0a6a6595`. Its required gates are:

- [CI](https://github.com/elsa-workflows/elsa-foundation/actions/runs/34619336872): build/test,
  architecture guards, Secrets EF PostgreSQL composition, and the Groundwork native-provider
  matrix all passed.
- [HTTP workflow performance](https://github.com/elsa-workflows/elsa-foundation/actions/runs/34619337005):
  the SQLite-default, production-shaped Workbench journey passed.
- [Maps](https://github.com/elsa-workflows/elsa-foundation/actions/runs/34619336672) and
  [solution filters](https://github.com/elsa-workflows/elsa-foundation/actions/runs/34619336756)
  passed.
- [Packages](https://github.com/elsa-workflows/elsa-foundation/actions/runs/34619336779) and
  [main code quality](https://github.com/elsa-workflows/elsa-foundation/actions/runs/34619335413)
  also passed.

The final corrective heads received explicit exact-head approval:

- Phase 2 hardening [#1638](https://github.com/elsa-workflows/elsa-foundation/pull/1638) and
  operator closeout [#1643](https://github.com/elsa-workflows/elsa-foundation/pull/1643) were
  Copilot-approved on their final heads.
- Phase 3 closeout [#1651](https://github.com/elsa-workflows/elsa-foundation/pull/1651) was approved
  at `df0135a6`, with no independent P0-P3 findings and all review threads resolved.
- Phase 4 evidence closeout [#1652](https://github.com/elsa-workflows/elsa-foundation/pull/1652)
  was independently reviewed with no P0-P3 findings and Copilot-approved at `dbeb6682` after
  reviewing all four changed files with zero comments.

Some earlier implementation PRs ended with comment-only or pre-final-head review records. This
report does not relabel those histories as exact-head approvals. Their delivered state was instead
challenged through the later corrective PRs, the Phase 4 current-state audit, and the integrated
post-merge gates above. Skipped alert/optional jobs are not counted as passed evidence.

## Parent success criteria

| #1626 criterion | Verdict | Evidence and qualification |
|---|---|---|
| Phase 0 + 1 through Phase 4 are merged, or equivalent scope is demonstrably complete | **PASS** | The eight merged PRs above cover the four phases plus their corrective closeouts. |
| Secrets runs on EF with SQLite and at least one of PostgreSQL or SQL Server, with tests | **PASS** | SQLite is covered by the EF repository/host suites; the dedicated hosted PostgreSQL composition job passes the complete project; SQL Server has its own derived context, migrations, and provider project tests. The pilot does not claim Oracle or MongoDB EF support. |
| Runtime `AutoMigrate` and `Validate`/CI out-of-process modes use the same artifacts | **PASS** | Phase 2 and its two hardening PRs use the module migration sets for both shell initialization and `tools/ef/dual-migrate.sh`, including provider/history/model validation. |
| A host selects EF or Groundwork without changing `ISecretRepository` | **PASS** | Phase 3 preserves the domain port and enforces order-independent mutual exclusion; the two composition artifacts prove the selected-family boundary. |
| The architecture/EF ratchet stays green with an explicit reviewed allowlist | **PASS, provisional policy** | The architecture gate passes and the exception is narrow. It is a pilot review exemption, not acceptance of ADR 0072. |
| Groundwork-default Workbench users do not regress without an accepted default change | **PASS, scoped to the pilot** | Both Workbench shell files still select Groundwork Secrets, architecture tests guard that default, and the post-merge SQLite-default HTTP journey passed. This does not claim all Workbench health; the unrelated continuation-signing defect remains #1641. |

No criterion depends on treating an unavailable local container as a pass. Native provider evidence
comes from the hosted jobs; local provider skips were reported as skips.

## Residual risks and ownership

| Risk | Current disposition |
|---|---|
| Product policy still says Groundwork-only first-party persistence | Human decision and source-of-truth reconciliation: [#1628](https://github.com/elsa-workflows/elsa-foundation/issues/1628). Proposed [ADR 0072](https://github.com/elsa-workflows/elsa-foundation/pull/1623) remains unmerged and `proposed`. |
| The real Foundation Host/Nuplane directory-feed route cannot yet share unsigned `CShells.Abstractions` identity reliably and can report a zero-feature shell ready | Production-readiness corrective issue: [#1644](https://github.com/elsa-workflows/elsa-foundation/issues/1644). The pilot used its explicitly allowed equivalent package-feed shell proof; #1644 blocks calling the current directory-feed route production-ready, not the narrower technical verdict. |
| API shape and persistence selection are proven in separate in-process suites, not one real HTTP/restart journey | Production-evidence follow-up: [#1653](https://github.com/elsa-workflows/elsa-foundation/issues/1653). It must drive the Secrets API with EF selected across a process restart without flipping the checked-in default or conflating the Nuplane defect in #1644. |
| Three migration folders and snapshots per relational module create review/maintenance cost | Apply an explicit per-module admission check and measure snapshot churn. Proposed ADR 0072 D11 retains FluentMigrator only as a threshold-triggered escape hatch, not the next default. |
| Secrets normalized search keys are a persisted compatibility contract | The v1 algorithm pins Unicode 16 simple-uppercase data plus the exact 26 additional mappings observed on the Phase 1 .NET 10 host. Runtime casing APIs are excluded. Any future change needs a new algorithm id, explicit backfill/data migration, and old/new lookup continuity tests; v1 must not be regenerated in place. |
| MongoDB has no EF implementation in this pilot | Keep Mongo as an optional second family. A module that requires Mongo must retain or add a document-family adapter with its own evidence rather than pretending EF is cross-family. |
| Runtime hot-path suitability was not tested | Runtime checkpoint, queue, placement, outbox, timer, lease, fencing, and distributed-lock work stays on Groundwork under the existing Runtime/G8 gates. A later ADR and workload evidence are prerequisites to reconsidering it. |

## Tracked work

Every concrete next action from this verdict has an issue. None of these issues silently authorizes
a broader rollout:

| Issue | Work | Gate |
|---|---|---|
| [#1628](https://github.com/elsa-workflows/elsa-foundation/issues/1628) | Explicitly accept, revise, or reject ADR 0072 and reconcile ADR 0042, the Zero-EF program goal, and spike #1622. | Human decision; broader replacement is blocked. |
| [#1644](https://github.com/elsa-workflows/elsa-foundation/issues/1644) | Repair the Foundation Host/Nuplane unsigned shared-assembly route and fail readiness for unavailable configured features. | Tracked post-pilot residual, not a dependency for closing #1632 or the narrow #1626 pilot; required before claiming directory-feed production readiness. |
| [#1653](https://github.com/elsa-workflows/elsa-foundation/issues/1653) | Add a production-shaped Secrets HTTP CRUD/resolution/restart journey with EF selected. | Required before claiming end-to-end HTTP evidence. |
| [#1654](https://github.com/elsa-workflows/elsa-foundation/issues/1654) | Inventory eligible modules and transaction boundaries, define the reusable delivery contract, and create one worker-ready issue per admitted module. | Blocked on #1628; planning only, no implementation authority. |
| [#646](https://github.com/elsa-workflows/elsa-foundation/issues/646) | Own broad native-provider and performance evidence for diagnostics/persistence workloads. | Existing evidence program; this pilot does not duplicate or claim its verdict. |

Unicode-table stability, Mongo demand, and Runtime exclusion are trigger-based constraints rather
than active implementation. #1654 owns the classification: it must create a focused issue before
any trigger is crossed. The unrelated Workbench continuation-signing defect remains tracked in
[#1641](https://github.com/elsa-workflows/elsa-foundation/issues/1641); this report neither closes it
nor treats it as a Secrets regression.

## Conditional Groundwork-to-EF replacement plan

This plan is **not active implementation authority**. It becomes executable only after #1628
records explicit human acceptance of the final ADR text, including another review round if the ADR
is revised. Until then, ADR 0042 and the active
[Zero-EF Persistence](../program-goals/zero-ef-persistence.md) goal remain authoritative.

### Admission rule

A domain may enter an EF replacement wave only when all of these are true:

1. Its core contract is provider-blind and bounded; callers do not consume `DbContext`,
   `IQueryable`, provider SQL, or EF entities.
2. Relational persistence is the intended product family and MongoDB is not a mandatory parity
   promise for that domain.
3. Its transaction, concurrency, query, ordering, tenancy, and retention semantics can be written
   as module-owned tests before implementation.
4. It is not a Runtime/G8 hot-path store and does not require Groundwork operational primitives.
5. Three provider-derived migration sets are small enough to review and maintain; otherwise stop
   and evaluate the D11 threshold instead of adding another schema framework by reflex.
6. The owning team accepts data conversion, rollback, compatibility, and on-call responsibility.

Failing one condition means **keep Groundwork** or open a focused architecture decision. It does
not mean weakening the condition to keep a wave moving.

### Wave 0 — decide and reconcile governance

Owner: #1628.

Current broader-rollout state: `none/free-flow`. No replacement module is admitted or scheduled.

- Explicitly accept, revise, or reject ADR 0072.
- Merge/close PR #1623 and dispose of spike PR #1622 without losing its evidence.
- Reconcile ADR 0042, the Zero-EF program goal, and the EF architecture guard.
- If accepted, create a named replacement program bucket; do not leave a multi-module rollout as
  `none/free-flow`.

No default switch or new module migration starts before this wave completes.

### Wave 1 — close the pilot's production-evidence gaps

- Complete #1644 or explicitly choose a different supported package-feed route before claiming
  dynamic Foundation Host production readiness.
- Complete #1653 so API behavior and EF selection are proven in one production-shaped HTTP/restart
  journey rather than inferred by joining separate in-process suites.
- Preserve Workbench's Groundwork default. These follow-ups harden evidence; they do not authorize
  another module or a default switch.

### Wave 2 — inventory and standardize before naming a second module

Owner: #1654, blocked on #1628.

- Inventory every candidate's actual transaction and consistency topology before ordering modules.
- Resolve whether a shell may host different relational engines for different EF modules, or must
  supply one engine consistently, before the recipe becomes a shared contract.
- Define the reusable contract for module-owned provider-derived contexts, migration history,
  runtime/out-of-process apply, mutual exclusion, conversion/rollback, and evidence retention.
- Create one worker-ready issue only for each admitted module. Freeze new Groundwork expansion only
  after that domain is admitted; retain its existing adapter as the rollback path until retirement.

### Wave 3 — conditional candidates and explicit holds

- **Studio Preferences:** strongest candidate for the second canary because its current surface is
  small and key/value-shaped, but not pre-approved. Its inventory must still prove tenancy,
  concurrency, conversion, and host-composition semantics.
- **Publishing:** **hold**. Publication currently stages Design, Runtime, and Publishing rows in one
  Groundwork transaction and rejects a split-target host. Do not migrate Publishing independently
  until a focused architecture decision defines a safe cross-family transaction topology.
- **Dashboard:** **hold** until its Design/Runtime projection and consistency dependencies are
  inventoried. Small store size does not make cross-domain projections simple.
- **Workflows Design and Activities Design:** **not automatic replacements**. Current architecture
  deliberately places them on Groundwork-only lanes. An EF lane would introduce a new optional
  family unless governance explicitly changes that decision; the shared atomic-write refactors in
  #1619/#1620 do not erase the separate ledgers or transaction semantics.
- **Foundation Identity:** evaluate later with a dedicated security, tenancy, credentials, claims,
  roles, external-identity, framework-compatibility, and conformance decision.
- **OpenIddict:** retain the vendor persistence boundary from ADR 0042. Do not merge the vendor
  `DbContext` with Elsa IAM stores.
- **Structured Logs and OpenTelemetry:** keep separate from this rollout; append/query/retention and
  performance evidence remains under #646.

If #1654 admits Studio Preferences or another module, that module becomes one thin vertical slice:
one domain contract, one conversion path, one selected-family composition, one rollback, and one
issue. Do not bundle multiple domains merely to make a wave look complete.

### Permanent hold — Runtime/G8 hot paths

Runtime checkpoint, execution state, scheduler queues, durable timers, outbox, placement,
transport, leases, fencing, and distributed locks do not enter these waves. Reconsideration needs a
separate ADR plus correctness, contention, crash-recovery, idempotency, and performance evidence on
the production workload shapes. Until then, Groundwork remains the implementation family.

### Per-module delivery template

Every admitted module gets its own issue and review-converged PR sequence:

1. Freeze semantics and exclusions in a worker-ready issue; identify the Groundwork rollback path.
2. Add EF contract tests first, sharing fixtures with the Groundwork implementation where behavior
   must be identical.
3. Add one module package with shared model/configuration and provider-derived SQLite, SQL Server,
   and PostgreSQL contexts; the module references EF Core + Relational only.
4. Generate and review all three migration/snapshot folders. Enforce provider/model drift and the
   module-specific history table.
5. Implement provider-appropriate OCC, types, normalized/search keys, indexes, paging, and redacted
   diagnostics; do not assume one mapping works everywhere.
6. Prove runtime `AutoMigrate`, startup `Validate`, and out-of-process apply against the same
   artifacts, including concurrent startup and wrong-provider failure.
7. Add order-independent EF/Groundwork mutual exclusion and two explicit composition artifacts.
8. Rehearse export/transform/import, cutover, rollback, and mixed-version deployment. Do not delete
   Groundwork in the same step as first enabling EF.
9. Run focused tests, affected full projects, provider jobs, backend e2e, architecture/map/filter
   gates, exact-head independent and Copilot review, and post-merge `main` gates.
10. Switch the default only in a separate PR after production-shaped evidence; retire the old
    Groundwork adapter only after the rollback window and compatibility decision close.

## Decision required

The evidence supports the **bounded direction** in ADR 0072: EF-first for admitted, shape-simple
relational modules; Groundwork retained for Runtime hot paths and document/Mongo or operationally
specialized domains; OpenIddict left at its vendor boundary. The recommendation is **revise, then
accept**, adding the pilot's material lessons before acceptance:

1. Shell-scoped runtime migration must support the CShells lifecycle (`IShellInitializer`), not
   rely only on host-scoped `IHostedService` execution.
2. Selected-family composition and evidence-ledger ownership are part of the product contract;
   an alternate persistence choice must own an alternate proof path.
3. OCC, physical types, normalization, and persisted search keys are provider-specific contracts,
   not a portable `IsRowVersion()` or runtime-casing convention.
4. #1644 must close before the Foundation Host directory-feed route is called production-ready.
5. Every existing-domain replacement must include an explicit data conversion and rollback gate;
   the greenfield Secrets pilot did not prove that part.

Sipke must record **accept**, **revise**, or **reject** in #1628. No agent should infer the answer
from this successful pilot.
