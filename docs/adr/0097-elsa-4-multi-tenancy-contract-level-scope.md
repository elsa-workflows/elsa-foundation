---
status: proposed
date: 2026-11-11
---

# Elsa 4 multi-tenancy: contract-level scope, shared-store discriminator baseline, shell-per-tenant hybrid

Status: proposed (2026-11-11). Draft for [program #4131](https://github.com/elsa-workflows/elsa-foundation/issues/4131)
and [issue #4133](https://github.com/elsa-workflows/elsa-foundation/issues/4133), from assessment
[#4123](https://github.com/elsa-workflows/elsa-foundation/issues/4123) §0, §4, §5 and §8. Owner questions Q1–Q5
and Q12 carry recommended defaults marked **pending owner**. Agreement needs the named architect approvers
Sipke Schoorstra (@sfmskywalker), Joey Barten (@j03y-nxxbz) and Frans van Ek (@FransVanEk) on the latest
commit; bot review is not architectural agreement and must not merge this ADR alone.

Program goal: the multi-tenancy program is authorized by the owner's 11 November 2026 chat with Elsa 4 Lead.
Recording it in [ADR 0080 D0](0080-elsa-4-simplification-decisions.md#d0--focus) and the
[program-goal registry](../program-goals/README.md#registry) is [issue #4135](https://github.com/elsa-workflows/elsa-foundation/issues/4135),
not this ADR.

Meanings of **persistence scope**, **tenant id** and **shell** live in the
[Elsa glossary](../glossary/elsa.md) and the [root glossary](../glossary/root.md). This ADR does not repeat them.

## Context

Elsa 4 already has a provider-neutral tenancy seam (`PersistenceAccessContext`, `PersistenceScope`, the scope
source, runner and factory) but no written invariant. In the code, "tenant" currently names three different
things: the persistence partition, the principal tenant claim, and a CShells shell (the Tasks module comments
that say a task is "executed for each tenant"). One ADR fixes the rules so Phase 1, the two-tenant conformance
suite and the identity work build on the same decisions.

The product goal is to host many tenants safely in one process and database: a tenant is resolved per request,
bound once, carried explicitly through every asynchronous boundary, and isolated in every store. Context:
Elsa Cloud's high-density hosting on Elsa 3 (elsa-control #633); Elsa 4 should be able to host the same way
later. Elsa 3 is a **reference only** for the resolver-pipeline idea, host/header/route/claim resolvers,
lifecycle events and per-tenant fairness. This ADR does not copy Elsa 3's ambient `AsyncLocal` plus global-filter
design, or its three-valued tenant visibility (`""`, `null`, `*`).

## Relation to existing ADRs

This ADR **amends none** of the following. If a later change must alter one of them, that change is an
explicit amendment of that ADR, not a silent override here.

| ADR | Relation |
|---|---|
| [0073 D2](0073-ef-core-is-the-only-first-party-persistence-family.md#d2--domain-contracts-remain-provider--and-persistence-neutral) | Tenancy is a domain-owned store-contract and access-context concern. Domain contracts stay provider-neutral. EF Core remains the only first-party durable family (0073 D1). In-memory stores are not a persistence family; they honour the same contract and must pass the same suite. This ADR does not add a provider SPI below the stores (Q1). |
| [0085 D6](0085-elsa-3-parity-identity-administration-and-scope.md#d6-host-wide-identity-for-v1-designed-for-per-tenant-later) | Identity providers and users stay host-wide in v1; every identity record already carries its tenant id. Tenant resolution, which D6 left "decided separately, token claim first, then host or header", is decided here as section (c). Pulling request tenant resolution forward from parity Phase 3 is sequencing (Q2), not an amendment of D6. Per-tenant identity providers remain a later design, as D6 already said. |
| [0085 D5](0085-elsa-3-parity-identity-administration-and-scope.md#d5-unknown-external-users-are-rejected-by-default) | Unchanged. External-identity links stay keyed by `(tenant, provider, issuer, subject)`. |
| [0080 D0](0080-elsa-4-simplification-decisions.md#d0--focus) | Unchanged. A program becomes active only through an owner amendment to D0 that names it. This ADR does not write that amendment; #4135 does. The remaining 0080 decisions (simplification, public surface, persistence modes, tests) continue to apply to the multi-tenancy work as to any other. |

Sibling work: embedding program [#4132](https://github.com/elsa-workflows/elsa-foundation/issues/4132) and
[#4150](https://github.com/elsa-workflows/elsa-foundation/issues/4150) consume section (g);
[#4145](https://github.com/elsa-workflows/elsa-foundation/issues/4145) is the identity Phase 1 tenant-awareness
addendum and does not change 0085 D6;
[#4146](https://github.com/elsa-workflows/elsa-foundation/issues/4146) reported in (g); the crash is
[#4158](https://github.com/elsa-workflows/elsa-foundation/issues/4158).

## (a) The invariant

The bound [persistence scope](../glossary/elsa.md#persistence-scope) is authoritative for every persistence
operation on tenant-keyed data.

### Decided

- Tenancy lives at the store-contract and provider-abstraction level, never tightly coupled to EF Core.
  Correctness comes from the domain-owned store contracts and `PersistenceAccessContext` (ADR 0073 D2), not
  from EF-only mechanisms. Global query filters, `DbContext` model tricks and interceptors are **not** the
  isolation mechanism and must not become it.
- The bound scope is present in every tenant-keyed key, unique index and natural key.
- An explicit tenant id or scope on a domain object is **verified against** the bound scope. It does not
  select the partition. A mismatch is a failure that must not disclose another partition's identity.
- The scope is propagated **explicitly** across every asynchronous boundary (scheduler work, bookmarks,
  timers, leases, outbox, recovery, locks, caches, extension stores and message envelopes). Ambient access
  is allowed only inside one DI scope after a binder has bound the context for that scope.
- In-memory stores honour the same contract and must pass the same two-tenant conformance suite as the EF
  stores on all four first-party engines.
- Background pumps, leases and outbox delivery outside a bound scope **fail closed**. They must not default
  to `"default"` or invent a tenant.

### Proposed

- **No nullable tenant and no agnostic tenant** in Elsa 4 (unlike Elsa 3's `""`, `null` and `*`). Shared or
  system data (activity catalog, templates) lives in a separate host-level store, not under a wildcard
  tenant. Recommended default for Q4, **pending owner**.
- Today's `PersistenceAccessContext.Global`, `PrivilegedGlobal` and `PrivilegedAcrossScopes` shapes are not
  a tenant-keyed visibility mode. Tenant-keyed stores require one ordinary bound scope. Host-wide stores
  (section (e)) do not use a persistence scope at all. Privileged maintenance of tenant-keyed data still
  names one scope, or, in Phase 3, an explicit [support context](../glossary/elsa.md#support-context).
- `EnsureTenantScope` treating a missing tenant id as "use the bound scope" is replaced, once Q4 is agreed,
  by verification that every tenant-keyed write carries a tenant id equal to the bound scope.
- An optional provider-neutral guard (architecture test plus store-side `RequireScope()`) replaces the
  safety-net role Elsa 3 gave to global query filters. It is defense in depth, never the only line.

### Deferred

- The exact privileged-purpose catalog for maintenance operations that today use global or across-scope
  access.
- PostgreSQL row-level security, or any other engine-specific extra, as an optional defense on one
  provider. Not v1, and never a substitute for the contract.
- Closing the unscoped stores named in program #4131 Phase 2 (`EfRuleRunStore`, `EfEtlConnectorSourceBudget`,
  `EfWorkflowExecutionDeletionStore`, partial `EfWorkflowLibraryStore`, and the rest of the 13-store audit).
  The invariant applies to them; the audit is Phase 2.

## (b) Vocabulary

### Decided

- The terms **persistence scope**, **tenant id** and **shell** are defined in the glossary, not here.
  New docs, issues and code comments use those terms and do not invent synonyms.
- A [shell](../glossary/root.md) is a CShells composition unit. It is not a tenant and not a persistence
  scope. `Elsa.Tasks` comments that say a startup, recurring or background task is "executed for each
  tenant" mean for each **shell**. Updating those comments is follow-through of this vocabulary, not a
  new decision.

### Proposed

- Identity and catalog language keep saying **tenant** / **tenant id** for the principal and the tenant
  catalog row. Persistence language says **scope** for the bound partition. The two values are equal for
  a given request once the resolver has bound the scope; they are still different kinds of thing.
- Startup tasks run once per shell, not per tenant (`TaskManager.RunStartupTasks`, one DI scope).
  Per-partition visits use `IPersistenceScopeRunner`. `IStartupTask` XML that says "executed for each
  tenant" is stale; updating it is follow-through of this vocabulary, not a TaskManager change
  (section (g)). Shell activation remains the CShells lifecycle; it does not iterate tenants.

### Deferred

- Renaming public types (`PersistenceScope`, claim type `elsa.identity.tenant_id`, CShells `Shell`) is
  not required by this ADR. A rename is a later public-surface change under ADR 0080 D3 if anyone proposes
  one.
- Studio copy for the tenant switcher (Phase 3).

## (c) The resolution pipeline and order

### Decided

- [ADR 0085 D6](0085-elsa-3-parity-identity-administration-and-scope.md#d6-host-wide-identity-for-v1-designed-for-per-tenant-later)
  already requires token claim first, then host or header. This ADR fills that "decided separately"
  slot; it does not change D6.
- First match wins. None resolved means **refuse**, not default to `"default"`.
- A claim / host (or claim / header, or claim / route) mismatch is **403**, never a silent switch to the
  claim or to the host.
- After resolution, request middleware binds `PersistenceAccessContext.Scoped(scope)` once per DI scope
  and stamps the tenant on the principal's permission-evaluation context.
- Anonymous workflow HTTP endpoints resolve tenant by host or route only; they do not read an untrusted
  header.

### Proposed

Resolver order, first match wins:

1. Token claim `elsa.identity.tenant_id` (authenticated API and Studio).
2. Host name (custom domains, Studio handoff). Needs a tenant-to-host table in the catalog.
3. Trusted header (service-to-service, internal tools), only when the caller is authenticated with
   permission to select a tenant. No anonymous header selection.
4. Route prefix (path-based multi-tenant HTTP triggers), against a tenant-qualified HTTP route table.

Recommended default for Q10's "which resolvers are required" is: claim is required wherever there is a
principal; host and route are required for HTTP-trigger hosting; header is opt-in and trusted. **Q10
itself stays open** through Phase 1.

The host-auth bridge in embedding program #4132 (#4152 / #4153) must map a host principal onto this
pipeline the same way #4139 does: the bridge produces a tenant id that the binder verifies; it does
not bypass the bound scope.

### Deferred

- Tenant membership of one tenant versus several, and whether several is token exchange or choice at
  sign-in (Q6). Phase 3.
- Studio tenant switcher (Phase 3). Until then, Studio shows the current tenant and does not offer a
  switcher ([#4145](https://github.com/elsa-workflows/elsa-foundation/issues/4145)).
- Custom-domain catalog schema beyond "the catalog can carry a host name".

## (d) The cross-tenant admin model

### Decided

- A wildcard role, a `*` tenant, or a permission that implies every tenant, is **not** the v1 model.
  Cross-tenant work must not bypass the bound scope by omitting it.
- Generic host administration is not a tenant-support role.

### Proposed

- Adopt the explicit **support context** from [#838](https://github.com/elsa-workflows/elsa-foundation/issues/838)
  / [#806](https://github.com/elsa-workflows/elsa-foundation/issues/806): target-bound, expiring, audited,
  materializing only a named support profile's tenant-scoped grants for exactly one tenant. Recommended
  default for Q7, **pending owner**. Delivery is Phase 3 of program #4131.
- Entering support rebinds the persistence scope to the target tenant for the lifetime of that context.
  It is never represented as a durable tenant assignment or a cross-scope permission implication.

### Deferred

- Support-profile catalog, Studio management-bridge UX, audit schema, and whether a host-admin
  permission is distinct from tenant-admin permissions. Phase 3.
- Whether users are members of one tenant or several (Q6), which shapes how support is requested.

## (e) Host-wide versus tenant-keyed data

### Decided

- Every tenant-keyed store includes the bound scope in its keys and predicates. A store that is
  host-wide is documented as host-wide and is ineligible for the two-tenant isolation assertions.
- OpenIddict remains a vendor-owned EF Core boundary (ADR 0073 D2). Elsa does not combine the
  OpenIddict `DbContext` with Elsa IAM contexts.

### Proposed

Host-wide in v1 (not tenant-keyed):

- OpenIddict tables (host-wide, with tenant claims on tokens). Recommended default for Q9, **pending
  owner**.
- DataProtection keys.
- EF migration history (per database, which is host-wide under the discriminator baseline and
  per-tenant-database under the hybrid).
- Cluster membership (ADR 0078).
- Identity **providers** and the identity protocol plane (ADR 0085 D6). Users, roles and API keys are
  tenant-keyed records behind those host-wide providers.

Tenant-keyed in v1:

- Workflow design, publishing and runtime state.
- Secrets, connections, bookmarks, timers, outbox, leases, user tasks, alterations, and the identity
  IAM records (users, roles, memberships, API keys) that already carry a tenant id.
- Extension stores that hold tenant data (Agents, ETL, Rules, Weaver, durable messaging), once the
  Phase 2 audit says so.

Shared / system data (activity catalog, templates), if Q4 is agreed, lives in a **separate host-level
store**, not in a tenant-keyed table under `*` or `""`.

### Deferred

- Per-tenant identity providers (Q8). ADR 0085 D6 already says v1 is host-wide and designed for
  per-tenant later. High-density hosting does not move that forward in this ADR.
- Per-tenant OpenIddict stores under the hybrid (Q9 alternative).
- Quotas, per-tenant metrics, export/delete and lifecycle hooks (Q11, Phase 4).

## (f) v1 non-goals, and the hybrid as opt-in

### Decided

- v1 storage is the **shared-table discriminator**: one database, scope as a column and as part of
  every key. This is today's physical model, written as the contract-level baseline.
- Schema-per-tenant is **rejected**. It is not portable across the four first-party engines (MySQL
  "schema" is a database; SQLite has none), and migrating N schemas is a new orchestrator for no
  gain over discriminator or database-per-tenant. Recommended default for Q3, **pending owner**.
- A non-EF provider SPI, or a real provider layer below the stores, is a v1 non-goal unless Q1 is
  answered "stronger".
- Copying Elsa 3's tenant model, Elsa 3 tenant import, and quotas/billing are v1 non-goals. Elsa 3
  import of identities and of `*` agnostic rows belongs to parity Phase 3 (Q5).
- A Studio tenant switcher is not v1 (it is Phase 3).
- Elsa Cloud control-plane work is out of scope.

### Proposed

- **Database-per-tenant via shell-per-tenant** is an **opt-in hybrid**, Phase 5, only if a Dedicated
  tier needs it. It reuses the Phase 1 contract unchanged: the bound scope is still the key; a
  connection factory (scope → connection string, the string itself a Secrets reference, never inline)
  selects the physical database; EF migrations run per database. In-memory is one instance per
  tenant, not a connection factory. Recommended default for Q3, **pending owner**.
- Elsa 3 `*` agnostic rows, on import, are kept as a shared host-level catalog rather than copied
  into every tenant. Recommended default for Q5, **pending owner**. Copying would drift; a host-level
  catalog matches Q4.

### Deferred

- Migration orchestration over N databases, per-tenant database provisioning, density measurements
  ([#2757](https://github.com/elsa-workflows/elsa-foundation/issues/2757)), and shell-per-tenant
  composition details. Phase 5.
- Whether the hybrid is ever built. It can be cut if demand is not there.

## (g) The single fixed tenant, no identity features default

An embedded host ([program #4132](https://github.com/elsa-workflows/elsa-foundation/issues/4132),
[#4150](https://github.com/elsa-workflows/elsa-foundation/issues/4150)) must start with **one fixed
tenant and no identity features**. That is a supported default of this ADR, not an exception to (a).

### Decided

- There is no tenant-less persistence. Every tenant-keyed operation still has a bound persistence
  scope. "No tenant set up" in the embedding program means **the host author configures none**: no
  tenant catalog, no resolver pipeline, no identity stores. The host binds one scope for the process.
- That scope is `PersistenceScope.DefaultValue` (`"default"`), matching
  `AspNetCoreIdentityDefaults.DefaultTenantId`, unless the host sets a different single value at
  composition. The value is ordinary; it is not a reserved agnostic token.
- Identity features are optional. The authoring stack (Design, Publishing, activity reconciliation)
  must activate without them.
- The same store contracts apply. Pumps fail closed on a missing bound scope; they do not invent a
  second tenant and they do not skip isolation because the host has only one.
- A later multi-tenant composition of the same host adds the resolver pipeline and catalog on top of
  this default. It does not replace the contract.
- The authoring-stack startup crash in a host with no tenant set up is a small store-contract bug
  ([#4158](https://github.com/elsa-workflows/elsa-foundation/issues/4158)), found by spike
  [#4146](https://github.com/elsa-workflows/elsa-foundation/issues/4146). `EfDesignAtomicWrite`
  treats a null request tenant as the bound `"default"` and rejects staged host-wide catalog rows.
  It is not identity seeding and is not blocked on this ADR. Host-wide catalog rows stay `TenantId`
  null (section (e)); they are never stamped `"default"`. Workbench does not hit it on the default
  composition; Foundation.Host does not ship authoring features.
- Startup tasks run once per [shell](../glossary/root.md), not per tenant.
  `TaskManager.RunStartupTasks` uses one DI scope per shell. `IPersistenceScopeRunner` is the
  per-partition visitor. `IStartupTask`'s XML ("executed for each tenant") is stale relative to the
  [startup task](../glossary/root.md) glossary entry; updating it is follow-through of (b), not a
  code change in this ADR.

### Proposed

- #4139 ships a **fixed-tenant scope source** used by this default: it binds the configured scope at
  composition and for every DI scope, with no HTTP resolution. The embedding preset selects it.
- Host-wide stores in (e) stay host-wide in this default. Cluster membership, migration history and
  DataProtection keys do not become per-tenant because the host has only one tenant.
- The host-auth bridge (#4152) maps an embedded host principal onto this single tenant id; it does
  not leave the persistence scope unbound.

### Deferred

- Multi-tenant embedding (a later embedding-program phase).

## Owner questions Q1–Q5 and Q12

Q1–Q5 and Q12 gate this ADR. Each row is a **recommended default, pending owner**. An owner answer
during review replaces the recommended default and is written into the corresponding section.

| Q | Question | Recommended default (pending owner) | Lands in |
|---|---|---|---|
| Q1 | What does "provider abstraction" cover? | Domain-owned store contracts (ADR 0073 D2). EF is the only first-party durable family. Tenancy lives at the contract / access-context level. Not a real provider SPI that non-EF implementations plug into. | (a), (f) |
| Q2 | Timing and priority: pull multi-tenancy forward, or keep it parity Phase 3? | Run Phase 0 and Phase 1 of program #4131 now, beside identity Phase 1. This pulls request tenant resolution forward from ADR 0085's Phase 3 without amending D6. Registry / D0 bookkeeping is #4135. | Relation, (c) |
| Q3 | Tenancy model? | Discriminator baseline, optional database-per-tenant hybrid later, schema-per-tenant rejected. | (f) |
| Q4 | No nullable and no agnostic tenant; shared data in a host-level store? | Agree. | (a), (e) |
| Q5 | Elsa 3 import of `*` agnostic rows? | Keep as a shared host-level catalog, not copy into every tenant. Parity Phase 3 owns the import. | (f) |
| Q12 | Who owns the effort, and may the Engineer start Phase 0 and the Phase 1 suite now? | Elsa 4 Engineer drafts; Elsa 4 Code Review plus the named architect approvers review. The Engineer may start the Phase 0 ADR (this document) and the Phase 1 conformance suite now. ADR and spec PRs still need the named architect approvers and must not merge on bot review alone. | this ADR, #4142 / #4143 |

## Open questions

These stay open through Phase 1. They do not block this ADR or the Phase 1 children. They gate later
phases.

| Q | Question | Gates |
|---|---|---|
| Q6 | Are users members of one tenant or several? If several, token exchange or tenant chosen at sign-in? | Phase 3, (c), (d) |
| Q7 | Confirm the support-context model versus a wildcard role. | Phase 3, (d); recommended default is support context |
| Q8 | Identity per tenant in v1? (ADR 0085 D6 says host-wide.) | Phase 3+, (e) |
| Q9 | OpenIddict: host-wide tables with tenant claims, or per-tenant stores under the hybrid? | Phase 3 / 5, (e); recommended default is host-wide |
| Q10 | Which resolvers are required for hosting (host, path prefix, header)? | Phase 2 routing, (c) |
| Q11 | Quotas and fairness in v1 or after? | Phase 4, (e) |

Also open:

- The 13-store unscoped audit and the two-scope behaviour of timers, stimulus and outbox are inferred
  from code, not demonstrated. [#4134](https://github.com/elsa-workflows/elsa-foundation/issues/4134)
  validates the estimate; confirmed leaks are filed separately.
- Exact privileged-purpose catalog for maintenance (section (a) Deferred).

## Alternatives considered

- **Leave the invariant unwritten.** The seam already exists and SC-005 / #3701 already show
  cross-tenant failures. Without a written rule, Phase 1, identity Phase 1 and embedding each invent
  a different "tenant". Rejected.
- **Copy Elsa 3: `AsyncLocal` ambient tenant, EF global query filters, three-valued visibility
  (`""` / `null` / `*`).** Familiar to Elsa 3 hosts, and the filters look like a safety net. They
  couple isolation to EF, they fail for in-memory and for any non-filter store, they hide missed
  predicates, and the three values are a standing null-versus-empty bug. Hard-rejected by the
  program's requirements.
- **Schema-per-tenant as v1 or as the hybrid.** Stronger isolation than a discriminator without N
  databases. Not portable across SQLite and MySQL; migration history and model cache are per context
  type today. Rejected (Q3).
- **Database-per-tenant as v1.** Strongest isolation and easy export/delete, but highest ops cost,
  and in-memory has no equivalent. The contract would still be the same, so there is no v1 gain in
  making it the baseline. Deferred as the opt-in hybrid (Phase 5).
- **A real non-EF provider SPI now (stronger Q1).** Would change W1/W4 and the test strategy, against
  ADR 0073 D1/D2. Not selected. A later SPI would still have to honour this contract.
- **Wildcard role or `*` tenant for cross-tenant admin.** Cheaper than a support context. It is Elsa
  3's hole: a missed predicate becomes a leak, and `*` is a third visibility value. Rejected as the
  v1 model; Q7's recommended default is the #838 support context.
- **Keep `EnsureTenantScope`'s null skip and `PersistenceAccessContext.Global` as tenant visibility.**
  Matches today's code and Elsa 3 agnostic rows. Conflicts with Q4 and with fail-closed pumps.
  Proposed for removal on tenant-keyed stores; host-wide data moves to host-level stores instead.

## Consequences

- Phase 1 of program #4131 (#4136–#4145) implements this contract: move the primitives, add the
  catalog and resolver/binder, unify store-contract verification, fail closed on pumps, land the
  two-tenant suite and the architecture guard, and apply the identity addendum. Bugs the suite finds
  are filed separately.
- Phase 2 closes unscoped stores, lock keys, extension stores, HTTP routing and the real-host
  second-tenant journey (#3701, tenant half of #4107).
- Identity Phase 1 issues #2775 and #2777–#2782 follow [#4145](https://github.com/elsa-workflows/elsa-foundation/issues/4145)
  without changing their scope or ADR 0085 D6.
- Embedding #4150 implements section (g). The authoring-stack startup crash is
  [#4158](https://github.com/elsa-workflows/elsa-foundation/issues/4158), not a prerequisite of this
  ADR.
- Program #4131 is updated with this ADR's link when the PR merges. #4135 records the program in
  ADR 0080 D0 and the registry; this ADR does not.
- No constitution amendment. Framework §2.20 and ADR 0073 already require provider-neutral contracts
  with EF behind them. Elsa §E2.5 (`ElsaDbContextBase` stays opt-in) is unchanged: this ADR adds no
  DbContext-level tenancy.
