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

**Decision**: Extract black-box behavior fixtures from existing EF-centric tests, run them against the temporary EF oracle and every Groundwork provider, then remove only the EF setup/mechanism assertions after parity and performance evidence is recorded. Preserve domain-objective tests such as immutability, event sequencing, SemVer resolution, layout behavior, and failure recovery. Before deleting any existing test, record the exact test, its classified objective, its replacement evidence or reason the objective is invalid, and explicit architect approval in the test-removal ledger in this document; general approval of the migration is not approval to delete an individual test.

**Rationale**: Framework §2.21.1 requires test objective continuity. Several current tests assert EF metadata as a proxy for domain immutability; those objectives must become provider-neutral observable stale-write/conflict tests before their EF mechanics disappear.

**Alternatives considered**:

- Delete every test containing an EF namespace with the EF projects: rejected because many assert durable domain behavior rather than EF itself.
- Keep a permanent EF oracle project: rejected by the zero-EF completion condition.

## Decision 8: Gate physical-form selection with reproducible evidence

**Decision**: Use identical fixed datasets, payloads, query shapes, concurrency, and result hashes to compare EF normalized tables with Groundwork shared/linked, dedicated-document, and physical-entity forms. The required scales are 1K for correctness/smoke, 100K for every acceptance workload on every mandatory provider, and 1M for every scale-bearing query/form comparison on every mandatory provider. A scale may be omitted only by an explicit architect-approved workload exclusion recorded before timing; machine capacity alone is not an implicit waiver.

Each measured case runs as three independent processes after one untimed warm-up per process, with at least 100 completed operations and 30 seconds of steady-state measurement per run. Acceptance uses the median of the three per-run p95, p99, and throughput results and applies the EF ratio gates to each gated operation rather than hiding a regression in an aggregate. Raw per-operation samples, fixed seed, payload hash, provider/server configuration, machine metadata, native plan, round trips/database work, allocation, and result hash are retained. A physical-entity form earns selection only when it improves median p95 or median throughput by at least 10% over both other Groundwork forms at 100K and 1M, the direction holds in all three runs, and a 95% bootstrap confidence interval for relative improvement excludes zero. The same-provider EF ratio is required wherever an EF oracle exists; MongoDB records the absolute baseline and must still pass form-selection, bounded-plan, and correctness gates without inventing an EF comparison.

**Rationale**: Physical entity tables add schema and backfill complexity and must earn it. The parent PRDs already ratified thresholds and representative 1K/100K/1M scale points.

**Alternatives considered**:

- Choose entity tables solely by architectural preference: rejected because the explicit performance policy requires measured justification.
- Benchmark only SQLite: rejected because provider-neutral correctness and plan shape can diverge even when SQLite is fast.

## Test-removal approval ledger

The exact baseline inventory is maintained in
[test-removal-ledger.md](test-removal-ledger.md). No entry is approved by default.
T072 and T073 may delete an existing test only when its exact test identity has an
explicit architect decision and replacement evidence in that ledger. A `Pending`
decision is deliberately not deletion permission.

## Framework §2.23 coverage ledger

T020 creates and maintains this ledger at class granularity. Every feature class receives a direct registration/composition test under §2.23.1; every logic-bearing implementation receives its own stubbed-dependency public-surface branch suite under §2.23.2. T035, T048, T061, and T062 must add and close rows introduced by their implementation phases before those checkpoints can pass.

| Class | Kind | Owning implementation task | Registration test | Direct branch test | Status |
|---|---|---|---|---|---|
| `WorkflowsDesignStorageManifest` | Manifest compiler | T010 | `Reference_deployment_schema_unions_exact_workflow_and_activity_physical_definitions` | `WorkflowsDesignStorageManifestTests`, including algorithm-version fingerprint | Covered |
| `WorkflowsDesignGroundworkStorageManifestSource` | Manifest source | T010 | `Family_registration_adds_its_source_scoped_and_idempotently` | `Family_source_declares_exact_manifest_and_public_store_contracts`; cancellation theory | Covered |
| `ActivitiesDesignStorageManifest` | Manifest compiler | T011 | `Reference_deployment_schema_unions_exact_workflow_and_activity_physical_definitions` | `ActivitiesDesignStorageManifestTests` | Covered; no design comparison-key algorithm is declared |
| `ActivitiesDesignGroundworkStorageManifestSource` | Manifest source | T011 | `Family_registration_adds_its_source_scoped_and_idempotently` | `Family_source_declares_exact_manifest_and_public_store_contracts`; cancellation theory | Covered |
| `GroundworkStorageCompositionContext` | Composition state | T012 | Not a feature | `GroundworkStorageCompositionTests` duplicate, freeze, ordering, immutability branches | Covered |
| `GroundworkStorageCompositionHandler` | Composition handler | T012 | Factory inline/fallback paths | `Handler_*` source-order, failure, and cancellation tests | Covered |
| `GroundworkStorageCompositionFactory` | Composition factory | T012 | `Registered_factory_builds_the_selected_composition_from_scoped_sources` | inline publisher plus `Factory_rejects_a_missing_executable_handler_before_provider_work` | Covered |
| `GroundworkStorageCompositionValidator` | Admission validator | T012 | Factory composition tests | owner, route, capability, topology, collision, compiler-failure, target-mismatch, and fingerprint tests | Covered |
| `GroundworkPhysicalNameResolver` | Naming resolver | T012 | Validator composition path | transform order, inexact owners, deterministic collision, and exact-shared-object exemption tests | Covered |
| `GroundworkRoutePhysicalSchemaTargetCompiler` | Target compiler | T012 | Validator default compiler path | null guards and exact manifest/provider/route preservation test | Covered |
| `GroundworkUnifiedManifest` | Compatibility facade | T012 | Not a feature | `Compatibility_facade_returns_snapshot_manifest_without_a_second_fingerprint` | Covered |
| `GroundworkDeploymentSchemaManifestSource` | Deployment schema source | T061 | Reference deployment schema registration | T061: empty/duplicate/invalid/non-constructible sources, naming mismatch, cancellation | Assigned to T061 |
| `GroundworkStorageCompositionRegistration` | DI registration | T061 | idempotence and deployment-authority tests | T061: resolve the complete registered composition surface | Assigned to T061 |
| `GroundworkPhysicalSchemaManifestSource` | Readiness/schema bridge | T061 | Schema CLI and host tests | T061: ready/pending/drift, executor failure, cancellation, inspect-only/no-auto-apply | Assigned to T061 |
| `GroundworkPersistenceAccessMapper` | Scope mapper | T016 | Not a feature | `GroundworkStoreSessionFactoryTests.Mapper_*` | Covered |
| `GroundworkStoreSession` | Session lifecycle | T016 | Factory/provider registration tests | disposal/audit branches are covered indirectly; direct construction awaits the recorded §2.23.3 public-surface decision | Open: T020 ratification |
| `GroundworkScopedDocumentStore` | Scoped store adapter | T016 | Runtime/provider registration tests | `GroundworkScopedDocumentStoreTests` covers every document/bounded operation, ordinary scope injection, failure, cancellation, Begin failure, and UoW retention | Covered except nested UoW direct-construction decision |
| `SessionDocumentUnitOfWork` | UoW/session lifetime adapter | T016 | Via `GroundworkScopedDocumentStore.BeginAsync` | disposal behavior is covered indirectly; direct construction awaits the recorded §2.23.3 public-surface decision | Open: T020 ratification |
| `GroundworkStoreSessionSource` | Resource publisher | T016 | Runtime/provider registration tests | `GroundworkStoreSessionSourceTests` publication/disposal race branches | Covered |
| `GroundworkStoreSessionFactory` | Session factory | T017 | Runtime/provider registration tests | ordinary/privileged mapping, authority rejection, cleanup, audit, cancellation, and terminal outcomes | Covered |
| `GroundworkQueryTranslator<TEntity>` | Query translator | T015 | Not a feature | operators, clause structure, JSON names/scalars, ordering, terminals, invalid selectors, non-scalars, and serialization failures | Covered |
| `GroundworkQueryTranslationException` | Public failure boundary | T015/T018 | Not a feature | Translator negative-path tests | Covered; hierarchy decision remains T018 |
| readiness, corrupt-payload, and provider-failure exception classes | Public failure boundaries | T018 | Not features | T018 direct constructor/context and throw-site branches | Assigned to T018 after naming ratification |
| `InMemoryDocumentStore` | Test query substrate | T019 | Not production/feature | `InMemoryDocumentStoreBoundedQueryTests` | Covered; outside production denominator |
| `GroundworkReadStore<TEntity>` | Transitional candidate reader | T048 | Current design-store registration tests | Current legacy behavior tests; replacement must prove no load-all path | Assigned to T048 |
| `GroundworkDesignAtomicWrite` and workflow/activity command implementations | Command/UoW implementations | T035 | T035 store/command registration | T035: success, provider failure, rollback, retry/lost acknowledgement, cancellation, replay fingerprint, scope, exactly-once events | Assigned to T035 |
| `GroundworkSchemaReadinessTask` and schema-tool adapters | Readiness implementations | T061 | T061 feature/host composition | T061: ready/pending/drift, no apply/fallback, failure, cancellation | Assigned to T061 |
| SQLite/PostgreSQL/SQL Server/MongoDB provider registrations, initializers, target compilers, and shell features | Provider materialization | T062 | T062 one §2.23.1 resolution test per concrete feature/provider | T062 direct provider branches and exact target evidence | Assigned to T062 |

## Resolved Dependencies

- Groundwork physical storage forms, naming, compiled query routes, schema diffs, migration CLI, stateless sessions, storage-boundary tenancy, SQLite/SQL Server/PostgreSQL/MongoDB providers, and relational bounded mutations are present on Groundwork `main`.
- MongoDB bounded mutations and portable Unicode/long searchable values remain active upstream work. The design lane must consume a version that provides the exact operations and comparison semantics it declares; it must not emulate missing behavior locally.
- Document identity case policy is an Identity/OpenIddict dependency and is not required to change existing ordinal design document IDs in this slice.
- The targeted constitution amendment remains pending until complete zero-EF compliance; this plan does not require an interim exception.

## Phase 2 implementation reconciliation

The unified-composition architecture advanced after the original T012/T013 file targets were drafted. The
stable `elsa-documents` identity, `elsa.documents` owner, and `1.0.0` version now live in
`GroundworkStorageCompositionDescriptor`; `GroundworkStorageCompositionValidator` performs the selected
manifest union. `GroundworkDeploymentSchemaManifestSource` is the provider-neutral deployment source and
host naming-policy bridge. `GroundworkUnifiedManifest` deliberately remains only a compatibility facade, so
the design manifests are not hard-coded into it. Direct composition tests cover the workflow/activity union
and a host-supplied naming policy across both design families.

The original T016/T017 design-specific session class targets would duplicate the established access-bound
session contract. `GroundworkStoreSession` already presents one immutable `DocumentStoreAccess`,
`IDocumentStore`, and `IBoundedDocumentStore` bundle; `GroundworkScopedDocumentStore` retains that same
session for a unit of work and releases it afterward. `GroundworkStoreSessionFactory` maps the current Elsa
access context for every acquisition and rejects tenant-agnostic work before provider acquisition unless the
context is explicitly privileged across scopes. Its privileged execution helper also records acquisition
and terminal outcome. The generic foundation is therefore reused by design adapters rather than wrapped in
a second design-only lifetime abstraction.
