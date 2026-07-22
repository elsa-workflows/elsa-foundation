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
- `IActivityAvailabilitySettingsStore`
- `IAddActivityDefinitionCommand`
- `IAddActivityDefinitionVersionCommand` where present in the current public surface

### Reconciled current public surface (T002 baseline)

The list above was reconciled against the registrations on `origin/main` at
`d1548991f`. The smaller original list did not include the reusable-activity and
management-projection contracts added after the first Groundwork design switch.
They are part of this work unit and cannot be removed with the EF lane or omitted
from the shared suite.

| Domain | Public contract group | Current first-party adapter | Baseline disposition |
|---|---|---|---|
| Workflow | `IWorkflowDefinitionStore`, `IWorkflowDefinitionVersionStore`, `IWorkflowDefinitionDraftStore`, `IWorkflowDefinitionVersionLayoutStore`, `IWorkflowDefinitionListProjectionStore` | Corresponding `GroundworkWorkflowDefinition*Store` | Include in US1/US2 shared scenarios. |
| Workflow | `IAddWorkflowDefinitionCommand`, `IAddCommand<WorkflowDefinitionVersion>`, `ISaveWorkflowDefinitionCommand`, `IDeleteWorkflowDefinitionPermanentlyCommand` | Corresponding `Groundwork*Command` | Include atomicity, retry, OCC, and restart scenarios. |
| Workflow | `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IUpdateDraftCommand`, `IPromoteDraftToVersionCommand`, `IDiscardDraftCommand`, `ISubmitWorkflowDefinitionCommand` | Corresponding `Groundwork*Command` | Include lifecycle, layout, validation-gate, and event scenarios. |
| Workflow | `IWorkflowDefinitionLookup`, `IWorkflowDefinitionPermanentDeletionGuard`, `IDraftStateDiffEngine` | `WorkflowDefinitionLookup` plus provider-neutral collaborators | Retain their observable behavior in lookup and lifecycle scenarios. |
| Activity | `IActivityDefinitionStore`, `IActivityDefinitionVersionStore`, `IActivityAvailabilitySettingsStore` | `GroundworkActivityDefinitionStore`, `GroundworkActivityDefinitionVersionStore`, `GroundworkActivityAvailabilitySettingsStore` | Include baseline CRUD, exact/latest-version, scope, and restart scenarios. |
| Activity | `IAddActivityDefinitionCommand`, `IAddCommand<ActivityDefinitionVersion>` | `GroundworkAddActivityDefinitionCommand`, `GroundworkAddActivityDefinitionVersionCommand` | Include uniqueness, immutable-version, and atomic multi-document scenarios. |
| Activity | `IActivityDefinitionManagementProjectionStore` | `GroundworkActivityDefinitionManagementProjectionStore` | Include bounded page, continuation, visibility, and retention scenarios. |
| Activity | `IActivityDefinitionAuthoringStore`, `IActivityDefinitionDraftStore`, `IActivityDefinitionVersionPublicationStore`, `IRecommendedActivityDefinitionPickerStore`, `IActivityDefinitionLayoutStore`, `IActivityDraftValidationStore`, `IActivityForkStore` | `GroundworkReusableActivityStores` | Include in reusable-activity conformance once its public scenarios are extracted. |
| Activity | `IActivityDirectDependencyStore`, `IActivityDependencyProjectionStore`, `IActivityDependencyProjectionRebuilder` | `GroundworkReusableActivityStores`, `GroundworkActivityDependencyProjection` | Include bounded dependency reads and rebuild/restart scenarios. |
| Activity | `IActivityUpgradePlanStore`, `IActivityUpgradeApplyReceiptStore` | `GroundworkActivityUpgradePlanStore` | Include idempotency, receipt, and restart scenarios. |
| Activity | `ICreateActivityDefinitionCommand`, `ISaveActivityForkCandidateCommand`, `IPruneActivityForkCandidatesCommand`, `IApplyActivityForkCandidateCommand`, `IUpdateActivityDefinitionPresentationCommand`, `ICreateActivityDraftCommand`, `IUpdateActivityDraftPresentationCommand`, `ICreateActivityDraftConflictCopyCommand`, `IReplaceActivityDraftCommand`, `IApplyActivityContractProposalCommand`, `IDiscardActivityDraftCommand`, `IStoreActivityDraftValidationCommand`, `IChangeActivityVersionLifecycleCommand`, `ISetActivityDefinitionRecommendationCommand` | `GroundworkReusableActivityStores` | Include the relevant public authoring, conflict, validation, recommendation, and lifecycle scenarios; no contract is implicitly excluded. |

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
5. Every retryable mutation request carries an explicit caller-stable operation/idempotency key. The key is separate from the canonical fingerprint derived from the mutation material. Exact key-plus-fingerprint replay of an acknowledged or acknowledgement-lost commit returns the authoritative prior outcome; reuse of the key with a different fingerprint is a conflict with no mutation.
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
5. for every row in the Benchmark Acceptance Catalog at 100K, the median of three independent measured processes (one untimed warm-up, at least 100 operations and 30 seconds steady state each) meets the absolute operational budget for its class:

   | Class | Rows | p95 | p99 | Throughput |
   |---|---|---|---|---|
   | point-read | identity get / version-exact / version-latest / exists (6) | ≤ 0.8 ms | ≤ 2.5 ms | ≥ 2,000 ops/s |
   | batch/projection reads | `act.catalog.versions-batch`, `wf.catalog.projection` | ≤ 5 ms | ≤ 20 ms | ≥ 200 ops/s |
   | catalog page/count | `wf`+`act` filter-page, `wf.catalog.count` | ≤ 400 ms | ≤ 800 ms | ≥ 4 ops/s |
   | writes @c1 | create/materialize/create/add-version (4) | ≤ 3 ms | ≤ 25 ms | ≥ 400 ops/s |
   | writes @c16 | the same 4 rows at concurrency 16 | ≤ 100 ms | ≤ 500 ms | ≥ the same row's @c1 throughput (write scaling must not invert) |

   **Decision record (ratified 2026-07-22 by program-owner interactive decision, after reviewing the fair-conditions data; validated by the T079 independent review).** This item previously required Groundwork p95 `<= 1.25x` same-provider EF, throughput `>= 80%` EF, and p99 `<= 2x` EF. That per-row EF ratio is **replaced** by the absolute budgets above because the ratio compared semantically unequal work: the Groundwork write path executes the ratified operation-ledger marker, replay preflight, scope-bound sessions, and atomic multi-document staging per operation, while the temporary EF oracle performs bare `SaveChanges`. The EF oracle's own conformance profile (`DesignPersistenceContractProfiles.LegacyEfOracle`, research.md) declares the ledger, replay, and storage-scope scenarios **N/A** ("No durable operation ledger…", "No caller-stable operation key or durable replay outcome", "No storage-scope-bound write boundary"), so an EF ratio charges Groundwork for correctness work the oracle does not perform. The budgets instead bound the product-relevant authoring envelope (interactive-save perception thresholds, point-lookup latencies, catalog page responsiveness) and protect against regression; each measured median is recorded alongside its budget so headroom is visible. Same-provider EF measurements remain **RECORDED as evidence, not a gate**. Verified: the fair same-conditions re-measurement of 2026-07-22 passes 19/19 (`docs/reports/groundwork-design-persistence-performance.md`, `bench-out/comparison.100k.json`, `bench-out/gates.json`).
6. at 100K and 1M, every selected physical-entity type improves median p95 or throughput by at least 10% over both shared/linked and dedicated-document forms, in the same direction in all three runs, with a 95% bootstrap confidence interval excluding zero;
7. reference design composition uses Groundwork;
8. design source/project/package/test dependency audit reports zero EF;
9. architecture guard rejects a deliberate direct and transitive reintroduction.

Only after all nine pass may the EF mechanism tests and projects be deleted. Their still-valid domain objectives must already exist in the shared suite.
