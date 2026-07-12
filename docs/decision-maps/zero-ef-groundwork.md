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

In progress as [Groundwork #28](https://github.com/valence-works/Groundwork/issues/28). Produce a focused vocabulary/API report in the Groundwork repository and link it here.

## session-concurrency: What Session Lifecycle Replaces The Singleton Gated Connection?

Blocked by: none
Status: in-progress
Type: Prototype

### Question

What stateless facade, pooled per-operation session, explicit unit-of-work session, and SQLite-specific serialization design preserves atomicity without serializing SQL Server/PostgreSQL workloads?

### Answer

In progress as [Groundwork #27](https://github.com/valence-works/Groundwork/issues/27). Prototype the lifecycle and validate concurrency, cancellation, disposal, pool saturation, and transaction ownership before benchmarking.

## identity-openiddict: Which Framework Store Contracts Must Groundwork Implement?

Blocked by: none
Status: in-progress
Type: Research

### Question

Which ASP.NET Core Identity and OpenIddict store contracts, indexes, concurrency semantics, token/application/authorization relationships, and host registrations are mandatory for Elsa's current authentication surface?

### Answer

In progress as [Elsa #631](https://github.com/elsa-workflows/elsa-foundation/issues/631). Produce an Elsa contract inventory and conformance matrix; do not couple core identity contracts to Groundwork.

## diagnostic-storage: What Specialized Groundwork Primitive Fits Diagnostics?

Blocked by: none
Status: open
Type: Research

### Question

Which append, batching, time-range, ordering, retention, count, aggregation, tenancy, and restart behaviors do Structured Logs and OpenTelemetry require beyond ordinary document CRUD?

### Answer

Pending. Produce a workload contract shared with [Diagnostics Observability Readiness](../program-goals/diagnostics-observability-readiness.md), then design the smallest specialized Groundwork primitive.

## physical-table-definition: What Is The Stable Physical Description Contract?

Blocked by: vocabulary-api
Status: open
Type: Prototype

### Question

What exact portable manifest/API represents envelope fields, canonical JSON, serialized field paths, native columns, types, indexes, versioning, naming, and provider extensions for all three storage forms?

### Answer

Pending.

## unified-query-planner: How Does One Query Contract Select A Physical Plan?

Blocked by: physical-table-definition
Status: open
Type: Prototype

### Question

How does Groundwork compile bounded queries consistently across shared indexes, dedicated document tables, entity tables, and MongoDB while proving server-side execution and equivalent results?

### Answer

Pending.

## manifest-diff-migrations: How Are Physical Definitions Diffed And Applied?

Blocked by: physical-table-definition
Status: open
Type: Prototype

### Question

What deterministic plan, fingerprint, schema-history, backfill, locking, and authorization contracts let every provider execute the same semantic evolution safely?

### Answer

Pending.

## migration-cli: What Is The Stable DevOps CLI Contract?

Blocked by: manifest-diff-migrations
Status: open
Type: Prototype

### Question

What commands, exit codes, JSON schema, dry-run behavior, environment/configuration inputs, and secret-handling rules make Groundwork migrations reliable in CI and deployment pipelines?

### Answer

Pending.

## provider-conformance: How Are Four Providers Proven Equivalent?

Blocked by: session-concurrency, unified-query-planner, manifest-diff-migrations, diagnostic-storage
Status: open
Type: Prototype

### Question

What executable conformance kit proves capability claims, storage forms, queries, schema evolution, tenant isolation, optimistic concurrency, atomicity, restart, and failure recovery for SQLite, SQL Server, PostgreSQL, and MongoDB?

### Answer

Pending.

## performance-harness: How Are EF And Groundwork Compared Fairly?

Blocked by: session-concurrency, unified-query-planner, diagnostic-storage
Status: open
Type: Prototype

### Question

What reproducible datasets, payload sizes, concurrency levels, warm/cold runs, metrics, result hashes, database telemetry, and reports compare EF physical tables with all three Groundwork forms and ratify the provisional gates?

### Answer

Pending.

## elsa-store-migration: In What Vertical Order Do Elsa Stores Move?

Blocked by: none
Status: open
Type: Grilling

### Question

What dependency-ordered Elsa slices migrate design, runtime, IAM, diagnostics, Identity, and OpenIddict stores while keeping EF only as a temporary oracle and preserving each domain's contract tests?

### Answer

Pending. Resolve each store family into independently grabbable issues with only its specific released upstream dependencies. Design, runtime, IAM, diagnostics, Identity, and OpenIddict may migrate independently while EF remains a temporary oracle.

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
