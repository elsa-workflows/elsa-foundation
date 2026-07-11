# Feature Specification: Runtime HTTP Hot-Path Performance

**Feature Branch**: `597-runtime-http-performance`

**Created**: 2026-07-11

**Status**: Draft

**Input**: User description: "Make synchronous HTTP workflows execute in milliseconds instead of seconds, deliver the improvement end to end, and provide clear runtime performance controls."

## Context

A published synchronous HTTP workflow containing only an HTTP endpoint followed by a response activity currently spends multiple seconds on its first executions and hundreds of milliseconds after warm-up. The workflow itself performs no expensive work. Runtime evidence shows that the request waits for the complete durable scheduler drain and that the two-activity workflow produces thirteen physical checkpoint commits plus associated continuation records. The runtime already supports a crash-safe policy that folds replayable checkpoints within one non-suspending execution segment, but normal server composition cannot select or configure it.

This work makes the safe lower-write policy operable in a real server, validates it against the actual HTTP and durable-persistence path, and prevents the latency and write-amplification regression from returning. It does not weaken mandatory durability boundaries or silently change synchronous HTTP response semantics.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fast synchronous HTTP response (Priority: P1)

A workflow author publishes a synchronous HTTP endpoint that immediately writes a response. A caller receives that response promptly and consistently rather than waiting through avoidable durable commits.

**Why this priority**: This is the reported user problem and the minimum valuable outcome. Configuration controls without a proven end-to-end improvement would not solve it.

**Independent Test**: Publish the two-activity hello-world workflow in the reference server, warm the process, issue a representative request sample, and verify both the authored response and the latency/commit-count budgets.

**Acceptance Scenarios**:

1. **Given** a warmed reference server using durable local persistence and a published synchronous hello-world workflow, **When** a representative sample of requests is executed serially, **Then** at least 95% complete within 50 milliseconds and every response contains the authored status, headers, and body.
2. **Given** the same workflow under the optimized policy, **When** one execution reaches completion without suspending, **Then** its replayable intra-segment checkpoints are folded and the number of physical checkpoint commits is no greater than the mandatory durability boundaries require.
3. **Given** the optimized execution path, **When** the workflow completes, **Then** its terminal state, activity inspection, durable response artifact, and continuation state are equivalent to the immediate-persistence reference result.

---

### User Story 2 - Explicit durability and replay controls (Priority: P2)

A server operator can select either immediate checkpoint persistence or checkpoint coalescing and can bound how much work may be replayed after a crash. The selected behavior is visible in server configuration and requires no custom host code.

**Why this priority**: The performance gain deliberately widens the crash-replay window. Operators need an explicit, documented and reversible policy instead of a hidden code-level choice.

**Independent Test**: Compose two otherwise-identical server shells from configuration, one immediate and one coalesced with a custom segment cap, and verify that each resolves and executes with the selected policy.

**Acceptance Scenarios**:

1. **Given** an operator selects immediate persistence, **When** a workflow runs, **Then** every checkpoint selected by the existing policy is persisted immediately and current behavior is preserved.
2. **Given** an operator selects coalesced persistence with a positive segment cap, **When** a non-suspending workflow segment runs, **Then** replayable checkpoints are folded up to that cap and the segment flushes at quiescence or an earlier mandatory boundary.
3. **Given** an invalid policy name or non-positive segment cap, **When** the shell starts, **Then** startup fails with a clear configuration error before requests are accepted.
4. **Given** the reference development server, **When** it starts with its committed shell configuration, **Then** it uses the validated low-latency policy while retaining an immediate-policy rollback configuration.

---

### User Story 3 - Reproducible performance and safety evidence (Priority: P3)

A maintainer can reproduce the before-and-after result, attribute physical checkpoint writes, and verify recovery behavior without relying on an anecdotal stopwatch measurement.

**Why this priority**: Performance changes are not complete unless their gain is measurable and their durability trade-off is guarded against regression.

**Independent Test**: Run the documented validation command to produce immediate-versus-coalesced measurements, then run deterministic commit-count and crash-recovery tests in the normal test suite.

**Acceptance Scenarios**:

1. **Given** an unchanged machine and workflow fixture, **When** the performance validation is run for both policies, **Then** it reports cold and warm latency, response correctness, physical commit count, and improvement ratio with environment metadata.
2. **Given** a crash at any point before the coalesced flush, **When** the server restarts and recovery runs, **Then** the durable queue replays from the last flushed state and converges to the immediate-policy terminal result without losing continuation work.
3. **Given** a configured segment cap, **When** a workflow exceeds the cap, **Then** the replay window never exceeds the configured number of checkpoints and an intermediate durable flush occurs.

### Edge Cases

- The first request after process start is measured separately from warmed requests; cold-start cost must not be hidden in a warm average.
- Concurrent executions may contend for the same persistence provider; one execution must not corrupt or reorder another execution's state.
- A workflow that suspends, creates a bookmark, faults, is cancelled, or completes must cross its existing mandatory durable boundary even when coalescing is selected.
- A mid-segment crash may re-execute activities according to the documented at-least-once contract, but external continuation work must not be delivered before the folded commit is durable.
- A distributed or non-local synchronous execution continues to use the existing accepted-response degradation behavior; this work does not add cross-process live HTTP response transport.
- If the measured optimized path does not meet the latency budget, further changes must target the dominant measured span without weakening the durability requirements in this specification.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The runtime MUST provide an operator-selectable checkpoint persistence policy with at least immediate and coalesced modes.
- **FR-002**: The selected checkpoint persistence policy MUST be configurable through the normal shell configuration surface without custom host code.
- **FR-003**: Coalesced mode MUST expose a positive maximum-segment-checkpoint setting that bounds both in-memory buffering and crash replay.
- **FR-004**: The default value for the maximum coalesced segment MUST remain 50 unless benchmark and recovery evidence justifies a different committed default.
- **FR-005**: Immediate mode MUST preserve the existing checkpoint, queue, outbox, and terminal-state behavior.
- **FR-006**: Coalesced mode MUST fold replayable state changes within a non-suspending drain segment and persist the folded result atomically at quiescence, at the configured cap, or at a mandatory boundary.
- **FR-007**: Mandatory suspension, bookmark, completion, fault, incident, and cancellation boundaries MUST remain durable before their effects become externally observable or resumable.
- **FR-008**: The durable scheduler queue MUST never advance past the last successfully flushed checkpoint state.
- **FR-009**: Ownership fencing MUST gate the folded durable commit exactly as it gates immediate commits; a stale execution owner MUST NOT commit.
- **FR-010**: Post-commit continuation work MUST remain atomic with the state that produced it and MUST NOT be delivered before the corresponding state commit is durable.
- **FR-011**: The reference development server MUST enable the validated low-latency policy in committed shell configuration and document how to restore immediate mode.
- **FR-012**: The feature MUST provide a real synchronous HTTP workflow validation covering the authored response, durable terminal state, response artifact, and physical commit count.
- **FR-013**: The feature MUST provide reproducible before-and-after performance evidence that separates cold and warm execution and records the tested policy, segment cap, provider, runtime version, and request sample size.
- **FR-014**: Automated regression coverage MUST use deterministic structural assertions for commit folding and recovery; wall-clock latency thresholds MUST be kept out of ordinary unit-test execution unless the environment is explicitly controlled.
- **FR-015**: If the first optimized implementation misses the warm latency target, the work unit MUST continue with measured hot-path optimization until the target is met or a documented platform limit is demonstrated with evidence.
- **FR-016**: Request timeout, request size, drain-cycle, and work-item limits MUST retain their reliability meanings and MUST NOT be presented as latency optimizers.
- **FR-017**: Synchronous HTTP response semantics MUST remain unchanged: a local workflow-authored response is returned in the same exchange, while existing suspension and non-local degradation behavior remains intact.

### Key Entities

- **Checkpoint persistence policy**: The operator-selected rule that chooses immediate persistence or folds replayable checkpoints within a bounded execution segment.
- **Coalesced segment**: The ordered set of replayable checkpoint changes buffered between the last durable state and the next mandatory, cap, or quiescence flush boundary.
- **Performance evidence run**: A reproducible record of environment, workload, policy, provider, cold/warm latency distribution, response correctness, and physical commit count.
- **Physical checkpoint commit**: One atomic durable application of runtime state, scheduler changes, inspection projections, and post-commit continuation records.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the reference development machine and durable local provider, at least 95% of warmed synchronous hello-world requests complete within 50 milliseconds, with a stretch target of 25 milliseconds.
- **SC-002**: The optimized synchronous hello-world path reduces physical checkpoint commits by at least 75% compared with the current thirteen-commit baseline, without omitting any mandatory boundary.
- **SC-003**: Immediate and coalesced executions produce equivalent terminal workflow state, activity evidence, response artifacts, and continuation outcomes across the acceptance suite.
- **SC-004**: Every injected crash window converges after restart with no lost continuation work, no stale-owner commit, and replay bounded by the configured segment cap.
- **SC-005**: A maintainer can switch between immediate and coalesced behavior entirely through shell configuration and verify the active choice through automated composition tests.
- **SC-006**: The documented before-and-after validation can be reproduced in one command and reports enough environment metadata to compare later regressions honestly.

## Assumptions

- The existing coalescing policy and its mandatory-boundary set are the starting implementation; this unit productionizes and validates them rather than inventing a second folding mechanism.
- The reference performance budget applies to a warmed, local, single-node development server. Cold start, distributed dispatch, and remote database latency are reported separately.
- The local durable provider remains the reference development configuration. Provider-specific tuning is included only when measurements show that coalescing alone cannot meet the target.
- In-memory persistence may establish an execution-engine ceiling but cannot prove durable-provider performance or crash safety.
- At-least-once activity re-execution within a crashed coalesced segment remains part of the documented durability contract.
- The result belongs to the `runtime-execution-seam` program-goal bucket; provider-specific findings link to Groundwork persistence documentation rather than reopening the completed Groundwork roadmap.
