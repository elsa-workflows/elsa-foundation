# Research: Groundwork Store Hardening

**Work unit**: `094-harden-groundwork-stores`

## Decision 1: Make the coverage denominator executable

**Decision**: Commit a versioned machine-readable coverage ledger and schema alongside its human-readable contract. Freeze its initial denominator to this feature branch's merge-base. Every implementation PR owns explicit row identities, and a CI validator reconciles the ledger with durable contracts, registrations, provider evidence, test objectives, and the retiring EF surface.

**Rationale**: A changing reflection inventory or prose checklist cannot prove that every durable store was handled. Stable row identities let agents, reviewers, and CI distinguish missing, externally owned, implemented, evidence-complete, and ready outcomes.

**Alternatives considered**:

- Markdown-only checklist: rejected because omissions and invalid state transitions would remain review conventions.
- Generate the denominator from current registrations on every run: rejected because deleting a registration could silently delete the obligation.

## Decision 2: Compose selected manifests before provider materialization

**Decision**: Each Groundwork implementation feature exposes one manifest source. A small `Elsa.Persistence.Groundwork.Composition` project owns only the acyclic implementation-level source/event/context/snapshot contract. Domain Groundwork projects depend on Composition; `Elsa.Persistence.Groundwork.Unified` also depends on Composition and owns the single aggregating handler, validation, and materialization, but no feature project depends on Unified. This prevents the current base→Unified→base cycle that would otherwise result. The named startup contribution event produces an immutable, deterministically ordered composition snapshot before stores resolve. `GroundworkUnifiedManifest` becomes a compatibility façade over the snapshot and loses its hard-coded domain and project references.

The composition root registers only sources belonging to selected features. Runtime, Workflows Design, Activities Design, Publishing, IAM, Secrets, and Distributed Runtime each own a source; #660 contributes diagnostics through its linked workstream. Groundwork's `StorageManifestComposition.Union` remains the collision authority. The runtime and deployment CLI consume the same concrete `IPhysicalSchemaManifestSource` and target fingerprint.

**Rationale**: The current static union includes runtime/design/publishing but omits IAM, secrets, and distributed runtime. Independently materialized feature stores would lose atomic cross-unit operations and coherent schema history. The named contribution event plus one source aggregator follows framework §2.6.1 without introducing Groundwork into domain core modules.

**Alternatives considered**:

- Keep editing one central static union: rejected because optional feature selection remains implicit and omissions recur.
- Let every feature create its own document store: rejected because a host could expose inconsistent provider, transaction, scope, and schema state.
- Resolve arbitrary `IEnumerable<StorageManifest>` directly at the call site: rejected because fan-in contribution must use the sanctioned named-event/aggregator topology.

## Decision 3: Replace the global holder with session-factory infrastructure

**Decision**: Retire the application-wide `GroundworkDocumentStoreHolder` as the normal consumer seam. Provider initialization owns a materialized provider factory; each operation or unit of work acquires an immutable session with a tenant/global storage scope and a separate ordinary/privileged access policy. Static immutable provider resources may remain singleton. Logic-bearing store adapters, aggregators, handlers, access-context selectors, and session/unit-of-work consumers are scoped; any different lifetime requires a documented constitution-compliant exception and registration/lifetime tests.

**Rationale**: A singleton `IDocumentStore` initialized with `DocumentStoreAccess.Global` cannot enforce tenant scope, and instance-level locks on its adapters do not coordinate processes. Groundwork's released session APIs already bind access and unit-of-work policy at the storage boundary.

**Alternatives considered**:

- Keep a global store and prefix document IDs: rejected because point reads, queries, mutations, and units of work can still cross scope.
- Put a mutable ambient scope on the singleton: rejected because cancellation, async flow, pooling, and concurrent requests can leak state.

## Decision 4: Add a provider-neutral persistence-scope contract in `Elsa.Persistence.Core`

**Decision**: Define the small provider-neutral scope/access context in the existing `Elsa.Persistence.Core` package. Groundwork adapters map tenant scope to `StorageScope` and `DocumentStoreAccess.Scoped`; explicitly global storage maps to `DocumentStoreAccess.Global`; privileged operations are classified independently, require a named Elsa capability/purpose, and map to the appropriate privileged access kind. Manifests become tenant-scoped by default.

Domain contracts that already carry a tenant use that explicit value and verify it agrees with the active scope. Contracts without a tenant use the scoped accessor. A single-tenant host registers a nonblank default scope (the default literal is `default`); a null/absent tenant never means global. Global and cross-scope operations are individually classified in the ledger. No Groundwork type appears in `Elsa.Persistence.Core` or domain core projects.

**Rationale**: A coherent provider-neutral persistence concern already exists and is narrower than `Elsa.Primitives`. Scope must cover loads, saves, deletes, queries, mutations, and UoW acquisition rather than only tenant-filtered queries.

**Alternatives considered**:

- Add scope types to `Elsa.Primitives`: rejected by its domainless, zero-dependency charter.
- Repeat an ambient tenant accessor in each domain: rejected because four store families need identical persistence-boundary semantics.
- Add `tenantId` to every store method: rejected because it changes domain contracts that already have a request scope and still does not model privileged access.

## Decision 5: Use one real-provider black-box fixture

**Decision**: Create a shared conformance fixture with provider drivers for file-backed SQLite, SQL Server and PostgreSQL containers, and a transaction-capable MongoDB replica set. The fixture owns production-shaped startup, schema application, independent clients, deterministic reset, disposal/reopen, true child-process reconnect, provider-process restart where applicable, failure injection, cancellation, native-plan capture, and topology validation. Domain suites supply public-contract scenarios and provider-independent outcome assertions.

Dispose/reopen and process restart are separate observations. Reconstructing an adapter, service provider, or generation counter in the test process does not satisfy process-restart evidence. A dedicated helper executable receives a closed manifest/provider probe request over redirected standard input, connects to the same durable target in a different operating-system process, and returns only sanitized identities, versions, process IDs, and payload digests. Connection values and credentials are never arguments or evidence.

MongoDB uses the ratified `mongo:7.0.24` replica-set fixture with two independently constructed clients. Topology admission proves replica-set identity, writable-primary state, and an actual transactional begin/rollback. Standalone MongoDB is a rejection fixture, not an allowed evidence substrate. Same-container restart uses a bounded idempotent replica-set startup callback so an already-initialized set is accepted without weakening readiness checks.

Native-plan evidence must represent the exact admitted Groundwork route. Groundwork preview.47 exposes this explain path for MongoDB but not through a public relational API; handwritten SQL may smoke-test a substrate but cannot satisfy route evidence. Before T084 can advance relational evidence, Groundwork must expose a public structured relational explain/execution-observer surface that retains route identity and a sanitized canonical plan without connection, database, server, or parameter values.

**Rationale**: Current evidence is dominated by memory/SQLite plus targeted PostgreSQL coverage. Repeating test bodies per provider invites semantic drift; memory-backed reopen cannot prove durability.

**Alternatives considered**:

- Provider-specific test suites with copied cases: rejected because expected domain outcomes could diverge unnoticed.
- In-memory provider as restart evidence: rejected because it cannot prove persistence, process coordination, or native execution.
- MongoDB standalone topology: rejected for scenarios that promise multi-document atomicity.
- Adapter recreation or a generation counter as process-restart evidence: rejected because no independent process reconnects to durable storage.
- Handwritten provider SQL as admitted-route evidence: rejected because it can diverge from the command Groundwork actually executes.

## Decision 6: Make ownership and checkpoint admission one durable fence decision

**Decision**: Persist execution ownership with expected-version compare-and-swap. Acquisition is create-only or a conditional update that issues a strictly greater fencing token; heartbeat and release are conditional on the current owner/token. The checkpoint request carries the expected fence. Inside the same Groundwork unit of work that writes checkpoint state, outbox state, and the create-only idempotency marker, the adapter conditionally validates/touches the ownership record. A stale token aborts before any checkpoint outcome becomes visible.

**Rationale**: Current instance semaphores do not coordinate nodes. The existing preflight `EnsureCurrentAsync` can pass and become stale before checkpoint commit, creating a TOCTOU window. Placement leases route work but cannot become the final execution-write authority.

**Alternatives considered**:

- Keep preflight validation outside the unit of work: rejected because the fence can change between validation and commit.
- Use a distributed application lock: rejected because provider-atomic version/fence semantics already express the invariant and survive lock-client failure.
- Treat placement lease ownership as sufficient: rejected because routing and durable execution admission have different failure windows.

## Decision 7: Use explicit optimistic-concurrency outcomes for IAM and secrets

**Decision**: #644 owns user, role, and external-login documents. #645 implements Elsa adapters to that authority, retains tenant membership where separately owned, and implements application, credential, claim-mapping, and provider-configuration outcomes without parallel user/role documents. Mutable IAM and secret contracts gain provider-neutral revision/conditional outcomes where their current signatures cannot express stale update/delete. `TryAdd` uses create-only storage; update/delete use expected versions; logical uniqueness is enforced by scoped physical keys/indexes.

Provider configuration is global only where its product semantics are genuinely host-wide; tenant-specific configuration remains scoped. Global reads may be ordinary where the contract permits them, while global writes use the separately declared privileged administration policy.

**Rationale**: The current Groundwork stores are last-write-wins and secret `TryAdd` is a read/save race. Hiding envelope versions would make domain callers unable to distinguish a conflict from success.

**Alternatives considered**:

- Keep unconditional upserts: rejected because concurrent changes silently overwrite one another.
- Preserve separate Elsa user/role documents beside #644: rejected because two authorities cannot stay transactionally consistent.
- Encode tenancy only in composite IDs: rejected because it is not an access boundary.

## Decision 8: Bind every scale-bearing query to a finite physical route

**Decision**: Inventory bookmarks, trigger stimulus, due timers, recurring schedules, recovery/liveness, outbox, queue, source references, execution/history, IAM, secrets, placement, and command backlog queries. Declare each through Groundwork's bounded query model, compile it to a versioned executable route, enforce a finite adapter maximum and complete deterministic ordering, and collect provider-native evidence that scope, predicates, ordering, continuation, count/distinct, and limiting execute before materialization.

Public contracts that currently imply unbounded results are changed to bounded page/continuation shapes. A provider missing a required route blocks composition; there is no production load-all fallback.

**Rationale**: Multiple current adapters query a broad collection and then filter, order, or take in memory. Small correctness fixtures hide this scale failure.

**Alternatives considered**:

- Retain equality side indexes and filter the candidate set locally: rejected because candidate cardinality remains unbounded.
- Expose SQL or provider-native query objects from Elsa contracts: rejected by provider neutrality and four-provider equivalence.
- Select physical entity tables for every query before measurement: rejected because #646 must compare forms and justify added schema cost.

## Decision 9: Keep Elsa's outbox model, but add atomic delivery ownership

**Decision**: Preserve checkpoint-linked Elsa outbox documents because their deterministic identity and inline-success path differ from Groundwork's generic operational outbox. Evolve the provider-neutral outbox contract to expose an atomic claim command with owner/lease token and visibility deadline. Completion/retry/cancel must present the current claim token; stale acknowledgement is rejected. Create is idempotent/create-only, deliverable selection is bounded FIFO/due order, and every transition uses expected-version mutation.

Apply the same transition discipline to the scheduler work queue, durable timers, recurring schedules, incidents, liveness, holds, publication projection state, and the missing poison store: create-only identity, conditional claim/advance/complete, bounded due queries, and named failure-window recovery.

**Rationale**: The current outbox query exposes an owner filter but does not claim, while delivery results carry no owner/token. Two workers can therefore dispatch the same item successfully. The existing Elsa semantics should be completed rather than forced into a different generic message model.

**Alternatives considered**:

- Replace the Elsa outbox contract with Groundwork's generic operational outbox: rejected because checkpoint-bundle identity and inline dispatch semantics differ.
- Keep read-then-record delivery: rejected because it cannot prevent duplicate successful ownership.
- Serialize workers with instance locks: rejected because it does not coordinate processes.

## Decision 10: Give distributed transport an atomic stream head

**Decision**: Placement claim/renew/takeover/release uses expected-version atomic transitions in a scoped session. Command transport uses deterministic create-only command identities and a per-execution stream-head document whose CAS update allocates the next sequence without scanning backlog. Visibility claims carry lease tokens and expiry; acknowledgement/delete requires the current token. Bounded retrieval preserves the declared stream order. `LeaseFencing` is advertised only after the provider-backed checkpoint fence scenario passes.

**Rationale**: Scanning for the maximum sequence is both a race and an unbounded hot path. Unconditional capability advertising currently promises stronger safety than the active path proves.

**Alternatives considered**:

- Application-level locks around scan/max: rejected because they do not survive or coordinate all hosts.
- Random ordering keys: rejected because the public transport declares per-execution order.
- Treat placement version as the checkpoint fence: rejected because stale route owners still need durable commit rejection.

## Decision 11: Separate #645 correctness inputs from #646 measurements

**Decision**: #645 owns versioned workload definitions, fixed seed/cardinality/concurrency inputs, correctness digests, provider prerequisites, and ledger mappings for every FR-030 workload. #646 owns the benchmark harness, warm-up/statistics, physical-form comparison, raw artifacts, reports, and Pass/Redesign/Blocked verdict. A #645 lane cannot become ready until it consumes the verdict; Redesign returns it to implementation.

**Rationale**: Correctness scenarios can land before the shared harness, but measurement authority and statistical method must not be duplicated in each store family.

**Alternatives considered**:

- Build ad-hoc benchmarks in each lane: rejected because results would not be comparable or independently reproducible.
- Wait for #646 before defining workloads: rejected because the harness needs stable consumer-owned operations and correctness baselines.

## Decision 12: Deliver in ten ratcheted boundaries

**Decision**: Sequence work as coverage/ratchets; package/composition/session substrate; shared provider fixture; scope adoption; ownership/checkpoint fencing; IAM/secrets; bounded runtime queries; operational runtime stores; distributed placement/transport; #646 handoff/verdict consumption. Each PR names and advances exact ledger rows and cannot mark a row complete with partial mandatory-provider evidence.

**Rationale**: Composition, scope, and test infrastructure are shared prerequisites. Operational and distributed transitions depend on bounded provider mutations and fencing semantics. The sequence permits safe parallel work after shared contracts stabilize without creating incompatible adapters.

**Alternatives considered**:

- One repository-wide implementation PR: rejected because review, CI diagnosis, and provider matrices would be unmanageable.
- Work store-by-store before shared composition/scope: rejected because every adapter would need rework and could establish incompatible conventions.

## Resolved Dependencies

- Groundwork #32 and #43–#48 provide scope, executable routes, applied state, planning, and four-provider execution. Elsa must consume them from one released version.
- Groundwork MongoDB bounded mutations are merged. Portable Unicode/long-value work #70 and identity case-policy work #71 must land before their dependent Elsa lanes pin a release; the plan does not hard-code preview.42 as the final version.
- #644 remains the authoritative user/role/external-login workstream. Spec 095 completes the ASP.NET Core Identity correctness lane with one Groundwork authority, four-provider/schema/highest-seam evidence, and the #646 `iam-normalized-lookup-update` handoff digest `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49`; this does not complete #646 timing or #647 EF deletion.
- #660 owns diagnostics-settings persistence and remains a coverage-ledger external-authority row.
- #646 owns performance measurement and physical-form verdicts; this feature owns workload definitions and correctness baselines.
- MongoDB atomic multi-document scenarios require a replica-set or sharded transaction-capable topology and fail startup clearly otherwise.

## Test-removal approval ledger

Delivery boundary 1 records every baseline test objective in the machine ledger. An implementation PR
may remove or rename a test only after adding an exact row here with its objective, replacement
evidence or invalid-objective rationale, named architect, decision, and date.

| Existing test | Objective | Replacement evidence / rationale | Architect | Decision | Date |
|---|---|---|---|---|---|
| tests/Elsa/Persistence/Groundwork/Tests/GroundworkWorkflowExecutionStatePagingTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkWorkflowExecutionStatePagingTests.Sqlite_startup_upgrades_v2_history_before_the_first_page_query | Prove the runtime upgrades stored execution-history documents before serving the first page query. | Replaced by `Sqlite_startup_does_not_rewrite_v2_history`, which proves the ratified deployment-owned schema boundary: runtime startup is read-only and preserves the existing v2 document; schema changes belong to the CLI. This is greenfield software, so no released runtime-upgrade behavior is being withdrawn. | Sipke Schoorstra | Approved | 2026-07-15 |
| tests/Elsa/Persistence/Groundwork/Tests/GroundworkDocumentStoreHolderTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkDocumentStoreHolderTests.Concurrent_publication_selects_one_complete_state_and_disposes_every_owner_once | Prove concurrent provider publication selects exactly one complete state, disposes every losing owner once, and disposes the winning owner exactly once across repeated shutdown calls. | Replaced by `tests/Elsa/Persistence/Groundwork/Tests/GroundworkStoreSessionSourceTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkStoreSessionSourceTests.Concurrent_publication_selects_one_complete_state_and_disposes_every_owner_once`, which preserves the objective against the scoped session-source seam that supersedes the application-wide holder. `Disposal_waits_for_an_inflight_open_and_rejects_every_later_open` and `Disposed_uninitialized_source_rejects_publication_and_open` add disposal/open race and terminal-state coverage. | Sipke Schoorstra | Approved | 2026-07-15 |
| tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkRuntimeDocumentFixtureTests.Committed_Fixture_Loads_Through_The_Bridge_Under_The_Legacy_Stamp | Prove every admitted runtime fixture loads through the versioned bridge. | Replaced by `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkRuntimeDocumentFixtureTests.Every_Supported_Fixture_Version_Loads_Through_The_Bridge` plus `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentSerializerTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkRuntimeDocumentSerializerTests.Deserialize_Rejects_Versions_Below_A_Kinds_Minimum_Readable_Boundary`, which together prove every current fixture loads and every pre-GA historical version is rejected before content parsing. | Sipke Schoorstra | Approved | 2026-07-16 |
| tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkRuntimeDocumentFixtureTests.Workflow_execution_state_shape_is_explicitly_versioned_at_v3 | Prove the workflow-execution-state fixture is explicitly versioned rather than drifting silently. | Replaced by `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs::Elsa.Persistence.Groundwork.Tests.GroundworkRuntimeDocumentFixtureTests.Dispatch_dependency_shapes_are_explicitly_versioned_at_v4` and `Version_4_runtime_shapes_use_a_clean_contract`, which preserve explicit version evidence for workflow execution state while ratifying v4 as the sole readable pre-GA baseline. | Sipke Schoorstra | Approved | 2026-07-16 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.List_ReturnsAllStoredLeases_OrderedByExecutionId | Prove placement discovery returns a deterministic view of stored leases. | Replaced by `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.ListOwned_ReturnsOnlyLiveOwnerLeasesInEarliestExpiryOrderWithinTheRequestedBound`, which strengthens the objective to the ratified provider-bounded live-by-owner contract and runs against in-memory, Groundwork-memory, and physical SQLite implementations. | Sipke Schoorstra | Approved | 2026-07-18 |
| tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/IdentityGroundworkStoreTests.cs::Elsa.Foundation.Identity.Persistence.Groundwork.Tests.IdentityGroundworkStoreTests.Save_Is_An_Upsert | Prove repeated role saves replace the existing row. | Reclassified and renamed to `Role_Save_replaces_an_existing_record_without_a_revision_contract`; create-only and optimistic-concurrency behavior remain separately covered by the revision-aware and ASP.NET Identity suites. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Secrets/Tests/GroundworkSecretRepositoryTests.cs::Elsa.Secrets.Tests.GroundworkSecretRepositoryTests.List_Includes_Deleted_Tombstones | Prove deleted secrets remain queryable as tombstones. | Replaced by `GroundworkV2SecretRepositoryTests.CrudRevisionTombstoneAndTenantIsolationUsePublicV2Contracts`, which exercises the clean-break public-v2 tombstone contract. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Secrets/Tests/GroundworkSecretRepositoryTests.cs::Elsa.Secrets.Tests.GroundworkSecretRepositoryTests.RoundTrips_And_Lists_Secrets_By_Collection_Index | Prove secret round-trip and bounded collection lookup behavior. | Replaced by `GroundworkV2SecretRepositoryTests.CrudRevisionTombstoneAndTenantIsolationUsePublicV2Contracts` and `GroundworkV2SecretRepositoryTests.ListCombinesPortableSearchFacetsStatusScopeCountAndPaging`, with native-provider coverage in `GroundworkV2SecretsProviderMatrixTests`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Secrets/Tests/SecretsGroundworkDocumentFixtureTests.cs::Elsa.Secrets.Tests.SecretsGroundworkDocumentFixtureTests.Committed_Fixture_Loads_Through_The_Repository_Under_The_Legacy_Stamp | Prove a v1 persistence fixture remains readable. | Retired by the explicitly approved clean-break boundary: v1 migration and compatibility are out of scope. `GroundworkV2SecretRepositoryTests.SchemaAndAssemblyProveTheCleanBreak` and the public-v2 provider restart matrix prove the replacement authority without loading legacy data. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Secrets/Tests/SecretsGroundworkDocumentFixtureTests.cs::Elsa.Secrets.Tests.SecretsGroundworkDocumentFixtureTests.Fixture_Matches_What_The_Repository_Writes_Today | Freeze the legacy secret-document wire shape. | Retired by the explicitly approved clean-break boundary. The v2 storage declaration and public repository/provider tests are the sole current authority; no legacy serialized shape is retained. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionCommandTransportContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionCommandTransportContractTests.Ack_ByLiveLeaseHolder_RemovesTheItem | Prove the current live lease holder can acknowledge and delete one command. | Replaced by `ExecutionCommandTransportContractTests.AckRequiresTheCurrentLiveHolderAndDeletesExactlyOneItem` over the product in-memory and public-v2 SQLite adapters. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionCommandTransportContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionCommandTransportContractTests.Ack_ByNonHolderOrAfterExpiry_IsRefused | Prove non-holders and expired holders cannot acknowledge a command. | Replaced by `ExecutionCommandTransportContractTests.ExpiredOrSupersededHolderCannotAck`, including stale-token refusal after a newer lease. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionCommandTransportContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionCommandTransportContractTests.Ack_BySupersededHolder_AfterSurvivorReleased_IsRefused | Prove a superseded holder cannot acknowledge after the successor releases. | Replaced by `ExecutionCommandTransportContractTests.ExpiredOrSupersededHolderCannotAck`, preserving the fencing objective on clean-break rows. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionCommandTransportContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionCommandTransportContractTests.Lease_HidesItemsFromOtherNodes_UntilExpiry | Prove a live command lease hides work until expiry. | Replaced by `ExecutionCommandTransportContractTests.LeaseHidesUntilExpiryThenIncrementsDeliveryAttempt`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionCommandTransportContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionCommandTransportContractTests.Lease_RespectsMaxItems_AndListPendingSpansExecutions | Prove lease and pending queries are bounded across executions. | Replaced by `ExecutionCommandTransportContractTests.LeaseAndPendingQueriesRespectBoundsAcrossExecutions`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionCommandTransportContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionCommandTransportContractTests.Send_AssignsStrictlyIncreasingSequences_AndLeaseDrainsInEnqueueOrder | Prove send assigns increasing sequence values and lease drains in order. | Replaced by `ExecutionCommandTransportContractTests.SendSequencesStrictlyIncreaseAndLeaseDrainsInOrder`; the four-provider matrix adds concurrent-send coverage. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.Claim_AgainstExpiredForeignLease_GrantsWithGreaterToken | Prove an expired foreign lease can be taken over with a greater fencing token. | Replaced by `ExecutionPlacementStoreContractTests.ExpiredForeignClaimTakesOverWithGreaterToken`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.Claim_AgainstLiveForeignLease_IsDeniedWithoutMutation | Prove a live foreign placement claim is denied without mutation. | Replaced by `ExecutionPlacementStoreContractTests.LiveForeignClaimIsDeniedWithoutMutation`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.Claim_ByCurrentOwner_RenewsWithStrictlyGreaterToken | Prove current-owner renewal advances the fencing token. | Replaced by `ExecutionPlacementStoreContractTests.ClaimGrantRenewAndFindHaveContractParity`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.Claim_OnUnplacedExecution_GrantsWithTokenOne | Prove a first placement claim starts at token one. | Replaced by `ExecutionPlacementStoreContractTests.ClaimGrantRenewAndFindHaveContractParity`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.Release_MatchingOwnerAndToken_Unplaces | Prove a matching owner/token release removes the placement. | Replaced by `ExecutionPlacementStoreContractTests.MatchingReleaseDeletesButStaleReleaseDoesNot`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/ExecutionPlacementStoreContractTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.ExecutionPlacementStoreContractTests.Release_WithStaleToken_IsANoOp | Prove a stale release cannot remove the current placement. | Replaced by `ExecutionPlacementStoreContractTests.MatchingReleaseDeletesButStaleReleaseDoesNot`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/GroundworkDistributedCasTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.GroundworkDistributedCasTests.ExpiredLeaseTakeoverRace_ExactlyOneNodeIsGranted | Prove concurrent expired-lease takeover has one storage CAS winner. | Replaced by `GroundworkV2DistributedStoreTests.ConcurrentExpiredPlacementTakeoversHaveOneStorageCasWinner`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/GroundworkDistributedCasTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.GroundworkDistributedCasTests.FirstClaimRace_ExactlyOneNodeIsGranted | Prove concurrent first placement claims have one storage CAS winner. | Replaced by `GroundworkV2DistributedStoreTests.ConcurrentPlacementClaimsHaveOneStorageCasWinner`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/GroundworkDistributedCasTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.GroundworkDistributedCasTests.LeaseRace_OnTheSameItem_ExactlyOneNodeHoldsIt | Prove concurrent command leases have one storage CAS winner. | Replaced by `GroundworkV2DistributedStoreTests.ConcurrentCommandLeasesHaveOneStorageCasWinner`. | Sipke Schoorstra | Approved | 2026-08-17 |
| tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/GroundworkDistributedCasTests.cs::Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.GroundworkDistributedCasTests.SendRace_OnTheSameSequence_LosesNoCommand | Prove concurrent sends on one stream lose no command and allocate distinct sequence values. | Replaced by `GroundworkV2DistributedProviderMatrixTests.PlacementAndAtomicCommandTransportConformAcrossNativeProviders`, which issues concurrent sends and validates both payloads and distinct sequence values on SQLite, PostgreSQL, SQL Server, and transactional MongoDB. | Sipke Schoorstra | Approved | 2026-08-17 |

## Primary Affected Paths

- `Directory.Packages.props`
- `src/Elsa/Persistence/Core/`
- `src/Elsa/Persistence/Groundwork/Scoping/GroundworkStoreSessionSource.cs`
- `src/Elsa/Persistence/Groundwork/Unified/GroundworkUnifiedManifest.cs`
- `src/Elsa/Persistence/Groundwork/{Stores,Querying,Serialization}/`
- `src/Elsa/Persistence/Groundwork/{Sqlite,SqlServer,PostgreSql,MongoDb}/`
- `src/Elsa/Workflows/Runtime/{Core,Services}/` ownership, checkpoint, outbox, queue, timer, schedule, and poison contracts
- `src/Elsa/Foundation/Identity/{Abstractions,Persistence/Groundwork}/`
- `src/Elsa/Secrets/{Core,Persistence/Groundwork}/`
- `src/Elsa/Workflows/Runtime/Distributed/{Contracts,Persistence/Groundwork}/`
- `tests/Elsa/Persistence/Groundwork/Testing/` and all four provider evidence projects
- affected domain tests, architecture ratchets, `EXTENSION_POINTS.md`, program-goal/decision-map state, and generated maps
