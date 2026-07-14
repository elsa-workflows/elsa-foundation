# Feature Specification: Harden Groundwork Store Families

**Feature Branch**: `codex/645-groundwork-store-hardening`

**Created**: 2026-07-14

**Status**: Draft

**Input**: User description: "Harden every already-landed Elsa Groundwork runtime, IAM, secrets, and distributed-runtime store so Groundwork can become the only first-party persistence implementation family in elsa-foundation. Preserve provider-neutral core contracts, prove equivalent behavior across SQLite, SQL Server, PostgreSQL, and MongoDB, close composition, tenancy, concurrency, recovery, bounded-query, and performance gaps, and avoid duplicate identity authorities."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Account For Every Durable Contract (Priority: P1)

An Elsa maintainer can inspect one coverage ledger and determine how every durable runtime, IAM, secrets, and distributed-runtime contract is stored, scoped, queried, tested, and owned.

**Why this priority**: A zero-EF exit is unsafe while durable contracts can be omitted, silently left in memory, or claimed by overlapping persistence authorities.

**Independent Test**: Compare the ledger with the contracts registered or consumed by the four in-scope store families and verify that every contract has exactly one durable outcome, an explicit scope, an accountable owner, and required evidence.

**Acceptance Scenarios**:

1. **Given** the current Elsa durable-contract inventory, **When** it is compared with the coverage ledger, **Then** every in-scope contract has one recorded durable implementation outcome or an explicit, linked exclusion owner.
2. **Given** a contract without a current Groundwork implementation, **When** the ledger is reviewed, **Then** the missing implementation is visible and assigned rather than treated as complete.
3. **Given** identity user, role, or external-login persistence, **When** ownership is reviewed, **Then** the ledger points to the authoritative Identity work and does not establish a second document authority.
---

### User Story 2 - Compose Selected Stores In A Real Host (Priority: P1)

An app host can select any supported combination of runtime, IAM, secrets, and distributed persistence, and all selected durable requirements are included in one coherent storage composition before the host accepts work.

**Why this priority**: Isolated store tests do not prove that a real host prepares every selected feature's storage or rejects conflicting declarations.

**Independent Test**: Start a production-shaped host with all in-scope store families, write and query through each public contract, dispose and reopen the host over the same database, and verify that invalid compositions fail before serving work.

**Acceptance Scenarios**:

1. **Given** a host that selects several in-scope store families, **When** its persistence composition is validated, **Then** all selected storage requirements are included together.
2. **Given** two selected features with incompatible durable requirements, **When** composition is validated, **Then** startup fails with a diagnostic that identifies both owners.
3. **Given** a selected store whose durable requirement is absent, **When** the host initializes, **Then** startup fails rather than exposing a partially functional feature.
4. **Given** the same selected feature set on another supported provider, **When** the host starts, **Then** no core contract or feature behavior has to change.
5. **Given** a restarted host over the same durable database, **When** every selected family is queried, **Then** previously committed data remains available through its public contract.

---

### User Story 3 - Preserve Atomic Runtime Behavior (Priority: P1)

An operator can run multiple Elsa nodes concurrently without duplicate ownership tokens, stale-owner commits, duplicate queue delivery, lost outbox state, or unsafe timer and schedule transitions.

**Why this priority**: Runtime correctness depends on cross-process atomicity and recovery behavior, not merely on durable reads and writes.

**Independent Test**: Exercise ownership, checkpoint, recovery, outbox, work-queue, timer, recurring-schedule, and distributed-command scenarios with concurrent independent clients, injected failures, cancellation, and restart, then verify the externally observable outcomes.

**Acceptance Scenarios**:

1. **Given** two contenders for the same execution, **When** ownership changes concurrently, **Then** fencing tokens remain unique and monotonic and a stale owner cannot commit a checkpoint.
2. **Given** concurrent workers draining the same queue or outbox, **When** they claim and complete work, **Then** one logical item is not returned as successfully owned by more than one worker and completion state is not lost.
3. **Given** a failure at any durable transition boundary, **When** the process or provider restarts, **Then** recovery converges without partial checkpoint state, lost required work, or duplicate successful effects.
4. **Given** concurrent creation or advancement of timers, recurring schedules, incidents, and other create-once state, **When** conflicts occur, **Then** the contract's existing-winner and concurrency outcomes remain deterministic.

---

### User Story 4 - Enforce Storage Scope (Priority: P1)

A tenant administrator can rely on the persistence boundary to prevent one tenant from loading, changing, querying, or deleting another tenant's data, even if a caller supplies a colliding identifier.

**Why this priority**: Caller-side key conventions are not a sufficient isolation boundary for durable multi-tenant state.

**Independent Test**: Store equivalent identifiers in distinct tenant scopes, attempt all supported operations through the wrong scope, and verify isolation; separately exercise explicitly global storage and privileged operation access, and verify that each is independently classified, restricted, and observable.

**Acceptance Scenarios**:

1. **Given** equal identifiers in two tenant scopes, **When** either tenant reads, queries, updates, or deletes its data, **Then** only that tenant's data is affected.
2. **Given** an ordinary session, **When** it attempts global or another-tenant access, **Then** access is denied before data is returned or changed.
3. **Given** an operation over an explicitly global storage unit, **When** it is executed, **Then** global scope is selected for the recorded storage reason, and any operation that also requires elevated authority uses its separately declared privileged access policy and records the named purpose and outcome.
4. **Given** cancellation, disposal, or failure inside a scoped unit of work, **When** another request reuses provider resources, **Then** tenant scope and transaction state do not leak across requests.
5. **Given** a tenant-scoped or privileged operation that emits operational telemetry, **When** metrics are recorded, **Then** tenant identifiers are not used as metric labels and the privileged access remains diagnosable through bounded operational records.

---

### User Story 5 - Preserve IAM And Secrets Concurrency (Priority: P1)

An administrator can create and update IAM records and secrets concurrently without duplicate authorities, silent overwrites, cross-tenant leakage, or false restart guarantees.

**Why this priority**: Ordinary stores still require atomic creation and revision behavior; a durable round trip alone does not protect credentials, mappings, memberships, or secrets from races.

**Independent Test**: Exercise each in-scope IAM and secrets contract with concurrent independent clients, stale revisions, duplicate creation, tenant collisions, disposal and reopen, and verify the same domain outcomes across the mandatory provider matrix.

**Acceptance Scenarios**:

1. **Given** two clients creating the same logically unique secret or IAM record, **When** they race, **Then** exactly one create succeeds and the existing record is not overwritten.
2. **Given** a client holding a stale revision, **When** it updates or deletes a record, **Then** the operation reports the contract's conflict outcome and does not change the current record.
3. **Given** user, role, or external-identity operations, **When** they are persisted, **Then** they adapt to the authority owned by #644 and do not create parallel documents.
4. **Given** an in-scope IAM contract without a current durable implementation, **When** the coverage ledger is completed, **Then** it has an implemented outcome and provider evidence or remains an explicit exit blocker.
5. **Given** committed IAM or secret data, **When** a newly opened store reconnects after restart, **Then** data, uniqueness, revision, and tenant behavior remain intact.

---

### User Story 6 - Preserve Distributed Takeover And Delivery (Priority: P1)

An operator can fail over execution placement and command delivery between nodes without losing commands, reordering a declared stream, accepting stale acknowledgements, or treating routing as the final execution-safety authority.

**Why this priority**: Multi-node correctness depends on takeover, visibility, acknowledgement, and fencing behavior continuing to agree during races and recovery.

**Independent Test**: Run concurrent send, lease, renew, takeover, acknowledgement, cancellation, failure, and restart scenarios across multiple independent clients and verify placement, transport, ownership, and checkpoint outcomes together.

**Acceptance Scenarios**:

1. **Given** two nodes competing for one placement, **When** the current lease expires or is released, **Then** exactly one successor becomes current and stale owners cannot release or renew it.
2. **Given** concurrent senders for one execution, **When** commands are accepted, **Then** their identities and declared order are durable without a whole-backlog allocation race.
3. **Given** a consumer whose visibility lease expires, **When** a successor reclaims the command, **Then** the stale consumer cannot acknowledge or remove the successor's work.
4. **Given** a node failure during command processing, **When** recovery runs, **Then** required commands are redelivered according to the contract without loss or duplicate successful acknowledgement.
5. **Given** a placement winner, **When** it attempts an execution commit, **Then** durable fencing and checkpoint admission—not placement alone—determine whether the commit is valid.

---

### User Story 7 - Keep Scale-Bearing Queries Bounded (Priority: P1)

An app host can grow runtime, IAM, secrets, and distributed data without store operations loading an entire logical collection and filtering or ordering it in application memory.

**Why this priority**: Transitional client-side scans make apparently correct adapters fail unpredictably at production scale.

**Independent Test**: Run every scale-bearing query shape against a dataset larger than its requested result window and verify bounded results, stable ordering, complete result equivalence, and bounded-execution evidence showing that scope, predicates, ordering, and limits execute at the storage boundary.

**Acceptance Scenarios**:

1. **Given** due work, recovery candidates, outbox entries, source references, secrets, placement leases, or command backlog larger than the requested page, **When** the store queries them, **Then** the provider returns only the bounded result shape required by the contract.
2. **Given** a query shape unsupported by a selected provider, **When** the host validates its composition, **Then** startup fails with a clear capability diagnostic rather than enabling an unbounded fallback.
3. **Given** equivalent data on each supported provider, **When** the same query is executed, **Then** filtering, null and missing-value behavior, ordering, counts, and continuation boundaries are equivalent.

---

### User Story 8 - Prove Provider Equivalence And Recovery (Priority: P1)

An app host can select SQLite, SQL Server, PostgreSQL, or MongoDB for the in-scope Elsa store families and receive the same public store behavior.

**Why this priority**: Elsa's database portability claim is only credible when every advertised provider runs the same behavioral, concurrency, restart, and failure suite.

**Independent Test**: Execute one shared black-box suite against each provider using real persistent storage, independent clients, disposal and reopen, process restart where relevant, concurrent operations, failure injection, and bounded-execution evidence.

**Acceptance Scenarios**:

1. **Given** a store contract scenario, **When** it is executed on all four providers, **Then** observable results and domain-level failure classifications agree.
2. **Given** state written before disposal, **When** a new store instance reconnects to the same persistent database, **Then** the state and its required revision, uniqueness, ownership, and idempotency information remain correct.
3. **Given** a provider-specific limitation, **When** the host advertises a capability, **Then** a tested executable path exists for that capability on that provider.
4. **Given** a provider that cannot satisfy a required store contract, **When** composition is validated, **Then** the host fails clearly instead of advertising partial support.

---

### User Story 9 - Supply And Consume Performance Evidence (Priority: P2)

An Elsa performance owner receives representative #645 workloads with verified outcomes and can use the benchmark program owned by #646 to decide whether each in-scope store is ready, blocked, or requires redesign.

**Why this priority**: Correctness is mandatory, but readiness also requires an independently reproducible verdict without duplicating the shared benchmark harness inside each store family.

**Independent Test**: Submit every required #645 workload and correctness baseline to #646, then verify that each in-scope lane consumes a reproducible pass, redesign, or blocked verdict before it is marked ready for EF removal.

**Acceptance Scenarios**:

1. **Given** the required runtime, IAM, secrets, and distributed workloads, **When** #645 reaches its evidence gate, **Then** every workload and verified outcome baseline has been supplied to #646.
2. **Given** a runtime hot-path or ordinary-store workload, **When** #646 returns a verdict, **Then** the owning #645 lane accepts a passing verdict or remains blocked for redesign.
3. **Given** a storage-shape recommendation from #646, **When** the owning lane records readiness, **Then** the recommendation and its reproducible evidence are linked to that decision.
4. **Given** a missing or non-reproducible #646 verdict, **When** EF removal readiness is evaluated, **Then** the affected #645 lane remains incomplete.

### Edge Cases

- Two processes acquire execution ownership after reading the same previous token.
- An owner passes a liveness check, loses ownership, and attempts to commit with the stale token.
- Two workers observe the same queue or outbox item before either completes it.
- A checkpoint, claim, acknowledgement, or schedule advance fails after some writes but before completion is reported.
- The same idempotency key is retried with equivalent input, conflicting input, or after restart.
- A tenant-scoped identifier is equal to an identifier in another tenant or a global partition.
- A provider resource is reused after cancellation, failed transaction cleanup, or scope disposal.
- A query returns the correct small sample while still scanning or materializing the full logical collection.
- A provider reports a capability through configuration even though no active execution path can satisfy it.
- A restart test recreates only an adapter while retaining the same in-memory substrate and is incorrectly presented as durability evidence.
- Two selected feature manifests declare incompatible storage for the same logical unit.
- The #644 authoritative identity model changes while a dependent IAM adapter is being hardened.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The feature MUST maintain a coverage ledger for every durable runtime, IAM, secrets, and distributed-runtime contract discovered in the repository. The baseline MUST account for bookmark state and stimulus lookup; workflow executable artifacts and source references; activity and workflow execution state and inspection; durable values; scheduler, liveness, ownership, hold, and incident state; checkpoints and idempotency records; outbox, queue, poison, timer, trigger-binding, recurring-schedule, and publication-owned runtime projection state supporting trigger bindings and recurring schedules; Elsa IAM user, role, external-identity, tenant-membership, application, credential, claim-mapping, and provider-configuration contracts; secrets; distributed placement; and distributed command transport.
- **FR-002**: Each ledger entry MUST identify its owning store family, durable outcome, storage scope, required query shapes, concurrency semantics, restart and failure scenarios, provider evidence, required performance verdict, dependency, and delivery owner.
- **FR-003**: Each durable outcome MUST select one accountable path: ordinary document storage, an operational store, a specialized primitive or query handler, adaptation to authority owned by another workstream, or an explicit linked exclusion.
- **FR-004**: A baseline contract without a current durable implementation, including the scheduler poison contract, MUST remain incomplete until its durable outcome is implemented or accepted by its linked external owner.
- **FR-005**: The ledger MUST route diagnostic-settings persistence to the diagnostics workstream, route authoritative user, role, and external-login documents to #644, and prevent this feature from creating parallel authority for those concepts.
- **FR-006**: Every selected in-scope feature family MUST contribute its durable requirements to one host-selected storage composition before the host serves work.
- **FR-007**: Composition validation MUST reject missing, duplicate, incompatible, or unsupported durable requirements with a diagnostic that identifies the owning features.
- **FR-008**: A production-shaped host MUST prove that runtime, in-scope IAM, secrets, and distributed stores can be enabled together, used through their public contracts, disposed, and reopened over the same durable database.
- **FR-009**: Every in-scope storage unit MUST be classified as tenant-scoped, explicitly global, or externally owner-classified, with a storage reason for every explicitly global unit; operation access MUST be classified separately as ordinary, privileged, ordinary-read/privileged-write, or externally owner-classified, with an authorization reason for every policy containing privileged access.
- **FR-010**: Tenant scope MUST be enforced for direct loads, writes, deletes, queries, mutations, recovery, and units of work at the persistence boundary; wrong-scope operations MUST NOT disclose whether another tenant's record exists.
- **FR-011**: Privileged access MUST reject ordinary callers and record the access scope, named purpose, and outcome without exposing tenant identifiers as unbounded telemetry labels.
- **FR-012**: Execution ownership MUST issue unique, strictly increasing fencing tokens across independent processes, release, failure, and restart.
- **FR-013**: A checkpoint commit MUST reject a stale fencing token as part of the same atomic durable decision that persists the checkpoint outcome.
- **FR-014**: Checkpoint idempotency MUST hold across concurrent independent writers, retry, failure, and restart.
- **FR-015**: Queue and outbox behavior MUST prevent multiple workers from successfully owning the same logical delivery at once and MUST preserve retry, completion, attempt, and stale-acknowledgement outcomes under concurrency.
- **FR-016**: Timer creation, recurring-schedule advancement, incident creation, and other declared create-once runtime transitions MUST preserve their winner and revision outcomes under races.
- **FR-017**: Every multi-record runtime operation that promises atomicity MUST either commit all required state or expose no committed partial outcome after failure and restart.
- **FR-018**: Scheduler poison records MUST survive disposal and restart while preserving the identity and retry relationship of the failed work item.
- **FR-019**: IAM, secrets, and ordinary runtime stores MUST enforce atomic add-if-absent behavior for logically unique records and revision-aware update and delete behavior wherever their public contract exposes uniqueness or optimistic concurrency.
- **FR-020**: IAM and secrets provider evidence MUST cover duplicate creation, stale update and delete, tenant collisions, independent clients, disposal and reopen, and adaptation to #644 authority where applicable.
- **FR-021**: Distributed placement MUST preserve atomic claim, renewal, takeover, monotonic versioning, and stale release, while durable execution fencing remains the final commit authority.
- **FR-022**: Distributed command transport MUST preserve command identity, declared order, bounded acceptance and retrieval, visibility ownership, retry, acknowledgement, stale-acknowledgement rejection, failover redelivery, and restart behavior.
- **FR-023**: Every scale-bearing query MUST declare a finite result bound and deterministic ordering where applicable, and MUST execute filtering, ordering, continuation, counting, distinct selection, and limiting at the persistence boundary.
- **FR-024**: Production composition MUST reject an unsupported scale-bearing query rather than use whole-collection materialization or another unbounded client fallback.
- **FR-025**: Every scale-bearing query MUST have result-equivalence tests and independent storage-boundary execution evidence for each provider on which it is advertised.
- **FR-026**: One shared black-box behavior suite MUST exercise each in-scope public store contract across SQLite, SQL Server, PostgreSQL, and MongoDB without provider-specific expected domain outcomes.
- **FR-027**: Mandatory provider evidence MUST use real persistent storage and cover independent clients, disposal and reopen, concurrency, cancellation, declared failure windows, and restart; a memory-backed test double MUST NOT count as provider, restart, or cross-process durability evidence.
- **FR-028**: Observable results MUST remain equivalent across providers, including ordering, counts, null and missing-value semantics, conflict outcomes, idempotency, and domain-level failure classification.
- **FR-029**: Every advertised provider capability MUST derive from an active tested execution path, and composition MUST fail when a required capability has no such path.
- **FR-030**: This feature MUST supply #646 with verified workloads for checkpoint commit, bookmark lookup, trigger-binding stimulus lookup, recovery scan, queue drain, outbox drain, due timer selection, recurring-schedule selection, IAM normalized lookup and update, secret create and read, bounded secret-list query, placement takeover, and command send, lease, and acknowledgement; each workload MUST include a correctness baseline before timing is considered. Every coverage entry classified as a hot path MUST map to one of these workloads or to an explicitly accepted representative workload recorded in the ledger before measurements begin.
- **FR-031**: Each in-scope lane MUST consume #646's reproducible pass, redesign, or blocked verdict and MUST remain incomplete while that verdict is missing or blocked.
- **FR-032**: Core modules and their persistence contracts MUST remain free of Groundwork dependencies and provider-specific behavior.
- **FR-033**: This feature MUST NOT add new EF migrations or expand the EF persistence surface; while EF remains a temporary oracle, the same observable contract scenarios MUST run against EF and Groundwork where an EF implementation exists.
- **FR-034**: Existing behavioral tests for refactored stores MUST remain present and passing; removing a test requires the repository's recorded-approval process.
- **FR-035**: Logic-bearing store adapters, aggregators, handlers, access-context selectors, and unit-of-work/session consumers MUST be scoped unless a narrower constitution-compliant lifetime is required; every non-scoped exception MUST be documented and covered by registration/lifetime tests that prove request scope and mutable operation state cannot leak.

### Requirement Traceability

| Requirements | Primary scenario | Measurable outcome |
|---|---|---|
| FR-001–FR-005 | User Story 1 | SC-001 |
| FR-006–FR-008 | User Story 2 | SC-002 |
| FR-009–FR-011 | User Story 4 | SC-004 |
| FR-012–FR-018 | User Story 3 | SC-005 |
| FR-019–FR-020 | User Story 5 | SC-003, SC-005 |
| FR-021–FR-022 | User Story 6 | SC-003, SC-005 |
| FR-023–FR-025 | User Story 7 | SC-006 |
| FR-026–FR-029 | User Story 8 | SC-003, SC-007 |
| FR-030–FR-031 | User Story 9 | SC-008, SC-009 |
| FR-032 | User Story 1 | SC-011 |
| FR-033 | User Stories 1, 8, and 9 | SC-012, SC-013 |
| FR-034 | User Story 1 | SC-010 |
| FR-035 | User Stories 2 and 4 | SC-002, SC-004, SC-014 |

### Key Entities

- **Persistence Coverage Entry**: The authoritative status of one durable contract, including owner, scope, behavior, query, provider, recovery, and performance obligations.
- **Durable Outcome**: The one accountable persistence path assigned to a coverage entry: ordinary document storage, operational storage, a specialized primitive or query path, adaptation to externally owned authority, or linked exclusion.
- **Storage Composition**: The complete set of storage declarations selected by one application host and validated as a coherent whole.
- **Storage Scope**: The tenant-scoped, explicitly global, or externally owner-classified partition in which data is stored and accessed. Privilege is a separate operation access policy, not a storage scope.
- **Operation Access Policy**: The ordinary or privileged authorization required for an operation independently of the storage unit's scope.
- **Fencing Token**: A strictly increasing ownership number included in a commit decision so an earlier owner cannot write after a successor takes over.
- **Scale-Bearing Query**: A collection query whose possible dataset size is not capped by a documented business invariant and therefore requires a finite result bound and storage-boundary execution.
- **Operational Transition**: A durable ownership, checkpoint, claim, acknowledgement, retry, completion, or schedule change whose atomicity and concurrency outcome are part of the public contract.
- **Failure Window**: A named interruption point before, during, or after a durable transition, with an explicit set of allowed durable outcomes and a recovery expectation.
- **Provider Evidence Record**: Reproducible proof that one provider satisfies a contract scenario, including persistent restart and bounded-execution evidence where applicable.
- **Bounded-Execution Evidence**: Independently inspectable evidence that a scale-bearing operation applied its required scope, predicates, ordering, and finite bound before results reached application memory.
- **Capability Claim**: A host-visible promise backed by a tested executable path for a specific provider and composition.
- **Performance Verdict**: The reproducible pass, redesign, or blocked decision produced by #646 for a workload with an already verified correctness baseline.
- **Behavioral Baseline**: The named set of existing store-test objectives recorded in the coverage ledger at the feature baseline commit, used as the fixed denominator for refactoring continuity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The coverage ledger accounts for 100% of durable contracts in the four in-scope store families, with zero unowned or silently in-memory contract rows.
- **SC-002**: 100% of mandatory production-composition matrix rows enable every selected in-scope store family together and pass validation, public-contract use, disposal, and reopen.
- **SC-003**: 100% of mandatory provider-matrix contract scenarios produce equivalent observable outcomes.
- **SC-004**: Tenant-isolation suites report zero cross-tenant reads, writes, deletes, query results, or unit-of-work effects across all supported operations and providers.
- **SC-005**: Deterministic concurrency and failure suites report zero duplicate successful ownership outcomes, zero stale-owner checkpoint commits, zero lost required completions, and zero committed partial atomic outcomes.
- **SC-006**: 100% of scale-bearing query shapes have bounded-result tests and accepted bounded-execution evidence, with zero production-enabled unbounded client fallbacks.
- **SC-007**: 100% of advertised provider capabilities map to a passing active-path scenario; no configuration-only capability claims remain.
- **SC-008**: 100% of the workloads listed in FR-030 are supplied to #646 with matching correctness baselines and reproducible input definitions.
- **SC-009**: 100% of in-scope store lanes have an explicit #646 verdict, and zero lanes are marked ready while their verdict is missing, blocked, or calls for redesign.
- **SC-010**: 100% of the named behavioral scenarios recorded in the ledger at the feature baseline commit remain covered and passing, with zero removals lacking a linked architect approval.
- **SC-011**: The complete dependency audit reports zero violations of the ratified provider-neutral core boundary.
- **SC-012**: The retiring persistence-surface ratchet reports zero additions relative to the feature baseline.
- **SC-013**: 100% of shared observable contract scenarios execute against both the temporary oracle and the replacement wherever both implementations exist.
- **SC-014**: Registration/lifetime tests report zero logic-bearing persistence services with an undocumented non-scoped lifetime and zero scope or mutable-operation-state leakage across independently created request scopes.

## Assumptions

- The zero-EF provider boundary and Groundwork-only first-party implementation decision are already ratified.
- The Elsa and framework constitutions remain draft quality-gate documents; this feature depends only on their currently applicable provider separation, refactoring continuity, exception-boundary, documentation, and test gates.
- The software is greenfield and unreleased, so migrating existing EF-backed production data is outside this feature.
- Groundwork #32 supplies storage-boundary scope; #43 executable routes; #44 durable applied-state comparison; #45 bounded query planning; #46 SQLite execution; #47 SQL Server and PostgreSQL execution; and #48 MongoDB execution. These issues are complete upstream, but dependent Elsa work consumes them only through a versioned Groundwork release.
- #644 owns the authoritative ASP.NET Core Identity user, role, and external-login documents. This feature owns dependent Elsa IAM adapter hardening, tenant membership, missing Elsa IAM contract outcomes, and reusable provider evidence without duplicating that authority.
- Diagnostics persistence, ASP.NET Core Identity framework-store implementation, and OpenIddict implementation are delivered by their respective sibling workstreams; this feature records their boundaries but does not implement them.
- #646 owns the shared benchmark harness, statistical method, cross-family report, storage-shape comparison, and final threshold verdicts. This feature supplies the exact FR-030 workloads and correctness baselines and consumes those verdicts as readiness gates.
- The provisional thresholds evaluated by #646 are: runtime hot-path p95 no worse than 1.10 times the temporary oracle and throughput at least 90%; ordinary-store p95 no worse than 1.25 times the oracle and throughput at least 80%; and p99 no worse than 2 times the oracle unless a reviewed workload-specific gate replaces them.
- EF remains available only as a temporary correctness and performance oracle until the parent zero-EF program authorizes final deletion.
- Existing correct Groundwork adapters are hardened and reused; they are rewritten only when required behavior or evidence proves the current design insufficient.
- Provider-specific deployment prerequisites, such as transactional topology requirements, may be documented as explicit support conditions but do not permit weaker public store behavior.
