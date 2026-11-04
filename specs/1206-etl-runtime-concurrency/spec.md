# Feature Specification: ETL runtime concurrency on the server engines

**Feature Branch**: `agent/codex/2908-etl-runtime-concurrency` (stacked on #3803)

**Created**: 2026-11-04

**Status**: Implemented — delivery and resulting-main verification remain pending

**Input**: The owner-requested next slice from [spec 1203's handoff](../1203-etl-runtime-store-schema/HANDOFF.md), called “spec 1204” there. Numbers 1204 and 1205 were already allocated when continuation refreshed the repository and open PR heads, so this same slice is numbered 1206. Part of #2908 and Program #2892.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Run ETL using either store on the chosen engine (Priority: P1)

An operator selects PostgreSQL, SQL Server or MySQL for ETL design and runtime persistence. The ordinary host starts, publishes a pipeline, runs it from a native workflow across at least two partitions and exposes its result after restart. The operator does not bypass a registration guard or construct stores directly.

**Why this priority**: Schema qualification alone does not make the server runtime usable in a host.

**Independent Test**: An owned Workbench and fresh database on each server engine create/publish/run a pinned pipeline, deliver its expected file records, complete its native caller once, restart against the same retained stores, and read back the exact definition and run identities.

**Acceptance Scenarios**:
1. **Given** both ETL stores configured on one supported engine, **When** the ordinary host starts and runs the published pipeline, **Then** two or more partitions settle and its native caller completes with the expected output.
2. **Given** the completed run and published definition, **When** the host restarts against the same stores, **Then** their immutable identities, terminal outcome and inspection remain available.
3. **Given** SQLite configuration or runtime-only composition, **When** the host composes its ETL runtime, **Then** the existing default and separation from design persistence remain supported.

### User Story 2 — Competing workers settle one authoritative outcome (Priority: P1)

Independent workers contend for preparation, partitions, reservation budgets, acceptance and terminal delivery. A temporary database write conflict does not make a correct worker lose its run or advance another worker's state.

**Why this priority**: The existing refusal is removed only when real server concurrency is qualified.

**Independent Test**: Real-engine races and a bounded correctness workload use separate contexts for each participant and verify exclusive live authority, exact counters, one accepted result/outbox identity, replay/conflict outcomes and stale-writer refusal.

**Acceptance Scenarios**:
1. **Given** two writers racing the same admission or transition, **When** one loses a retryable storage conflict, **Then** the whole transition is retried against current state and returns its established contract outcome.
2. **Given** concurrent reservations or final acceptance/cancellation, **When** both race, **Then** committed outstanding budgets stay within limits and only the winner's authoritative state advances.
3. **Given** repeated transient conflicts, **When** the bounded retry budget is exhausted, **Then** an ETL-owned failure reports the operation and preserves the underlying cause; no successful outcome is fabricated and the context contains no abandoned pending write.
4. **Given** cancellation or a transaction owned by the caller, **When** an operation is requested, **Then** cancellation is respected; unsupported caller transactions are refused before mutation and the store performs no retry inside or outside them.

### Edge Cases

- Two callers admit the same run, including matching replay and mismatching immutable invocation content.
- Expired leases, stale fences and a run made terminal by another participant while an attempt retries.
- A failed attempt that tracked inserts before rollback; the next call must not publish those inserts later.
- Conflicts during reads inside one transition versus standalone runtime inspection.
- Physical connector effects remain outside the database retry boundary and retain their existing at-least-once guarantees.
- SQLite's key format, migrations, frozen authoring/artifact bytes and actual retained fixtures remain unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** Every runtime-owned store operation that can observe a retryable write conflict retries its complete logical database unit, with a finite shared attempt budget. Its transaction ends before a new attempt begins; reads and writes from different attempts are never mixed. The store never retries inside a caller-owned transaction.
- **FR-002** Retry preserves the existing admission, lease, reservation, acceptance, cancellation, terminal and outbox contracts, including scoped identities, fencing, exact accounting and idempotent replay. Repeated attempts create no authoritative duplicate result. Caller-owned inputs are snapshotted once before the first database attempt, and every retry uses that same immutable snapshot.
- **FR-003** Terminal infrastructure failures throughout the runtime store boundary, including standalone inspection, non-retryable storage failures and retry exhaustion, are ETL-owned exceptions carrying the original cause and the operation plus scope/run identifiers where available. Public contracts document that failure. Cancellation and established validation/corruption outcomes retain their semantics. Caller-owned transactions (including ambient/enlisted transactions) are unsupported and refused before tracking or database mutation; the caller must retry its whole unit, and the store never replays inside or outside that caller transaction.
- **FR-004** Ordinary ETL runtime registration and module migration discovery select SQLite, PostgreSQL, SQL Server or MySQL through the existing provider choice. Unsupported providers still fail clearly; the persistence package remains free of concrete engine dependencies.
- **FR-005** Real-server races and a bounded correctness workload qualify concurrent transitions on all three server engines. Existing complete runtime/store contracts and SQLite regressions remain valid.
- **FR-006** An actual Workbench journey with both ETL stores on each engine proves publish, native invocation, expected output, completion and retained readback after process restart. Evidence identifies application source/products separately from harness changes.
- **FR-007** Existing SQLite persisted state, key encoding, migration history, authored artifacts and frozen fingerprints are preserved. Choosing a different engine does not automatically migrate a database between engines.
- **FR-008** Every added guard is demonstrated by a red/green control or explicitly named as unproven. Local proof is distinguished from exact PR-head CI, review and resulting-main acceptance.

### Key Entities

Existing ETL invocation, preparation lease, partition lease/fence, owed batch, accepted counters and terminal outbox retain their meanings and wire formats. No new execution model or record-level workflow is introduced.

## Success Criteria *(mandatory)*

- **SC-001** All three configured server hosts complete and retain the same pinned two-partition journey after restart, without direct-store activation shortcuts.
- **SC-002** Real-server contention tests preserve exclusive authority, exact bounded accounting and one authoritative terminal result on each engine.
- **SC-003** Complete affected suites execute every discovered method; failures, skips, intentional negative controls and pending checks are reported separately.
- **SC-004** SQLite migration/snapshot and frozen authoring/artifact inputs are unchanged; its existing compatibility tests pass.

## Assumptions

- Spec 1203 / #3803 supplies the schemas; this child merges after its parent. No branch protection, required check or human hold is bypassed.
- Use the existing retry policy and provider binding. A small ETL-owned exception is justified by failure at the now-enabled runtime boundary; design-store exception cleanup remains #3758.
- No new connector, transform chain, execution abstraction, distributed engine or cross-engine migration is added. Mid-work crash/recovery and full connector acceptance follow in the already ordered #2908 slices.
- Container credentials are generated per run; proof uses owned processes, databases and ephemeral loopback ports, never 5095 or 4179.
