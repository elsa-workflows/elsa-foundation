# Design Persistence Contract

**Work unit**: `093-groundwork-design-persistence`

## Boundary

Workflow and activity design core modules own the contracts and invariants. They do not expose Groundwork types, provider sessions, physical names, schema operations, or arbitrary queryables. Provider-suffixed implementation modules translate those contracts into Groundwork documents, bounded queries, and units of work.

Completed repository state:

- one first-party concrete design persistence family: Groundwork;
- one host-level provider selection among SQLite, SQL Server, PostgreSQL, and MongoDB;
- no workflow/activity design EF project, migration, registration, package, or transitive dependency;
- no concrete provider dependency in a design core project.

## Public Store Coverage

### Workflow design

The Groundwork implementation covers the complete public behavior of:

- `IWorkflowDefinitionStore`
- `IWorkflowDefinitionVersionStore`
- `IWorkflowDefinitionDraftStore`
- `IWorkflowDefinitionVersionLayoutStore`
- `IWorkflowDefinitionListProjectionStore`
- `IAddWorkflowDefinitionCommand`
- `IAddWorkflowDefinitionVersionCommand` where present in the current public surface
- `ICreateDraftCommand`
- `ICloneDraftFromVersionCommand`
- `IUpdateDraftCommand`
- `IPromoteDraftToVersionCommand`
- `IDiscardDraftCommand`
- `ISubmitWorkflowDefinitionCommand`
- `ISaveWorkflowDefinitionCommand`
- `IDeleteWorkflowDefinitionPermanentlyCommand`

### Activity design

The Groundwork implementation covers the complete public behavior of:

- `IActivityDefinitionStore`
- `IActivityDefinitionVersionStore`
- activity availability settings persistence
- `IAddActivityDefinitionCommand`
- `IAddActivityDefinitionVersionCommand` where present in the current public surface

If implementation inventory finds another public design persistence contract, it is added to this list and the shared suite before deletion. Absence from this planning document is not permission to drop it.

## Read Semantics

1. Every request is bound to an immutable storage access context before provider work.
2. Point reads address envelope identity and still enforce scope.
3. Non-point reads translate to a named `DocumentQuery` whose shape is a subset of its declared bounded query.
4. AND-of-OR predicate composition, equality, membership, contains, stable ordering, paging, count, any, and first preserve the current Elsa `Query<TEntity>` semantics.
5. Scale-bearing filters, sorting, paging, counts, and latest-version selection execute in the provider. Application code may deserialize and compose already-bounded returned rows, but may not fetch a full kind to discover the matching set.
6. Related aggregates are explicit second bounded reads. No provider navigation or join leaks into core contracts.
7. Unsupported query shape, comparison policy, cardinality, or route is rejected before provider I/O with a stable capability/readiness error.
8. Operational failures, corrupt payloads, and missing schema propagate as domain-scoped failures; only an authoritative no-match returns null/empty.

## Write Semantics

1. A single-document write commits canonical JSON, envelope metadata, and every projected/index value atomically.
2. Multi-document domain transitions use one provider transaction/UoW and become fully visible together.
3. Every staged result is inspected; any non-success or exception rolls back the whole UoW.
4. Expected-version conflicts do not change canonical JSON, projections, related documents, or operation ledgers.
5. Retried requests use deterministic operation identity and canonical fingerprint. Reuse with a different request is a conflict; replay of an acknowledged or acknowledgement-lost commit returns the authoritative prior outcome.
6. Insert uniqueness and version identity include storage scope.
7. Domain events retain their established publication phase and failure policy. A storage retry does not publish a duplicate outcome event.
8. MongoDB composition advertises multi-document design writes only on a transaction-capable deployment.

## Bounded Query Catalog

| Contract behavior | Predicates/order | Result operation | Required physical evidence |
|---|---|---|---|
| Definition point read | envelope id | first | scoped key/unique lookup |
| Definition list/search | id/name/description equality or IN; OR contains across search fields | documents/page/count | projected fields and bounded native plan |
| Version exact/existence | definition id + SemVer sort key | first/any | compound unique/index plan |
| Version latest | definition id; SemVer sort key descending + identity tie-break | first | compound ordered plan |
| Draft by definition/current | definition id; modified/created/id descending | documents/first | scoped ordered plan |
| Version layout | version id | first | unique/index lookup |
| Activity definition lookup | id/type key/category/display fields; supported OR/contains | documents/first/any | projected fields and native plan |
| Activity versions | definition id equality/IN; optional SemVer sort key | documents/first | equality/compound plan |
| List projection inputs | definition id IN with declared maximum cardinality | documents/count | bounded IN plan; deterministic batching above limit |

All physical plans inject scope and document-kind discrimination. Provider plan evidence must identify the intended table/index and must not show a full shared collection scan for a selective query.

## Benchmark Acceptance Catalog

"Gated ordinary-store operation" means each row below; no aggregate score may substitute for a failing row. Reads use a 100K mixed catalog with 10% of records in the active scope, page size 50, a selective predicate returning 1–2%, and fixed hit/miss ratios of 90/10 for identity operations. Writes use pre-seeded related state and concurrency 1 and 16. The 1M run repeats every scale-bearing read and every physical-form comparison.

| Workload | Fixed operation |
|---|---|
| Workflow identity | Get a definition by scoped identity; get an exact version; resolve latest version |
| Workflow catalog | Filter name/description, order by stable identity, return page 10 of 50; count the identical predicate |
| Workflow lifecycle | Create definition plus draft; replace draft state plus layout; promote draft to immutable version; submit definition; permanently delete the complete aggregate |
| Activity identity | Get an activity definition by scoped identity/type key; get an exact version; resolve the applicable latest version |
| Activity catalog | Filter type/category/display fields, order by stable identity, return page 10 of 50; load versions for a fixed definition set |
| Activity lifecycle | Create activity definition plus initial version; add a version; update availability settings |

The seeded value distribution, payload bytes, requested identities, predicate values, and expected result hashes are identical across adapters and forms. Each row records latency, throughput, allocation, provider work/round trips, storage, write amplification, and plan selection; lifecycle rows also record rollback/retry cost.

## Provider Contract

Each provider fixture must prove:

- manifest and route compilation before traffic;
- schema plan/status/validate/safe-apply parity;
- exact CRUD/query/count/order results;
- transaction boundary truthfulness;
- scope isolation for point, query, mutation, and ledger paths;
- optimistic concurrency and uniqueness;
- cancellation, disposal, pool saturation, and restart behavior;
- native query-plan selection;
- identical domain error classification;
- no application-memory fallback.

MongoDB tests run on the supported transaction topology for multi-document conformance and separately prove that a standalone topology rejects unsupported composition before writes.

## Performance and Removal Gate

EF remains a temporary oracle only until all of the following are attached to the work unit:

1. complete result-hash parity for the representative design workloads;
2. all four provider conformance suites green;
3. provider-native plans accepted for every scale-bearing query;
4. atomicity, retry, scope, restart, and schema-evolution tests green;
5. for every row in the Benchmark Acceptance Catalog at 100K, the median of three independent measured processes (one untimed warm-up, at least 100 operations and 30 seconds steady state each) has Groundwork p95 `<= 1.25x` same-provider EF, throughput `>= 80%` EF, and p99 `<= 2x` EF;
6. at 100K and 1M, every selected physical-entity type improves median p95 or throughput by at least 10% over both shared/linked and dedicated-document forms, in the same direction in all three runs, with a 95% bootstrap confidence interval excluding zero;
7. reference design composition uses Groundwork;
8. design source/project/package/test dependency audit reports zero EF;
9. architecture guard rejects a deliberate direct and transitive reintroduction.

Only after all nine pass may the EF mechanism tests and projects be deleted. Their still-valid domain objectives must already exist in the shared suite.
