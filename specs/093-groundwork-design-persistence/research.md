# Research: Groundwork Design Persistence

**Work unit**: `093-groundwork-design-persistence`

## Decision 1: Productionize the existing design lane

**Decision**: Reuse and harden the existing Groundwork workflow/activity read stores, write commands, serializers, manifests, and unified-host composition. Treat the current by-collection/in-memory evaluator as transitional code to remove, not as a second supported mode.

**Rationale**: The current repository already implements every named workflow/activity design store and command over Groundwork, including embedded draft layout, atomic multi-document writes, and unified SQLite/PostgreSQL host composition. Rewriting that behavior would duplicate reviewed orchestration and create avoidable parity risk.

**Alternatives considered**:

- Rebuild design persistence around new repository contracts: rejected because core contracts are already bounded and provider-neutral.
- Keep both the legacy and physical query paths: rejected because production could silently select the unbounded path and invalidate performance evidence.

## Decision 2: Translate once to declared bounded queries

**Decision**: Add one implementation-layer translator from Elsa's closed `Query<TEntity>` model to Groundwork's `DocumentQuery` model. Each public query shape binds to a versioned query identity declared with its physical table. Runtime execution uses `IBoundedDocumentStore`; direct identity lookup may retain the document-store point-read fast path. Unsupported shapes fail during registration/readiness or before provider I/O.

**Rationale**: Groundwork now provides physical query planning, compiled routes, handler certification, native count/any/first operations, compound predicates, disjunction, paging, and provider plan evidence. The Elsa load-all fallback predates that surface and is the exact exit-gate violation identified by issue #641.

**Alternatives considered**:

- Translate expression trees or expose `IQueryable`: rejected by the ratified bounded-query decision.
- Maintain a provider-specific translator in each adapter: rejected because it would duplicate semantics and allow provider drift.
- Fetch a by-collection candidate set and filter locally: rejected for every scale-bearing production query.

## Decision 3: Use physical entity tables for query-bearing design types

**Decision**: Use physical entity tables—with envelope fields, canonical JSON, and native projected columns—for workflow definitions, workflow versions, workflow drafts, version layouts, activity definitions, and activity versions. Use a dedicated document table for activity availability settings unless benchmark evidence proves an entity form useful. Canonical JSON remains authoritative in all forms.

**Rationale**: Each selected entity type has stable, product-owned query fields and participates in equality, membership, contains, compound existence, ordering, or bounded projection queries. Drafts need definition identity plus deterministic current-draft ordering; layouts need version identity. The availability settings store is point-oriented and does not yet justify native projections.

**Alternatives considered**:

- Shared documents plus linked indexes for every type: portable but retains unnecessary joins and write amplification for stable static types.
- Dedicated document tables for every type: acceptable fallback, but leaves established query columns inside JSON/sidecars and must be benchmarked against the selected entity form.
- Columns-only relational entities: rejected because canonical JSON and document-provider portability are required.

## Decision 4: Enforce scope through sessions, not query flags

**Decision**: Normal design operations acquire a store/session bound to the current storage scope. Tenant-agnostic operations require an explicit privileged access context. The translator does not turn `TenantAgnostic` into an ambient filter bypass; composition must deliberately acquire the privileged session, and tests prove wrong-scope point reads and writes do not leak existence.

**Rationale**: Groundwork's storage-boundary tenancy must cover identity reads and mutations, not only query predicates. Elsa's existing `TenantAgnostic` flag expresses authorization intent, but the provider implementation must bind that intent to an auditable privileged session.

**Alternatives considered**:

- Persist all design data under `TenancyPolicy.Global`: rejected because it contradicts the public filter surface and parent zero-EF gates.
- Inject tenant predicates only into queries: rejected because point reads, deletes, UoW operations, and ledgers could bypass them.

## Decision 5: Preserve atomic domain transitions with provider-native UoW

**Decision**: Continue to use Groundwork document units of work for multi-document design commands, check `TransactionBoundary` before any operation, roll back on every non-success outcome or exception, and keep operation identity/fingerprint stable across retry. Single-document draft mutations remain atomic document replacements with OCC. MongoDB readiness requires a transaction-capable topology.

**Rationale**: Add-definition, add-version, promote, submit, and permanent delete span related documents. Partial visibility is incompatible with the existing domain behavior. Groundwork now exposes the deployment-dependent transaction boundary explicitly.

**Alternatives considered**:

- Compensating writes on providers without atomicity: rejected for this design lane because readers could observe impossible intermediate states.
- Sequential autonomous document writes with retry: rejected because convergence after failure does not prevent transient partial visibility.

## Decision 6: Ship and test four coherent provider compositions

**Decision**: Keep provider-neutral manifests and adapters in common projects, retain SQLite/PostgreSQL provider leaves, and add equivalent SQL Server/MongoDB host leaves plus one shared conformance suite. Provider packages and `Groundwork.Tool` must use the same released version. Startup validates; deployment pipelines plan/status/apply.

**Rationale**: The product promise is host choice among four mandatory providers without feature-level provider changes. Elsa currently proves only SQLite and PostgreSQL unified composition.

**Alternatives considered**:

- Test Groundwork providers only upstream: rejected because Elsa serialization, query translation, scope mapping, and command orchestration are consumer-specific.
- Put provider SDK references in each feature adapter: rejected by the provider boundary and duplication cost.

## Decision 7: Migrate test objectives before deleting EF tests

**Decision**: Extract black-box behavior fixtures from existing EF-centric tests, run them against the temporary EF oracle and every Groundwork provider, then remove only the EF setup/mechanism assertions after parity and performance evidence is recorded. Preserve domain-objective tests such as immutability, event sequencing, SemVer resolution, layout behavior, and failure recovery.

**Rationale**: Framework §2.21.1 requires test objective continuity. Several current tests assert EF metadata as a proxy for domain immutability; those objectives must become provider-neutral observable stale-write/conflict tests before their EF mechanics disappear.

**Alternatives considered**:

- Delete every test containing an EF namespace with the EF projects: rejected because many assert durable domain behavior rather than EF itself.
- Keep a permanent EF oracle project: rejected by the zero-EF completion condition.

## Decision 8: Gate physical-form selection with reproducible evidence

**Decision**: Use identical fixed datasets, payloads, query shapes, concurrency, and result hashes to compare EF normalized tables with Groundwork shared/linked, dedicated-document, and physical-entity forms. Record raw artifacts and provider-native plans. Keep the selected physical-entity form only when it passes correctness and shows repeatable benefit while meeting the accepted latency/throughput gates.

**Rationale**: Physical entity tables add schema and backfill complexity and must earn it. The parent PRDs already ratified thresholds and representative 1K/100K/1M scale points.

**Alternatives considered**:

- Choose entity tables solely by architectural preference: rejected because the explicit performance policy requires measured justification.
- Benchmark only SQLite: rejected because provider-neutral correctness and plan shape can diverge even when SQLite is fast.

## Resolved Dependencies

- Groundwork physical storage forms, naming, compiled query routes, schema diffs, migration CLI, stateless sessions, storage-boundary tenancy, SQLite/SQL Server/PostgreSQL/MongoDB providers, and relational bounded mutations are present on Groundwork `main`.
- MongoDB bounded mutations and portable Unicode/long searchable values remain active upstream work. The design lane must consume a version that provides the exact operations and comparison semantics it declares; it must not emulate missing behavior locally.
- Document identity case policy is an Identity/OpenIddict dependency and is not required to change existing ordinal design document IDs in this slice.
- The targeted constitution amendment remains pending until complete zero-EF compliance; this plan does not require an interim exception.
