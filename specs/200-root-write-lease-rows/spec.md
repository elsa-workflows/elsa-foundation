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

### User Story 4 - Commits that do not change a root skip the lease (Priority: P3)

A checkpoint commit only needs artifact protection while it creates or changes the durable record that roots an execution to its artifact. Examples are the first commit of an execution and an alteration that re-pins it. Ordinary later commits are already protected by the execution's existing root. When that is proven, they should not take a lease at all.

**Why this priority**: This removes most coordination writes per execution, which is a throughput gain for Track B. It is a separate safety argument from User Story 1 and must not delay it.

**Independent Test**: A safety proof shows that collection's final re-check protects an artifact pinned by an existing execution root, followed by interleaving tests. Then show that a non-root-changing commit performs no lease operation, and that collection still never removes an artifact any execution is pinned to.

**Acceptance Scenarios**:

1. **Given** an execution whose durable root already pins its artifact, **When** a later checkpoint commit does not change that pin, **Then** the commit takes no lease, and collection cannot remove the artifact while the execution's root exists.
2. **Given** the first commit of an execution, or a commit that changes its pinned artifact, **When** it commits, **Then** it takes a lease on the newly pinned artifact closure, exactly as before.
3. **Given** the safety proof does not hold for some commit kind, **When** that kind commits, **Then** it keeps taking a lease. Correctness wins over the optimization.

---

### Edge Cases

- **Same lease id from two holders:** two holders acquire the same lease id at once, for example overlapping retries of one commit. Today's shared-token behavior from #2274 is preserved; #2286 owns the separate per-attempt-identity defect.
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
- **FR-012**: A checkpoint commit that does not create or change an execution's pinned artifact SHOULD take no lease. It MUST do so only where the plan's safety argument and tests prove that collection's final re-check protects the existing root.
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
- **SC-005**: If User Story 4 is delivered, a non-root-changing checkpoint commit performs zero lease writes, measured by command accounting. No timing measurement is involved.

## Scope

**In scope:**
- lease storage and operations;
- the deletion guard interplay;
- incarnation fencing;
- migrations for the four providers;
- in-memory parity;
- isolating commit results from release failures;
- the evidence-gated root-change lease optimization.

**Out of scope:**
- [#2537](https://github.com/elsa-workflows/elsa-foundation/issues/2537): incident projection for executions poisoned before their first state row. A separate unit.
- [#2286](https://github.com/elsa-workflows/elsa-foundation/issues/2286): per-attempt checkpoint lease identity.
- The cause of the HTTP timeouts in [#2536](https://github.com/elsa-workflows/elsa-foundation/issues/2536) that are not explained by lease faults. This unit re-runs A1 to establish what remains.
- Retry tuning as a substitute for the structural change.
- Track B throughput measurement.
- Any performance gate (ADR 0073 D7).

## Assumptions

- The owner chose the per-lease-record direction on 9 October 2026. Retry backoff MAY be added to the remaining guard operations, but MUST NOT be the fix for FR-001.
- The pre-GA clean-break policy (ADR 0073 D5) applies to data conversion. FR-010 exists only so that leases live during an upgrade are not silently dropped. Leases last minutes, so honoring legacy leases until they expire is enough; no backfill is needed.
- Lease duration, renewal cadence, garbage-collection grace and the guard timeout keep their current defaults.
- Race-freedom between lease grants and deletion starts can be achieved without a shared hot record. One approach: each side commits its own record, then checks the other's. The plan must choose and prove a provider-correct mechanism, and must document why it is free of write skew under each provider's isolation level.
- The deterministic regression tests can only be run in CI for now: the authoring environment cannot restore the repository's preview packages. Their first CI run on this branch is the reproduction evidence.
