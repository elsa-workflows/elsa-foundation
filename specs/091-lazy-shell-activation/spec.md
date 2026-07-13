# Feature Specification: Observable Shell Readiness and Cold Activation

**Feature Branch**: `codex/624-shell-readiness`

**Created**: 2026-07-12

**Status**: Approved for implementation

**Input**: GitHub issue #624: measure and reduce lazy shell activation latency, distinguish process liveness from workflow-endpoint readiness, attribute activation time to its major phases, and preserve shell isolation and warm runtime behavior.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Trustworthy Workflow Readiness (Priority: P1)

As an operator, I can distinguish a live server process from a default shell that is ready to serve workflow endpoints, so traffic is never sent to a socket whose workflow routes are still being initialized.

**Why this priority**: Honest readiness prevents avoidable first-request failures and multi-second request stalls during startup.

**Independent Test**: Start the server with default-shell activation held at route initialization. Verify liveness succeeds immediately, readiness remains unavailable without triggering another activation, and readiness succeeds only after route initialization and shell activation complete.

**Acceptance Scenarios**:

1. **Given** the server is listening but the default shell is not active, **When** an operator checks liveness and readiness, **Then** liveness succeeds and readiness reports unavailable without blocking for activation.
2. **Given** default-shell activation and route initialization complete successfully, **When** readiness is checked, **Then** readiness succeeds and a published workflow endpoint can be invoked.
3. **Given** default-shell activation fails, **When** readiness is checked, **Then** readiness remains unavailable, liveness stays healthy, and the failure is observable without exposing sensitive details.
4. **Given** another shell remains cold, **When** default-shell readiness succeeds, **Then** the other shell is not activated and shell isolation is preserved.

---

### User Story 2 - Reproducible Cold-Start Evidence (Priority: P2)

As a performance engineer, I can run a repeatable clean-boot measurement that separates process listening, shell readiness, and the first successful workflow response and summarizes multiple runs with p50 and p95 values.

**Why this priority**: Comparable raw evidence is required to choose and verify optimizations safely.

**Independent Test**: Run the cold-start command against a frozen data baseline for at least five boots and verify every raw sample and aggregate percentile is reported, including validation of the workflow response.

**Acceptance Scenarios**:

1. **Given** a prebuilt server and frozen data baseline, **When** the measurement command runs multiple cold boots, **Then** it reports per-boot and aggregate listening, activation, ready, first-request, and first-success timings.
2. **Given** the readiness response, workflow status, or workflow body is incorrect, **When** a boot is measured, **Then** the command fails that run and retains actionable diagnostics.
3. **Given** equivalent before and after builds, **When** both are measured against the same baseline, **Then** the reports contain enough provenance to make the comparison reproducible.

---

### User Story 3 - Actionable Activation Telemetry (Priority: P3)

As an operator or maintainer, I can see how much startup time is spent in discovery/composition, provider initialization and migrations, reconciliation and startup tasks, and HTTP route initialization, so the largest recurring cost can be addressed rather than guessed.

**Why this priority**: Phase attribution turns a long opaque wait into concrete performance work and prevents optimization of insignificant phases.

**Independent Test**: Activate a shell with representative startup tasks and verify duration and outcome telemetry is emitted for the overall activation and each required phase, including failures.

**Acceptance Scenarios**:

1. **Given** a successful cold activation, **When** telemetry is inspected, **Then** it includes the overall activation plus the required major phase categories and relevant counts.
2. **Given** an activation task fails, **When** telemetry is inspected, **Then** the failed phase and task are identifiable and readiness remains unavailable.
3. **Given** repeated readiness probes during activation, **When** telemetry is inspected, **Then** one activation attempt is represented rather than a probe-driven stampede.

---

### User Story 4 - Faster First Workflow Availability (Priority: P4)

As a workflow client, I receive a fast response after the server declares itself ready, without paying the full default-shell activation cost on my first request.

**Why this priority**: The client-visible symptom is the first workflow request absorbing server initialization work.

**Independent Test**: Compare repeated clean boots before and after the change using the same build mode, configuration, data snapshot, and machine; verify the readiness and first-success budgets while the warm-request budget and lifecycle suites remain green.

**Acceptance Scenarios**:

1. **Given** the reference server starts, **When** no client request has yet reached a shell route, **Then** default-shell preparation begins once and readiness observes its result.
2. **Given** readiness succeeds, **When** the first workflow request arrives, **Then** it does not perform default-shell discovery, composition, migrations, startup tasks, or route initialization.
3. **Given** the measured dominant activation phase, **When** the selected optimization is enabled, **Then** repeated cold-boot evidence shows a material reduction without changing warm workflow behavior.

### Edge Cases

- The default shell does not exist, is misconfigured, or fails activation.
- The process receives shutdown while activation is still in progress.
- Many readiness probes arrive concurrently during activation.
- A shell reload creates a new generation while an older active generation is still serving.
- The initialized route table is valid but contains no published routes.
- A cold-boot run times out, the configured port is occupied, or the child process exits early.
- The frozen data baseline is incompatible with the measured build.
- Telemetry export is disabled; activation must remain functionally correct with negligible diagnostic overhead.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The server MUST expose distinct liveness and readiness signals that do not require authentication and do not route through or trigger shell resolution.
- **FR-002**: Liveness MUST report process availability independently of default-shell state.
- **FR-003**: Readiness MUST return immediately and report unavailable until the current default shell generation is active and its workflow HTTP route initialization has completed successfully.
- **FR-004**: The reference server MUST begin one stampede-safe default-shell preparation attempt after the process starts, without requiring a workflow client request.
- **FR-005**: A failed or cancelled preparation attempt MUST leave readiness unavailable, preserve process liveness where possible, and expose a stable non-sensitive diagnostic state.
- **FR-006**: Default-shell readiness MUST NOT activate, inspect, or share mutable readiness state with any other shell.
- **FR-007**: A repeatable command MUST measure multiple isolated clean boots and report raw samples plus p50 and p95 for process listening, shell activation, shell-ready absolute time, first workflow request, and first successful workflow response.
- **FR-008**: The measurement command MUST validate expected workflow status and body, capture build/configuration/data provenance, enforce optional budgets, and retain boot diagnostics on failure.
- **FR-009**: Activation telemetry MUST report duration and outcome for overall default-shell preparation, discovery/composition, provider initialization and migrations, reconciliation/startup tasks, and HTTP route-table initialization.
- **FR-010**: Startup-task telemetry MUST identify the task type and outcome while avoiding sensitive configuration or payload values and avoiding unbounded metric dimensions.
- **FR-011**: The implementation MUST optimize at least one phase shown by repeated baseline measurements to be material and MUST publish comparable before/after evidence.
- **FR-012**: Existing shell activation, reload, lifecycle, isolation, failure, and workflow HTTP behavior MUST remain valid.
- **FR-013**: Warm workflow response latency MUST remain within the existing reference budget and MUST NOT depend on readiness telemetry being enabled.
- **FR-014**: Operators MUST be able to configure preparation behavior and performance budgets, and rollback MUST require configuration only or removal of the optional host composition.

### Key Entities

- **Readiness snapshot**: The current default-shell preparation state, generation identity where available, start/completion timestamps, outcome category, and a stable diagnostic code.
- **Activation phase observation**: One bounded timing observation for a named activation phase or startup task, correlated to one shell preparation attempt without payload data.
- **Cold-boot sample**: One process run containing listening, activation, readiness, first-request, first-success timings, validation outcome, and environment provenance.
- **Cold-start report**: Raw samples and aggregate percentiles for one immutable build/configuration/data lane.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across 20 representative cold boots, readiness never succeeds before the default shell can serve its initialized workflow HTTP surface, and liveness remains independently available during preparation.
- **SC-002**: The reference server's shell-ready p95 improves by at least 30% from the recorded baseline and satisfies an explicit p95 budget of 30 seconds on the reference machine and frozen baseline.
- **SC-003**: Once readiness succeeds, the first published workflow response completes within 750 ms at p95 on the reference machine, while subsequent warm workflow requests remain within the existing 50 ms p95 budget.
- **SC-004**: Every measured cold boot reports all five required milestones; every default-shell preparation attempt reports overall duration and every top-level phase it reaches, while each reached owned provider, startup-task, and route operation reports its dedicated bounded observation. Phases not reached because an earlier phase failed are not assigned synthetic timings.
- **SC-005**: Concurrent readiness polling produces exactly one default-shell preparation attempt and no activation-related request waits.
- **SC-006**: Existing targeted shell lifecycle/isolation, workflow HTTP, runtime, persistence recovery, and architecture suites pass with no removed coverage.

## Assumptions

- The reference performance lane uses a prebuilt optimized server binary, loopback HTTP, a fixed frozen database baseline, and a fresh mutable copy per boot.
- Operating-system filesystem caches are not forcibly flushed; before/after lanes use repeated interleaved runs to reduce temperature bias.
- Empty but successfully initialized route tables are ready; readiness does not execute a user workflow or require one to exist.
- The existing shell registry provides stampede-safe activation for one shell name and remains the authority for active generations.
- Exact internal feature-composition timing may remain coarse while the shell framework is an external dependency; owned provider, task, reconciliation, and route phases must still be measured honestly.
- This work remains `none/free-flow` in the program-goal registry because it completes the explicitly tracked performance follow-up rather than creating a new architecture program.
