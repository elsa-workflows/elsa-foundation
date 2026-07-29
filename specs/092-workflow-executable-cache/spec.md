# Feature Specification: Bounded Workflow Executable Cache

**Feature Branch**: `codex/624-shell-readiness`

**Created**: 2026-07-11

**Status**: Approved for implementation

**Input**: GitHub issue #625 and the formal review of spec 091: repeated immutable workflow-executable artifact loads leave the reference HTTP workflow at roughly 360 ms warm p95, above the existing 50 ms budget.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reuse Immutable Executables (Priority: P1)

As a workflow runtime client, I can invoke the same published workflow repeatedly without reloading and deserializing its immutable executable artifact on every request.

**Why this priority**: Provider lookup and materialization dominate the remaining warm-request latency after cold activation is moved off the request path.

**Independent Test**: Save one executable through a durable store, resolve it repeatedly and concurrently through the cache, and verify callers receive the same content while the provider performs one load.

**Acceptance Scenarios**:

1. **Given** an executable exists in a durable provider, **When** it is requested repeatedly by content-addressed artifact ID, **Then** one provider load supplies all subsequent cache hits until eviction.
2. **Given** many callers miss the same artifact concurrently, **When** the provider load is in flight, **Then** they share one load without one caller's cancellation cancelling the shared result.
3. **Given** the provider returns not found or fails, **When** the same artifact is requested later, **Then** the result is retried rather than retained indefinitely.

---

### User Story 2 - Bounded and Correct Lifecycle (Priority: P2)

As an operator, I can bound executable-cache memory and trust saves, deletes, provider restarts, and source-reference changes to remain correct.

**Why this priority**: A fast cache is unsafe if it is unbounded or can serve an artifact after its durable lifecycle ended.

**Independent Test**: Configure a small capacity, exceed it, delete a cached artifact, and create a new service-provider instance; verify deterministic eviction, delete invalidation, and an initially empty cache after restart.

**Acceptance Scenarios**:

1. **Given** the configured capacity is reached, **When** another artifact is admitted, **Then** the least-recently-used entry is evicted and memory remains bounded.
2. **Given** a cached artifact is deleted, **When** it is requested again, **Then** the durable provider is consulted and the deleted value is not served.
3. **Given** the process or shell provider is recreated, **When** an artifact is first requested, **Then** the new cache starts empty and loads from the durable authority.
4. **Given** a mutable workflow source reference changes, **When** runtime resolution selects its new immutable artifact ID, **Then** the cache does not override that selection or alias IDs.

---

### User Story 3 - Observable and Configurable Operation (Priority: P3)

As an operator, I can enable, disable, size, and observe the cache using bounded signals, so I can tune or roll it back without changing workflow definitions.

**Why this priority**: Performance infrastructure needs safe operational controls and evidence that it is helping.

**Independent Test**: Exercise hit, miss, provider load, and eviction paths with listeners attached; verify stable counters/durations and validate enabled/capacity settings.

**Acceptance Scenarios**:

1. **Given** cache activity, **When** telemetry is collected, **Then** hit, miss, provider-load duration, and eviction observations are emitted without artifact IDs as metric dimensions.
2. **Given** caching is disabled, **When** an artifact is requested repeatedly, **Then** every request delegates to the durable provider with unchanged functional results.
3. **Given** an invalid capacity, **When** the runtime composition is validated, **Then** startup fails with an actionable configuration error.

### Edge Cases

- A provider load completes synchronously, returns null, throws, or is cancelled during a concurrent miss.
- Save or delete fails after a prior value is cached.
- Capacity is one and callers alternate between artifacts.
- A cache hit races with delete or save for the same artifact.
- The store's list operation returns many artifacts; listing must not flood or implicitly populate the cache.
- Telemetry listeners are absent or throw no application-visible behavior.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The runtime MUST provide an optional cache decorator for durable `IWorkflowExecutableStore` implementations without changing that store contract.
- **FR-002**: Cache keys MUST be immutable executable artifact IDs; mutable workflow/source references MUST remain authoritative outside the cache.
- **FR-003**: A successful lookup MUST be retained for reuse and a cache hit MUST avoid provider loading and executable deserialization.
- **FR-004**: Concurrent misses for the same artifact ID MUST coalesce into one provider load.
- **FR-005**: A caller cancelling its wait MUST NOT cancel or poison a shared provider load for other callers.
- **FR-006**: Null, failed, and cancelled provider loads MUST NOT be retained as cache entries or permanent in-flight entries.
- **FR-007**: The cache MUST enforce a configurable positive capacity using deterministic least-recently-used eviction and MUST have a documented bounded default.
- **FR-008**: Successful save and unconditional-delete operations MUST invalidate the cached value so the provider's idempotent-save result remains authoritative. A guarded delete MUST invalidate only when the provider reports that deletion succeeded. Failed mutations MUST leave the prior cache state unchanged.
- **FR-009**: List operations MUST delegate to the provider and MUST NOT implicitly populate the cache.
- **FR-010**: Resident cache entries MUST be limited to the runtime service-provider/shell lifetime so restart and shell replacement begin empty; cancellation/backpressure for provider loads that ignore host lifetime is tracked separately as lifecycle hardening.
- **FR-011**: Durable-provider composition MUST allow caching to be enabled or disabled and capacity to be configured without replacing source-reference resolution.
- **FR-012**: Telemetry MUST expose hit, miss, eviction, and provider-load duration/outcome using bounded metric dimensions and no workflow/artifact identifiers.
- **FR-013**: Existing in-memory and custom store implementations MUST remain usable without mandatory wrapping.
- **FR-014**: Provider-neutral tests MUST cover save/find/delete, concurrency, cancellation, null/failure retry, eviction, listing, disabled behavior, and telemetry; provider-backed tests MUST cover registration and restart behavior.
- **FR-015**: The combined spec 091/092 reference lane MUST satisfy first-after-ready p95 ≤750 ms and 200-request warm p95 ≤50 ms without restoring unconditional Groundwork rematerialization.
- **FR-016**: Root-write lease and deletion-guard acquisition, renewal, release, and cancellation operations MUST delegate directly to the provider so cache decoration preserves the executable-retention safety contract.
- **FR-017**: SQLite runtime composition MUST reuse a bounded set of immutable access-bound store adapters by default while every operation retains its own pooled connection and transaction; operators MUST be able to disable reuse and configure a positive capacity.
- **FR-018**: Durable executable retention coordination MUST be stored separately from the large immutable executable payload, with atomic create/delete behavior and lazy migration from the legacy embedded coordination fields.

### Key Entities

- **Executable artifact**: Immutable runnable workflow representation identified by a content-addressed artifact ID.
- **Cache entry**: One artifact ID, executable value, and least-recently-used position within a provider-local bounded cache.
- **In-flight load**: One shared provider lookup for an artifact ID, removed after every terminal outcome.
- **Cache observation**: A bounded hit, miss, eviction, or provider-load duration/outcome signal.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Repeated and concurrent lookup of one durable artifact performs exactly one provider load while resident.
- **SC-002**: The number of resident cache entries never exceeds configured capacity, and least-recently-used behavior is deterministic under test.
- **SC-003**: Unconditional and guarded delete, provider restart, null, failure, and cancellation tests never return stale or permanently suppressed results; lease and deletion-guard transitions retain provider behavior through the decorator.
- **SC-004**: Every cache path emits the required bounded telemetry and no artifact/workflow identifier appears as a metric dimension.
- **SC-005**: On the frozen reference lane, the first workflow request after readiness is ≤750 ms p95 and 200 warm requests are ≤50 ms p95.
- **SC-006**: Existing runtime, provider, workflow HTTP, shell lifecycle/isolation, and architecture suites pass without removed coverage.
- **SC-007**: The final 2x2 executable-cache/store-reuse lane identifies the decisive knob, and provider-backed tests preserve executable save, load, lease, guard, migration, and deletion behavior.

## Assumptions

- Executable artifact IDs are immutable/content-addressed; mutation creates a new ID.
- The durable executable provider remains the source of truth; this feature caches only positive immutable lookups.
- A default capacity of 256 executable artifacts is a conservative bounded starting point and is tunable per runtime composition.
- Cache state is intentionally process-local and is not distributed between nodes. Built-in Groundwork runtime and unified features enable it by default because artifact IDs are content-addressed and mutable source-reference selection remains authoritative. Operators that require coordinated eager eviction can disable it until distributed invalidation lands.
- This work belongs to the existing `first-request-cold-start-readiness` program-goal bucket; it closes an evidence-backed runtime performance follow-up without creating a new bucket.
