# Zero-EF Groundwork Decision Map

Program goal: [Zero-EF Persistence](../program-goals/zero-ef-persistence.md).

This compact map coordinates decisions across `elsa-foundation` and the Groundwork repository. Assets and implementation details belong in linked ADRs, reports, specs, PRDs, and issues rather than in this file.

Parent tracking: [Elsa PRD #629](https://github.com/elsa-workflows/elsa-foundation/issues/629) and [Groundwork PRD #25](https://github.com/valence-works/Groundwork/issues/25).

## provider-boundary: Which Persistence Implementations Ship From Elsa Foundation?

Blocked by: none
Status: resolved
Type: Grilling

### Question

How far does the zero-EF target extend, and where does Groundwork belong in the architecture?

### Answer

Core modules retain provider-neutral, Groundwork-free contracts. `elsa-foundation` ships only Groundwork-backed concrete durable implementations, including ASP.NET Core Identity and OpenIddict. EF may serve temporarily as a parity/benchmark oracle, then every direct and transitive EF Core dependency is removed. The product is greenfield, so no legacy-data bridge is required. See [ADR 0042](../adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md).

## physical-storage-forms: Which Physical Forms Must Groundwork Support?

Blocked by: none
Status: resolved
Type: Grilling

### Question

How should Groundwork represent portable, partitioned, and query-optimized documents?

### Answer

Support shared documents plus linked index tables, document-type-specific tables with a common envelope-and-JSON schema, and entity-type-specific tables that combine canonical JSON with native projected columns. JSON remains authoritative. Static types default to dedicated document tables; stable performance-relevant query fields justify entity tables; dynamic types use the shared form. A provider-neutral `PhysicalTableDefinition` describes physical shape. The host controls logical names through `feature default -> host policy -> explicit per-unit override`, followed by provider normalization. The canonical decision is Groundwork [ADR 0003 / PR #26](https://github.com/valence-works/Groundwork/pull/26); this answer records only Elsa's dependency on it.

## query-and-capabilities: What Query Surface Must Elsa Depend On?

Blocked by: none
Status: resolved
Type: Grilling

### Question

Must Groundwork reproduce EF Core's query surface, and may callers select physical query paths?

### Answer

Expose one bounded provider-neutral query contract; the provider planner selects shared indexes, dedicated tables, or entity tables. Do not expose general `IQueryable` or arbitrary LINQ. Production validation rejects unsupported scale-bearing queries and unbounded client evaluation. Capability claims come from executable handlers and conformance tests. Generic map/reduce is deferred until a concrete workload proves it is needed. Groundwork [ADR 0003 / PR #26](https://github.com/valence-works/Groundwork/pull/26) is canonical for the upstream query and capability design.

## schema-operations: How Is Schema Evolution Operated?

Blocked by: none
Status: resolved
Type: Grilling

### Question

How can features evolve physical storage without recreating provider-specific migration sets?

### Answer

Groundwork diffs resolved manifests into provider-neutral additive, backfill, index, and explicitly authorized destructive operations. Providers translate the same plan. Development may auto-apply safe changes; production startup validates by default; deployment jobs apply under a provider migration lock. A .NET CLI provides `plan`, `validate`, `status`, and safe/authorized `apply`, with deterministic machine-readable output. Groundwork [ADR 0003 / PR #26](https://github.com/valence-works/Groundwork/pull/26) is canonical for these upstream mechanics.

## performance-policy: What Evidence Gates EF Removal?

Blocked by: none
Status: resolved
Type: Grilling

### Question

What correctness and performance evidence must Groundwork produce before EF is removed?

### Answer

Correctness, durability, tenant isolation, concurrency, restart behavior, and server-side execution are absolute gates across SQLite, SQL Server, PostgreSQL, and MongoDB. Provisional performance gates are: runtime hot-path p95 no worse than 1.10x EF and throughput at least 90%; ordinary-store p95 no worse than 1.25x and throughput at least 80%; Groundwork p99 no worse than 2x EF. Entity tables must show a repeatable benefit over both other Groundwork forms. Ratify thresholds after the first controlled baseline.

## vocabulary-api: Does Groundwork's Vocabulary Match The Three-Form Model?

Blocked by: none
Status: in-progress
Type: Research

### Question

How should the established `StorageUnit`, `PhysicalizationPolicy`, `Portable`, `Optimized`, `Projection`, `PhysicalizationProjection`, query, naming, and migration APIs evolve around `PhysicalTableDefinition` without unnecessary breakage or overlapping concepts?

### Answer

The first review is complete in [Groundwork #28](https://github.com/valence-works/Groundwork/issues/28) and its linked report. Keep this decision open only for the post-delivery review tracked by [Groundwork #63](https://github.com/valence-works/Groundwork/issues/63), which must reconcile the now-implemented physical-storage, query, migration, and diagnostic APIs before the public surface stabilizes.

## version-aware-codecs: Where Does Payload Version Evolution Belong?

Blocked by: none
Status: resolved
Type: Research

### Question

Should version-aware serialization, payload policies, concrete codecs, and upcasters be provider-neutral Groundwork capabilities, provider-specific mechanics, or Elsa-owned behavior?

### Answer

Groundwork owns only the generic version-aware codec contract and provider integration delivered through [Groundwork PR #88](https://github.com/valence-works/Groundwork/pull/88), with provider-native ordered query explanations from Groundwork PR #89, certified provider-neutral keyset continuation from Groundwork PR #93, bounded residual predicates from Groundwork PR #95, portable substring search keys from Groundwork PR #96, provider-native latest-per-key execution from Groundwork PR #97, sort-only index fields admitted as residual predicates by Groundwork PR #101, bounded linked hydration from Groundwork PR #108, and batched authorized schema apply from Groundwork PR #126, consumed as one package family at `0.0.1-preview.86` from exact upstream source `fd6d1c1b3cb4ebfce03d4cd57e1420060e8c02ac`. Elsa provider packages own marker-gated per-kind version policies, legacy-stamp parsing, JSON options, and concrete upcasters; Elsa core modules remain Groundwork-free. Every payload-shape change increments its durable kind/manifest version, retains deterministic golden fixtures, and invalidates prior composition fingerprints. Provider evidence must be regenerated from the exact Groundwork and Elsa heads; the accepted `preview.60` Identity matrix and Spec 094's `preview.76`/`preview.77`/`preview.80`/`preview.81` artifacts are immutable historical evidence and cannot prove the current `preview.86` boundary. No preview.86 provider evidence is active until the corrected exact-source publication is mechanically imported.

## session-concurrency: What Session Lifecycle Replaces The Singleton Gated Connection?

Blocked by: none
Status: resolved
Type: Prototype

### Question

What stateless facade, pooled per-operation session, explicit unit-of-work session, and SQLite-specific serialization design preserves atomicity without serializing SQL Server/PostgreSQL workloads?

### Answer

[Groundwork #27](https://github.com/valence-works/Groundwork/issues/27) and [Groundwork #34](https://github.com/valence-works/Groundwork/issues/34) delivered stateless provider facades and per-operation sessions. Elsa consumes that model through an immutable admitted session source, access-bound scoped adapters, and explicit unit-of-work ownership. Provider matrices cover independent clients, cancellation, disposal/reopen, process restart, and transaction ownership; performance remains a separate #646/#50 gate.

## identity-openiddict: Which Framework Store Contracts Must Groundwork Implement?

Blocked by: none
Status: resolved
Type: Research

### Question

Which ASP.NET Core Identity and OpenIddict store contracts, indexes, concurrency semantics, token/application/authorization relationships, and host registrations are mandatory for Elsa's current authentication surface?

### Answer

Implement the framework-facing stores in concrete foundation packages over Groundwork while keeping Elsa identity contracts Groundwork-free. ASP.NET Core Identity fits ordinary entity documents and units of work; the replacement implements only complete optional store capabilities and does not expose `IQueryableUserStore`, `IQueryableRoleStore`, or passkeys in the first slice. Spec 095 supplies the implemented and remediated #644 ASP.NET Core Identity candidate and the v1.1 `iam-normalized-lookup-update` contract. Exact-candidate correctness evidence against Groundwork `0.0.1-preview.60` and Identity storage manifest v1.0.4 is accepted for all four supported provider topologies; the EF baseline is non-executed, and #646 owns real same-provider EF execution, equality, and timing. This does not complete OpenIddict, host switching, or #647 deletion. OpenIddict's application, authorization, scope, and token stores fit entity documents, but require compound/typed/multi-value indexes, range queries, bulk prune/revoke, storage-boundary tenancy, and four-provider UoW/OCC conformance. Its generic `IQueryable` delegate overloads are an explicit capability boundary: provide a bounded adapter translator or fail them immediately; never load all documents. The exact interfaces, queries, indexes, relationships, concurrency translations, seeding/normalization behavior, registration changes, and conformance suite are recorded in the [Identity/OpenIddict Groundwork contract inventory](../reports/identity-openiddict-groundwork-contract-inventory.md).

## diagnostic-storage: What Specialized Groundwork Primitive Fits Diagnostics?

Blocked by: none
Status: resolved
Type: Research

### Question

Which append, batching, time-range, ordering, retention, count, aggregation, tenancy, and restart behaviors do Structured Logs and OpenTelemetry require beyond ordinary document CRUD?

### Answer

Use a provider-neutral diagnostic record store for explicit-scope immutable time-ordered records: fingerprint-bound idempotent single-stream batch append and trim, cursor-only or logical-field-plus-cursor ordering, snapshot-bound keyset paging, exact counts, trim-surviving lifetime high-water metadata, and exact per-stream retention. Elsa retains channel buffering, retry/drop/drain policy and composes ordinary Groundwork documents for mutable OpenTelemetry resources and instruments. No current caller requires generic reduce. See the [diagnostics storage workload](../reports/diagnostics-storage-workload.md), [Elsa #632](https://github.com/elsa-workflows/elsa-foundation/issues/632), and upstream delivery slice [Groundwork #30](https://github.com/valence-works/Groundwork/issues/30). The public Groundwork name remains subject to [Groundwork #28](https://github.com/valence-works/Groundwork/issues/28).

## physical-table-definition: What Is The Stable Physical Description Contract?

Blocked by: none
Status: resolved
Type: Prototype

### Question

What exact portable manifest/API represents envelope fields, canonical JSON, serialized field paths, native columns, types, indexes, versioning, naming, and provider extensions for all three storage forms?

### Answer

[Groundwork #31](https://github.com/valence-works/Groundwork/issues/31) delivered `PhysicalTableDefinition`, deterministic storage resolution, explicit per-unit physical tables, host naming policy, provider normalization, and physical-target fingerprints. Elsa now declares dedicated document and entity-table forms through that contract. The v1.1 Identity workload remains a #646 handoff with fixed input fingerprint `5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9` and public result digest `32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`; those hashes do not substitute for current `preview.86` provider evidence or an executed EF comparison.

## unified-query-planner: How Does One Query Contract Select A Physical Plan?

Blocked by: none
Status: resolved
Type: Prototype

### Question

How does Groundwork compile bounded queries consistently across shared indexes, dedicated document tables, entity tables, and MongoDB while proving server-side execution and equivalent results?

### Answer

[Groundwork #45](https://github.com/valence-works/Groundwork/issues/45) delivered bounded planning across physical forms. Later provider work added ordered explanations, keyset continuation, residual predicates, substring lookup keys, latest-per-key execution, and bounded linked hydration. Elsa admits exact `BoundedQueryDeclaration.Identity` routes and rejects missing or unsupported scale-bearing paths before serving work.

## manifest-diff-migrations: How Are Physical Definitions Diffed And Applied?

Blocked by: none
Status: resolved
Type: Prototype

### Question

What deterministic plan, fingerprint, schema-history, backfill, locking, and authorization contracts let every provider execute the same semantic evolution safely?

### Answer

[Groundwork #44](https://github.com/valence-works/Groundwork/issues/44) delivered deterministic physical-schema diffs, fingerprints, durable applied history, and authorization classification. Elsa runtime admission is validate-only: it blocks pending or drifting targets and never applies schema implicitly.

## migration-cli: What Is The Stable DevOps CLI Contract?

Blocked by: none
Status: resolved
Type: Prototype

### Question

What commands, exit codes, JSON schema, dry-run behavior, environment/configuration inputs, and secret-handling rules make Groundwork migrations reliable in CI and deployment pipelines?

### Answer

[Groundwork #49](https://github.com/valence-works/Groundwork/issues/49) delivered `plan`, `validate`, `status`, and authorized `apply`, deterministic JSON, stable exit codes, environment-based connection input, and secret-safe diagnostics. Elsa ships public parameterless deployment schema sources so build/deployment pipelines reconstruct the same resolved target as runtime.

## provider-conformance: How Are Four Providers Proven Equivalent?

Blocked by: diagnostic-storage
Status: in-progress
Type: Prototype

### Question

What executable conformance kit proves capability claims, storage forms, queries, schema evolution, tenant isolation, optimistic concurrency, atomicity, restart, and failure recovery for SQLite, SQL Server, PostgreSQL, and MongoDB?

### Answer

The local Runtime, IAM/Secrets, distributed, recovery, capability-admission, and provider-native route matrices pass across SQLite, SQL Server, PostgreSQL, and a transaction-capable MongoDB replica set. Completion still requires immutable current-head result/native-plan publication into the coverage ledger and linked current evidence from diagnostics #642; closed PR #660 is historical replay provenance only. Historical `preview.60` Identity artifacts remain provenance only.

## performance-harness: How Are EF And Groundwork Compared Fairly?

Blocked by: provider-conformance
Status: in-progress
Type: Prototype

### Question

What reproducible datasets, payload sizes, concurrency levels, warm/cold runs, metrics, result hashes, database telemetry, and reports compare EF physical tables with all three Groundwork forms and ratify the provisional gates?

### Answer

Spec 094 now supplies versioned correctness workloads, fixed input/result digests, exact native-route prerequisites, and closed ALL32 ledger mapping to [Elsa #646](https://github.com/elsa-workflows/elsa-foundation/issues/646). Groundwork physical-form benchmarking remains [Groundwork #50](https://github.com/valence-works/Groundwork/issues/50). No lane may advance on missing, Redesign, or Blocked verdicts, and no timing verdict is inferred from a passing correctness matrix.

## elsa-store-migration: In What Vertical Order Do Elsa Stores Move?

Blocked by: none
Status: in-progress
Type: Grilling

### Question

What dependency-ordered Elsa slices migrate design, runtime, IAM, diagnostics, Identity, and OpenIddict stores while keeping EF only as a temporary oracle and preserving each domain's contract tests?

### Answer

Resolve each store family into independently grabbable issues with only its specific released upstream dependencies. The workflow/activity **design lane is complete and #641 is closed**: spec 093 (US1–US4, PRs #907/#919/#933/#934) made Groundwork the only design provider, deleted the EF design implementation family and its in-memory query fallback, and left the EF-core surface ratchet with zero design-persistence entries; its gate-5 criterion was replaced by the ratified 2026-07-22 absolute-budget amendment. Spec 094 hardens the existing Runtime, IAM, Secrets, and Distributed implementations on Groundwork `preview.86`; Identity #644 is closed; diagnostics #642 has its replayed adapters and reference-host composition on `main` through PRs #1048 and #1072 but still owes preview.86 provider/performance evidence and EF-oracle deletion; #646 owns executed EF comparison and physical-shape verdicts; and #647 owns the final reference-host switch and EF-family deletion. OpenIddict is a separately delivered migration lane (#643), but it remains inside the zero-EF completion gate: #647 and parent #629 cannot close until the OpenIddict EF implementation and dependency surface are deleted too.

## ef-removal: When May The EF Implementation Family Be Deleted?

Blocked by: elsa-store-migration, provider-conformance, performance-harness, migration-cli
Status: open
Type: Prototype

### Question

Which repository-wide audit and architecture test prove that all EF projects, migrations, registrations, packages, tests, host composition, and transitive `Microsoft.EntityFrameworkCore*` dependencies are gone?

### Answer

Pending. The audit must verify the complete build/test graph and reference hosts, not only source-text absence.

## Notes

Domain: provider-neutral persistence, physical storage, and Elsa persistence adoption.

Consult: the repository's Speckit flow for implementation slices; [Critical Constitution Review](../reports/zero-ef-constitution-review.md) for gate changes; source-driven development for third-party Identity/OpenIddict contract research.

Standing preferences: this task is the control room; fresh workers claim one unblocked ticket by setting it `in-progress`; Groundwork and Elsa changes land through separate versioned repository workflows; architecture decisions precede PRDs and implementation issues.
