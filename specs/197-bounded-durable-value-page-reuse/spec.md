# Feature Specification: Bounded Durable-Value Page Reuse

**Feature Branch**: `claude/runtime-db-materialization-spec-2396`

**Created**: 2026-10-07

**Status**: Draft

**Input**: User description: Reduce repeated durable-value page reads during eligible coalesced workflow execution while preserving workflow-visible values, identity, isolation, write freshness, and existing persistence contracts.

## User Scenarios & Testing

### User Story 1 - Reuse unchanged durable-value pages (Priority: P1)

When a workflow starts a typed activity, records its deferred start checkpoint, and invokes it within the same eligible execution segment, the runtime can reuse previously fetched durable-value pages when the underlying data and access context have not changed. Workflow inputs, identity, variable visibility, and results remain the same as when reuse is disabled.

**Why this priority**: This is the intended reduction opportunity. It must be demonstrated on a normal, non-empty execution path rather than inferred from source order or an empty-state probe.

**Independent Test**: Run the same deterministic workflow with reuse enabled and disabled, observe backing page-request counts, and compare the complete workflow-visible values and result.

**Acceptance Scenarios**:

1. **Given** a non-empty durable-value state and an eligible coalesced execution with equivalent repeated page requests, **when** the typed start-to-invoke path runs, **then** the runtime reuses eligible raw pages and returns the same complete values and identity as the uncached path.
2. **Given** the same fixture run with reuse enabled and disabled, **when** both executions complete, **then** the enabled run makes fewer backing durable-value page requests and produces byte-equivalent workflow inputs, identity, variable visibility, and final result.

### User Story 2 - Observe staged and persisted changes immediately (Priority: P1)

When workflow execution changes durable values between reads, later reads observe the current values, including staged additions, updates, and deletions. A write attempt or ownership transition cannot leave an earlier page usable after its freshness boundary.

**Why this priority**: Stale durable values can change workflow behavior or leak data across executions; correctness and freshness are prerequisites for reuse.

**Independent Test**: Read pages, stage updates and deletions, then read again; also exercise persisted writes, failures, cancellation, and an in-flight page read that completes after invalidation.

**Acceptance Scenarios**:

1. **Given** a cached provider page and staged durable-value additions, updates, or deletions, **when** the same logical read occurs again, **then** the current staged changes are reflected and the complete logical result matches the uncached behavior.
2. **Given** an inner or direct durable-value write attempt, **when** it succeeds, fails, or is cancelled, **then** a read cannot use a pre-attempt page as current data.
3. **Given** a page load that began before invalidation, **when** it completes after that boundary, **then** its result cannot repopulate reusable state with stale data.

### User Story 3 - Preserve isolation and safe fallback (Priority: P1)

When execution is outside the eligible ownership segment, crosses an identity or authorization boundary, loses ownership, or reaches a reuse limit, the runtime obtains the complete result through the existing provider path. Reuse never weakens tenant, access, or provider rejection behavior.

**Why this priority**: Reuse must remain local to the authority and lifetime that made the original read valid, and it must fail safe when those conditions cannot be established.

**Independent Test**: Exercise different executions and access partitions, nested and disposed ownership, recovery and interruption, and a configured reuse-limit boundary; compare behavior with reuse disabled.

**Acceptance Scenarios**:

1. **Given** different execution, tenant, access-policy, or purpose contexts, **when** an otherwise similar page is requested, **then** data from another context is not reused and the provider's normal authorization result is preserved.
2. **Given** an Immediate path, no eligible active owner, nested ownership, disposal, interruption, recovery, or lost ownership, **when** values are requested, **then** no stale page is reused and the full provider result remains available.
3. **Given** the bounded reuse capacity is reached, **when** a further page is requested, **then** the runtime falls back to the provider for a complete result without truncation or partial workflow-visible state.

### Edge Cases

- A staged deletion must hide a row present in a cached provider page, while a staged update must replace the baseline value.
- A write failure or cancellation still ends the freshness of pages from before that write attempt.
- Nested execution, checkpoint flush, disposal, retry, recovery, and ownership loss must not allow a parent or prior segment's page to become reusable.
- A page request that completes late must not overwrite a newer generation of data.
- Mutable metadata or JSON-backed values returned to callers must not let a caller mutate data observed by a later cache hit; expired source storage must not invalidate a later result.
- Authorization failures, including privileged or cross-scope requests the provider rejects, must remain failures rather than becoming cache hits.
- Reads outside the eligible coalesced path continue to use the provider, including Immediate execution and calls without an active owner.

## Requirements

### Functional Requirements

- **FR-001**: The runtime MUST reuse only raw durable-value provider pages for equivalent requests within the same eligible, actively owned execution segment. It MUST NOT reuse projected activity inputs or workflow outputs as a substitute for current reads.
- **FR-002**: Every logical read MUST apply the current staged durable-value additions, updates, and deletions to any reused provider-page data before returning results.
- **FR-003**: Reuse MUST preserve the complete ordering, values, identity, and visibility observed by callers on the uncached path.
- **FR-004**: A reusable page MUST remain isolated from caller mutation and from the lifetime of mutable or disposable source objects.
- **FR-005**: Reuse MUST be isolated by the current execution and authorization context. A cache hit MUST NOT bypass authorization, tenant partitioning, or a provider rejection.
- **FR-006**: Durable-value write attempts and other freshness boundaries MUST invalidate or fence earlier page data before that data can be served again. This includes inner checkpoint writes, direct writes, and failed or cancelled attempts.
- **FR-007**: A page load that crosses an invalidation, ownership, or lifetime boundary MUST NOT restore stale data to reusable state when it completes late.
- **FR-008**: Reusable data MUST have explicit finite entry, row, and retained-data size bounds, including request keys and page contents. When reuse is unavailable, invalid, or over a bound, the runtime MUST preserve the existing provider outcome without truncating a successful result.
- **FR-009**: Reuse MUST end when its owning segment is disposed, interrupted, nested into an incompatible owner, or loses execution authority. A retry or recovery MUST establish fresh reusable state before reuse resumes.
- **FR-010**: Calls without eligible coalesced ownership, including Immediate execution, MUST retain the existing uncached behavior.
- **FR-011**: The feature MUST NOT change public store or paging interfaces, default checkpoint cadence, or durability semantics. It MUST NOT introduce a global cache or cache point-read misses.
- **FR-012**: The implementation MUST retain the existing valid PostgreSQL HTTP and REST behavior, including the HTTP reference scenario returning status 200 and `Alice Smith`.
- **FR-013**: A successful empty provider page MAY be reused under the same freshness, isolation, and capacity rules as a non-empty page. Failed, cancelled, or rejected reads MUST NOT become cached successes or misses.
- **FR-014**: A diagnostic control MUST allow comparison with page reuse disabled while retaining the same coalescing, persistence, and workflow settings.

### Key Entities

- **Raw durable-value page**: A complete provider page before current staged changes are applied.
- **Execution segment**: The bounded period of execution authority in which page reuse may be eligible.
- **Staged durable-value changes**: Uncommitted additions, updates, and deletions that must be applied to every logical read.

## Success Criteria

### Measurable Outcomes

- **SC-001**: On a deterministic, non-empty normal-path fixture for the eligible typed start-to-invoke flow, the enabled run makes fewer backing page requests than the disabled run while input values, identity, variable visibility, and final result are byte-equivalent.
- **SC-002**: The PostgreSQL HTTP reference scenario continues to return status 200 with `Alice Smith`; the existing valid REST regression continues to preserve its expected response and terminal workflow state.
- **SC-003**: Applicable C4 concurrency, recovery, partition-isolation, and supported-provider gates pass with reuse enabled and disabled, with no stale or cross-context values observed.
- **SC-004**: Limit, invalidation, cancellation, late-completion, and ownership-loss cases preserve the uncached path's complete successful results or its failures, rejection, and cancellation behavior as applicable. No failed or cancelled read populates reusable state.

## Assumptions

- Reuse is eligible only within existing coalescing ownership; this feature does not change the persistence or checkpoint contract.
- T07 source analysis and its retained CLR probe identify a reachable repeat-read mechanism, but the probe used empty durable-value state and an in-memory provider. It does not establish reduced reads for the primary HTTP or REST workloads. The non-empty enabled/disabled fixture and PostgreSQL regressions above are required evidence.
- Numeric page, row, and byte limits, and the internal invalidation mechanism, are design decisions for the implementation plan. This specification requires finite bounds and complete uncached fallback without prescribing their values.
- Bounded latency comparisons belong to the later T17/T18 accounting work. This specification makes no latency or end-to-end speedup guarantee.
