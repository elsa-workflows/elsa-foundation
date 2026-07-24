# Implementation Plan: OpenIddict Groundwork Stores

**Branch**: `codex/106-openiddict-groundwork-implementation` | **Date**: 2026-07-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/106-openiddict-groundwork-stores/spec.md`

## Summary

Replace the EF-backed OpenIddict persistence integration with one concrete `Elsa.Foundation.Identity.OpenIddict.Groundwork` adapter package. The package implements OpenIddict 7.5's four store contracts over four global logical record units while retaining existing OpenIddict server, validation, selector, and `ITokenService` behavior. It uses declared bounded routes, expected-version concurrency, explicit units of work, and shared host schema readiness. Physical form is deliberately unresolved until the preview.81 multivalue probe and #646 comparison select an admissible shape. The work is complete only after real four-provider evidence and #646 performance verdicts allow the host switch and EF OpenIddict deletion.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)

**Primary Dependencies**: OpenIddict.Abstractions/Core/AspNetCore 7.5.0; Elsa Identity abstractions; Groundwork Core, Documents, provider packages, and Tool from one public binary-compatible `0.0.1-preview.81` family; Microsoft.Extensions dependency injection and options

**Storage**: Four global logical record units—applications, authorizations, scopes, and tokens—with canonical JSON authoritative. Preview.81 proves scalar physical-entity projections but cannot attach linked multivalue storage to `PhysicalEntityTable`; shared/dedicated or additional linked units require an explicit design and #646 verdict before production scaffolding. Mandatory providers remain SQLite, SQL Server, PostgreSQL, and MongoDB.

**Testing**: xUnit; existing OpenIddict identity, API, shell, and ASP.NET Core Identity Groundwork acceptance tests; new direct store branch suites; shared real-provider conformance; native route/mutation-plan evidence; restart/failure injection; architecture dependency guard

**Target Platform**: Cross-platform .NET hosts and CI; Docker-compatible SQL Server/PostgreSQL; MongoDB replica set or sharded topology for transaction-required scenarios

**Project Type**: Concrete provider adapter package, host feature composition, shared schema source, test/conformance suites, deployment CLI validation

**Performance Goals**: Submit token issue/lookup/refresh/revoke/prune, authorization filter/revoke, application-client lookup, and scope-resource lookup to #646 with reproducible correctness digests. The shared accepted same-provider comparison and physical-form selection policy decides pass, redesign, or blocked; no timing verdict is inferred from functional success.

**Constraints**: Core contracts stay Groundwork-free; OpenIddict records are explicitly global; arbitrary `IQueryable` delegates are never emulated; all scale-bearing reads and mutations bind to finite declared routes; CAS/UoW decisions survive retry, lost acknowledgement, and restart; EF is temporary oracle only; no greenfield compatibility alias or data migration.

**Scale/Scope**: OpenIddict 7.5 store denominator: application 42 members, authorization 32, scope 28, token 43; four mandatory providers; actual `Elsa.Server`; 1K correctness smoke, 100K acceptance, and 1M where #646 identifies a scale-bearing physical-form comparison.

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design.*

The Elsa and framework constitutions are draft/provisional. This matters because Elsa §E2.5 still describes a temporary EF capability. [ADR 0042](../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) is accepted and governs the repository-product decision; this work follows its narrow provider boundary without silently amending constitutional text.

| Gate | Result | Plan consequence |
|---|---|---|
| Framework §2.9 provider-neutral persistence | PASS | Identity abstractions, `ITokenService`, and OpenIddict-facing domain behavior do not reference Groundwork. |
| Framework §2.10 CQS | PASS | Named reads and mutations remain separate; generic delegate requests never become general query execution. |
| Framework §2.20 provider module decomposition | PASS | New Groundwork code is a concrete provider-suffixed package; no provider-neutral umbrella or second core is introduced. |
| Framework §2.21.1 test objective preservation | PASS | All 54 retained objectives, including nine reached only through the shared token-endpoint host, move to equivalent fixtures; deletion requires recorded architect approval. |
| Framework §2.23.1 registration coverage | PASS | Each feature composition resolves all four OpenIddict managers/stores, schema contributors, and required public collaborators. |
| Framework §2.23.2 branch coverage | PASS | Every logic-bearing mapper, route selector, generic-delegate rejection, CAS/UoW/recovery, exception mapping, and provider-admission branch has direct stubbed-dependency tests. |
| Framework §2.22 documentation/catalog/maps | PASS | Feature documentation, extension-point catalog/index where a new extension point exists, storage/readiness documentation, and authorized generated maps update with delivery. |
| Framework §2.23.5 exception boundary | PASS | Provider/serialization failures translate at the adapter boundary to documented OpenIddict/feature-scoped outcomes; cancellation is preserved. |
| Elsa §E6 naming | PASS | New Elsa-owned types use the provider prefix and one role suffix; external OpenIddict names are retained only when mirroring external contracts. |
| Accepted zero-EF ADR | PASS | OpenIddict is a separate delivery lane inside the zero-EF completion gate. Groundwork is the target first-party persistence family; EF remains only as an oracle until this lane and the shared exit gates pass. |

**Post-probe re-check**: BLOCKED before production store scaffolding. The data model confines physical projections and Groundwork session mechanics to the provider package, but preview.81 exposes linked projection parameters only on non-entity physical forms. The quickstart records the evidence and the design choice remains explicit rather than being improvised in an adapter.

## Hard Prerequisite Gate

No production store task may begin until the exact public `0.0.1-preview.81` Groundwork Core/Documents/provider/Tool family restores together and the following capabilities are demonstrated from its public API with a focused executable probe:

1. Version-aware document codec admission, current/minimum-readable policy, upcaster-chain validation, and rejection before deserialization.
2. Four logical record-unit definitions with a reviewed physical form, deterministic host naming, schema fingerprinting, CLI plan/validate/status/apply, and runtime validate-only admission.
3. Bounded server-side mutation by declared predicate with exact affected count, cancellation, failure cleanup, and provider-native mutation-plan inspection.
4. Typed compound, unique, date/range, and multivalue physical/query support needed by the named OpenIddict routes.
5. Expected-version CAS and transaction-capable UoW behavior across all four providers.

If any capability is absent, incomplete, non-public, or fails its probe, mark this feature blocked at that prerequisite and either link upstream work or record the Elsa design decision required to consume an already-public alternative form. Do not hide the gap with raw provider queries, client filtering, synthetic plan evidence, or a compatibility fallback.

The preview.81 probe proves that `PhysicalEntityTable` has no public linked parameter while the shared/dedicated document forms do. Therefore “four physical entity tables with linked multivalue relationships” is not an implementable contract. Production scaffolding remains blocked until architecture review chooses and #646 evaluates one of:

1. four shared/dedicated logical units using the public linked projection contract; or
2. four entity units plus separately declared linked membership units with explicit atomicity, naming, readiness, and provider evidence.

## Project Structure

### Documentation (this feature)

```text
specs/106-openiddict-groundwork-stores/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── openiddict-store-contract.md
│   └── deployment-schema-contract.md
└── tasks.md                         # Created later by speckit-tasks
```

### Source Code (repository root)

```text
src/Elsa/Foundation/Identity/
├── Abstractions/                     # Retained provider-neutral identity/token contracts
├── OpenIddict/                       # Server, validation, selector, options, public token behavior
│   └── EntityFrameworkCore/           # Temporary oracle; removed only at final exit
└── OpenIddict/Groundwork/             # New concrete stores, documents, manifest, registration

src/Elsa/Persistence/Groundwork/
└── Unified/                           # Existing host-composed schema source extended by feature manifest

src/Apps/Elsa.Server/                 # Feature selection and provider-neutral Groundwork composition

tests/Elsa/Foundation/Identity/Tests/
├── OpenIddict/                        # Existing objectives rewired, not weakened
├── OpenIddict/Groundwork/             # Direct store/registration/mapping branch suites
└── OpenIddict/Conformance/            # Shared black-box four-provider/restart/failure suite

tests/Elsa/Persistence/Groundwork/
├── Conformance/Tests/                 # Preview.81 capability and provider-evidence probes
└── UnifiedHost/Tests/                 # Production-shaped schema/host composition

tests/Elsa/Architecture/               # Provider-neutral core and final EF removal guards
```

**Structure Decision**: Keep OpenIddict server/validation behavior in its existing feature package and add one provider-suffixed implementation package. Do not move provider mechanics into identity abstractions, add a new core, or retain a separate EF feature after the exit gate.

## Delivery Sequencing

1. **Verify upstream capability**: record exact public preview.81 packages/tool, provider topology, public capability probes, and baseline test identities. Stop for upstream work or an explicit Elsa physical-form decision if a hard prerequisite fails.
2. **Freeze contract denominator**: encode all 145 OpenIddict store members by capability group, descriptor round trips, named route catalog, generic-delegate rejection, current token-service behavior, and every legacy test objective.
3. **Build the provider boundary**: only after the physical-form gate passes, add the Groundwork package, four-unit manifest, codec policies, physical field/index declarations, scoped registration, and Core builder replacement of all four stores.
4. **Implement named behavior**: add document mapping, CRUD/accessors, deterministic pages/counts, typed/multivalue named lookups, stable capability errors for unsupported generic delegates, and direct §2.23 branch tests.
5. **Implement atomic behavior**: add expected-version updates/deletes, refresh redemption, revocation, prune/bulk revoke, application-dependent cleanup, exact counts, cancellation, rollback, lost-acknowledgement inspection, and restart recovery.
6. **Prove four providers**: run one black-box suite on real persistent storage, independent clients, failure windows, close/reopen, restart, route/mutation plan evidence, and Mongo replica-set transaction admission.
7. **Prove host and performance**: switch the production-shaped identity host, run sign-in/bearer/refresh/replay/revoke restart flows, submit #646 workloads and consume pass/redesign/blocked verdicts.
8. **Delete EF only at exit**: migrate preserved tests, remove OpenIddict EF source/migrations/packages/settings/host registrations, update docs/catalog/maps and architecture ratchet, then audit exact HEAD for no reintroduced direct/transitive EF path.

## Complexity Tracking

| Concern | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Four concrete OpenIddict store adapters | OpenIddict Core registers four independent contracts and full host compatibility requires all of them. | Token-only implementation leaves a false partially registered capability. |
| Explicit global classification | Store interfaces have no tenant parameter and bearer validation must load entries without ambient tenant context. | Ambient tenant filtering would make a false isolation claim and break valid token verification. |
| Stable rejection of generic delegates | Preserves the closed bounded-query rule while satisfying the external interface honestly. | General `IQueryable` or load-all emulation has no portable finite execution proof. |
| Upstream capability gate | Mutation/multivalue plans must be truthful across four providers. | Provider-specific raw queries would violate the one provider-neutral implementation boundary. |
