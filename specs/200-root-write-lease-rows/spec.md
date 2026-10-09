# Feature Specification: Root-Write Lease Coordination Without a Hot Row

**Feature Branch**: `runtime-throughput/root-write-lease-rows`

**Created**: 2026-10-09

**Status**: Draft

**Input**: User description: "Root-write lease coordination without a hot row (#2538, Program #2531). Concurrent executions of one published workflow each take and release a root-write lease on the same workflow executable artifact for every checkpoint commit. Today all leases of an artifact live in one coordination row (serialized dictionary + deletion guard) updated by optimistic concurrency with 16 no-backoff attempts; at 16-32 concurrent executions holders exhaust retries and checkpoint commits fault. Goal: store one row per lease so a holder's acquire/renew/release touches only its own row, while preserving deletion-guard mutual exclusion with live leases (GC safety), incarnation fencing, same-lease-id shared-token semantics (#2274), expiry semantics, in-memory parity and four-provider support. Also decide whether a commit needs a lease only when it creates or changes an execution root, and stop a lease-release failure from reporting an already-durable checkpoint as failed."

## Context

Track A1 of [Program #2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531) ran the reference HTTP workflow (`Sequence / HttpEndpoint / SetVariable / WriteHttpResponse`) under 16 and 32 concurrent clients. Only 1 of 8 cases passed ([#2532](https://github.com/elsa-workflows/elsa-foundation/issues/2532)). The failures were HTTP 202/500 responses, timeouts, instances left Running or Faulted, and poisoned checkpoints. Their incident stacks run through root-write lease acquire and release on the shared workflow artifact ([#2538](https://github.com/elsa-workflows/elsa-foundation/issues/2538)). The source-level cause is in the [#2538 analysis](https://github.com/elsa-workflows/elsa-foundation/issues/2538#issuecomment-6078886014):

- every lease of an artifact is stored in one shared record;
- every checkpoint commit of every execution writes that record at least twice;
- after 16 lost races the write gives up.

The owner selected the structural direction on 9 October 2026: one record per lease, rather than retry tuning alone.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Many executions of one workflow run concurrently and correctly (Priority: P1)

A workflow author publishes an HTTP-triggered workflow, and many clients call it at the same time. Every call must get its configured response, and every execution must reach its correct durable terminal state. That holds however many other executions of the same published workflow are in flight.

**Why this priority**: This is a correctness defect in the most common production shape: one published workflow serving concurrent requests. It also caps throughput for the whole Runtime Throughput program, since Track B cannot measure anything meaningful while executions fault.

**Independent Test**: Run `e2e-tests/http/Test-RuntimeConcurrencyCorrectness.ps1` at 16 and 32 clients, under Immediate and Coalesced cadence, against a default SQLite host and against a PostgreSQL-runtime host. All eight cases must pass.

**Acceptance Scenarios**:

1. **Given** a published HTTP workflow and a fresh host, **When** 32 clients each send 4 concurrent requests, **Then** every response is HTTP 200 with the expected body and every execution is Completed with zero incidents.
2. **Given** the same load under Immediate cadence, which writes more checkpoints per execution, **When** the run finishes, **Then** the result is identical to Coalesced cadence: all responses correct, all executions Completed, zero incidents.
3. **Given** any number of concurrent executions of one workflow, **When** one execution takes, renews or releases its lease, **Then** that operation never fails because other executions took, renewed or released their own leases.

---

### User Story 2 - Unused workflow versions are still collected safely (Priority: P1)

An operator relies on reference garbage collection to remove executable artifacts that no published version or running execution uses. Collection must never remove an artifact while any writer holds a live lease on it. A writer must never be granted a lease while collection is deleting that artifact.

**Why this priority**: Changing how leases are stored must not weaken the safety property that leases exist to provide. Losing it would delete artifacts that running executions still need.

**Independent Test**: The existing lease/guard and garbage-collection test suites pass unchanged, together with new interleaving tests for the race between granting a lease and beginning deletion. Run them in memory and on every supported provider.

**Acceptance Scenarios**:

1. **Given** a live lease on an artifact, **When** collection tries to begin deleting it, **Then** deletion is refused.
2. **Given** collection holds a live deletion guard on an artifact, **When** a writer tries to take a lease, **Then** the lease is refused.
3. **Given** a lease grant and a deletion start racing on the same artifact in either order, **When** both complete, **Then** at most one has succeeded. The writer never holds a live lease while deletion of that artifact proceeds.
4. **Given** an artifact is deleted and later recreated under the same identity, **When** a holder from before the deletion acts on its old lease, **Then** the old lease is not honored for the new artifact (incarnation fencing).

---

### User Story 3 - A durable checkpoint is never reported as failed because of lease cleanup (Priority: P2)

When a checkpoint commit has durably succeeded, a later failure to release its lease must not turn that success into a reported failure. A reported failure would fault the activity, poison the checkpoint or lose the HTTP response even though the state was saved.

**Why this priority**: A1 shows release failures surfacing as `ActivityConstructionFailed` and poisoned completion checkpoints after the commit itself had succeeded. After User Story 1, release failures should be rare. When they do happen, they must stay cleanup problems and must not cause correctness failures.

**Independent Test**: Inject a release failure after a successful durable checkpoint commit. The commit is reported as successful. The lease becomes inert when it expires. The failure is observable to operators.

**Acceptance Scenarios**:

1. **Given** a checkpoint commit that succeeded durably, **When** releasing its lease fails, **Then** the commit is reported as successful, an operator-visible diagnostic is recorded, and the lease no longer blocks collection once it expires.
2. **Given** a checkpoint commit that failed, **When** its lease is released or fails to release, **Then** the commit failure is reported unchanged.

---

### Edge Cases

- **Same lease id from two holders:** two holders acquire the same lease id at once. The store keeps today's shared-token behavior (#2274). Checkpoint commits stop hitting this case because each attempt now gets its own lease id (FR-015).
- **Crashed holders:** a holder dies without releasing. Its lease stops blocking collection once it expires, and expired lease records are eventually removed without needing a shared record.
- **Expiry boundary:** a lease is renewed exactly at its expiry instant. Behavior matches today's comparison semantics, where `expiresAt <= now` means expired.
- **Artifact missing or orphaned:** a lease request for an artifact that does not exist, or whose identity records are inconsistent, keeps today's outcomes (refusal or fault).
- **Leases from before the upgrade:** leases recorded in the old shared record before the upgrade are still honored by deletion checks until they expire.
- **Release after deletion:** releasing a lease whose artifact has meanwhile been deleted completes as a no-op, as it does today.
- **Unguarded deletion:** the existing unguarded delete removes all lease state for the artifact, as it does today.
- **Clock skew:** writers and collection on different nodes use the same time-source contract as today. The change introduces no new skew assumption.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Taking, renewing or releasing a root-write lease MUST NOT fail, and MUST NOT need a retry, because other holders took, renewed or released *different* leases on the same artifact.
- **FR-002**: A lease operation's work MUST be bounded by its own lease. It MUST NOT grow with the number of other live leases on the same artifact.
- **FR-003**: A deletion guard MUST NOT be granted while any unexpired lease exists on the artifact, and a lease MUST NOT be granted while an unexpired deletion guard exists. This MUST hold when lease grants and deletion starts race, on every supported provider and in memory.
- **FR-004**: Guarded deletion MUST re-verify, atomically with the delete, that the guard is current and that no unexpired lease exists. A lease committed concurrently with the delete MUST either block it or be invalidated together with the deleted artifact.
- **FR-005**: Leases MUST be fenced to the artifact incarnation they were granted for. Deleting an artifact MUST remove or invalidate all of its lease records.
- **FR-006**: Re-acquiring a live lease with the same id MUST return the existing token, and renewing or releasing MUST require the matching token. This preserves today's contract (#2274).
- **FR-007**: Expired leases MUST be treated as absent by every check, and expired lease records MUST be removable without contending with live holders.
- **FR-008**: The in-memory store MUST keep behavioral parity with the persistent stores for every requirement above.
- **FR-009**: The change MUST work on SQLite, PostgreSQL, SQL Server and MySQL. Schema changes MUST be additive (expand-only) with provider migrations.
- **FR-010**: Leases recorded in the pre-upgrade shared record MUST continue to block deletion until they expire. No operator data migration may be required.
- **FR-011**: A checkpoint commit that succeeded durably MUST be reported as successful even if releasing its lease fails afterwards. The release failure MUST be observable to operators.
- **FR-012**: *(Moved out of this unit by owner decision, 2026-10-09.)* Skipping the lease for commits that do not change an execution's pinned artifact is a separate follow-up unit, sized against Track B evidence.
- **FR-015**: Each checkpoint commit attempt MUST hold its own root-write lease identity. If two attempts commit the same `CommitId` and overlap, releasing one attempt's lease MUST NOT remove the other's protection, and the second attempt's renewal MUST keep succeeding ([#2286](https://github.com/elsa-workflows/elsa-foundation/issues/2286)). Activation (#2274) and test-run leases keep their existing identity rules.
- **FR-013**: Existing lease, guard, deletion and garbage-collection tests MUST pass unchanged, except where a test asserts the old storage layout itself. Any such test MUST be listed and justified in the plan.
- **FR-014**: The regression tests added with this specification MUST pass on every supported provider. Each MUST fail when the implementation is reverted:
  - `Root_write_lease_acquire_is_not_failed_by_other_holders_of_the_same_artifact`
  - `Root_write_lease_renew_is_not_failed_by_other_holders_of_the_same_artifact`
  - `Root_write_lease_release_is_not_failed_by_other_holders_of_the_same_artifact`
  - `PostgreSql_concurrent_root_write_lease_holders_of_one_artifact_all_succeed`

### Key Entities

- **Workflow executable artifact**: an immutable published executable, identified by scope and artifact id, with an incarnation that changes whenever it is recreated.
- **Root-write lease**: a time-bounded claim by one writer that an artifact closure must not be collected while that writer creates or changes a durable root. It has a lease id, a token and an expiry, and is fenced to an artifact incarnation.
- **Deletion guard**: a time-bounded claim by collection that an artifact is being deleted. It is mutually exclusive with live leases.
- **Execution root**: durable execution state, or a pinned dispatch, that references an artifact and protects it from collection while it exists.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The A1 concurrency matrix passes all 8 cases: SQLite and PostgreSQL runtime, Immediate and Coalesced cadence, 16 and 32 clients. Every response correct, every execution Completed, zero incidents. Today 1 of 8 passes.
- **SC-002**: Under sustained contention from other holders of the same artifact, 100% of lease operations succeed on their first store write. Today the deterministic regression tests exhaust all 16 attempts.
- **SC-003**: 32 real parallel holders × 5 lease cycles on one artifact complete with zero failed cycles on PostgreSQL.
- **SC-004**: Zero regressions in existing lease, guard, deletion and garbage-collection tests across in-memory, SQLite and the three container providers.

## Scope

**In scope:**
- lease storage and operations;
- the deletion guard interplay;
- incarnation fencing;
- migrations for the four providers;
- in-memory parity;
- isolating commit results from release failures;
- per-attempt checkpoint lease identity ([#2286](https://github.com/elsa-workflows/elsa-foundation/issues/2286)).

**Out of scope:**
- Skipping the lease for commits that do not change an execution root. Owner decision of 9 October 2026: this becomes the follow-up unit after this one, informed by Track B.
- [#2537](https://github.com/elsa-workflows/elsa-foundation/issues/2537): incident projection for executions poisoned before their first state row. A separate unit.
- The cause of the HTTP timeouts in [#2536](https://github.com/elsa-workflows/elsa-foundation/issues/2536) that are not explained by lease faults. This unit re-runs A1 to establish what remains.
- Retry tuning as a substitute for the structural change.
- Track B throughput measurement.
- Any performance gate (ADR 0073 D7).

## Owner Decisions (9 October 2026)

1. **Leases written before the upgrade** stay honoured until they expire. Deletion checks read both the old shared record and the new per-lease records, and new leases are written only as per-lease records (FR-010). No data migration and no host drain are needed.
2. **The lease skip for non-root-changing commits** is split into a later unit, so this unit only does the per-lease storage fix.
3. **Mutual exclusion mechanism: write-then-check.** Each side commits its own record, then checks the other side's. This is portable across all four providers, uses no provider-specific locking SQL, and reintroduces no shared hot record. The plan must show why a lease grant and a deletion start can never both succeed under each provider's isolation level.
4. **Adjacent defects:** #2286 (per-attempt checkpoint lease identity) is **in scope** (FR-015). #2537 (incident projection for executions poisoned at their first checkpoint) stays a separate unit.

## Assumptions

- The owner chose the per-lease-record direction on 9 October 2026. Retry backoff MAY be added to the remaining guard operations, but MUST NOT be the fix for FR-001.
- The pre-GA clean-break policy (ADR 0073 D5) applies to data conversion. FR-010 exists only so that leases live during an upgrade are not silently dropped. Leases last minutes, so honoring legacy leases until they expire is enough; no backfill is needed.
- Lease duration, renewal cadence, garbage-collection grace and the guard timeout keep their current defaults.
- The deterministic regression tests can only be run in CI for now: the authoring environment cannot restore the repository's preview packages. Their first CI run on this branch is the reproduction evidence.
