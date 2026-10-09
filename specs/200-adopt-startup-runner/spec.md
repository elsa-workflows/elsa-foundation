# Feature Specification: Shared Startup Runner Adoption

**Feature Branch**: `2341-adopt-startup-runner`

**Created**: 2026-10-09

**Status**: Draft

**Input**: Deliver the modular-hosting upstream program end to end, adopting general startup activation support in Foundation.Host and Workbench while retaining Elsa policy and existing host behavior.

Program: [Modular Hosting Upstream Delivery](../../docs/program-goals/modular-hosting-upstream-delivery.md). This unit follows the qualified [shared Nuplane observer candidate](../199-shared-nuplane-adoption/spec.md). Branch and spec numbers use their separate configured allocation rules; spec 200 is the next global specification number after 199.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Foundation recovers without restarting (Priority: P1)

An operator starts Foundation.Host with configured shells. Each shell gets a serial initial activation attempt before the server listens. Ordinary failures leave the process running and are retried until activation succeeds or the host stops. An EF refusal retains its operator guidance and slower retry cadence.

**Why this priority**: A readiness-gated host receives no request that could otherwise recover a failed lazy activation.

**Independent Test**: Start with a temporarily unavailable database, observe sanitized not-ready diagnostics, repair the database and verify the same owned process becomes ready without restart.

**Acceptance Scenarios**:

1. **Given** eager activation is absent or unparseable, **When** the host starts, **Then** every configured shell receives its initial attempt in configured order before startup completes; explicit false performs no eager activation.
2. **Given** one initial activation fails ordinarily, **When** later targets are attempted, **Then** their initial attempts continue and background retries begin only after the serial initial pass.
3. **Given** repeated ordinary failures or an EF refusal, **When** retries are scheduled, **Then** the existing exponential cap/jitter or refusal-at-maximum cadence is retained, with no retry-count exhaustion and unchanged sanitized Attention/health information.
4. **Given** a fatal exception during an initial attempt, **When** startup observes it, **Then** that original failure escapes and no later initial target or retry begins.
5. **Given** a fatal exception during a background retry, **When** another target remains recoverable, **Then** only the fatal target stops retrying; the existing joined-shutdown fault behavior and bounded cancellation are retained.

### User Story 2 - Workbench retains its two startup phases (Priority: P2)

A Workbench operator may opt into pre-listen eager activation, while default-shell warmup independently runs after the application has started listening. Both paths retain their own logging, telemetry and outcomes.

**Why this priority**: Sharing execution must preserve the host's intentional startup latency and recovery choices.

**Independent Test**: Gate activation and application-start signals, observe both services and assert ordering, one underlying activation and independent warmup outcomes.

**Acceptance Scenarios**:

1. **Given** eager activation is not explicitly enabled, **When** Workbench starts, **Then** that service performs no activation; opted-in target selection keeps all/named selection, order and deduplication.
2. **Given** an opted-in eager target fails, **When** the pass continues, **Then** later targets are attempted serially before startup completes, failures are logged and no automatic retry is scheduled.
3. **Given** warmup is enabled, **When** StartAsync returns before ApplicationStarted, **Then** activation has not begun; after ApplicationStarted, feature discovery precedes one default-shell activation and the existing phase telemetry and Ready/Failed/Cancelled outcomes remain accurate.
4. **Given** warmup is disabled or cancellation precedes application start, **When** the service completes, **Then** the existing disabled/cancelled state is recorded without activation.
5. **Given** eager activation and warmup target the same shell, **When** both phases execute, **Then** one underlying generation is activated and warmup still records its own outcome and telemetry.
6. **Given** a supported custom registry returns a successfully activated current shell, **When** warmup records success, **Then** its reported generation belongs to that returned shell rather than a later registry read or an invented default.

### User Story 3 - Shutdown owns outstanding activation work (Priority: P2)

An operator stops either host during startup recovery or warmup. New work stops and outstanding activation remains tracked even when a bounded shutdown wait expires.

**Why this priority**: A shared execution primitive must preserve cancellation and ownership across host lifecycle boundaries.

**Independent Test**: Use cancellation-aware and deliberately cancellation-ignoring activations, cancel shutdown waits, then release the owned work and verify completion without additional attempts.

**Acceptance Scenarios**:

1. **Given** a retry deadline or activation is pending, **When** shutdown starts, **Then** no subsequent activation begins and cancellation-aware work joins normally.
2. **Given** activation ignores cancellation, **When** the shutdown wait expires, **Then** that caller receives the existing bounded cancellation outcome while outstanding work remains owned until completion.
3. **Given** multiple background targets fail fatally, **When** the shutdown join completes, **Then** every original failure is retained for diagnostics and the first configured target's original fatal is rethrown. This replaces the legacy scheduler-dependent first exception with a deterministic selection.

### Edge Cases

- No configured targets; mixed successful/failing initial targets; invalid enable flags; changing configuration after target selection.
- Ordinary failure followed by external request/reload activation; an unsettled candidate must not satisfy runner recovery, and a terminal startup result must not become a live reload-readiness certificate.
- Shutdown racing an initial fatal exception, retry fatal failure, observer logging or externally completed activation.
- A successful custom-shell activation with no concrete built-in shell metadata; a concurrent later reload must not replace the recorded successful generation.
- Telemetry/logging observers fail; sensitive exception messages remain confined to authorized diagnostic logs and do not enter public snapshots.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The hosts MUST use the delivered general startup activation runner for serial attempts, retry scheduling and owned run lifetime; they MUST remove superseded local activation/retry loops.
- **FR-002**: Foundation MUST retain its default-on/explicit-opt-out policy, configured target order, pre-listen first pass and independent continuing recovery per failed target.
- **FR-003**: Foundation MUST retain existing capped exponential/jitter retry behavior and maximum-interval EF refusal checks, including operator guidance, sanitized health/Attention projections and diagnostic exception handling.
- **FR-004**: Foundation MUST preserve its fatal-exception boundary separately for initial startup and background retry, including original failure propagation, unaffected-target progress and joined/bounded shutdown behavior.
- **FR-005**: Workbench MUST retain default-off eager activation, target selection, serial pre-listen one-shot execution and continue-on-failure behavior.
- **FR-006**: Workbench MUST retain nonblocking warmup startup, its ApplicationStarted boundary, optional disablement, feature-discovery/activation/overall telemetry and one-shot outcome transitions.
- **FR-007**: Successful warmup MUST identify the generation returned by the successful activation, including supported custom registries, without a later raw-current lookup or invented value.
- **FR-008**: Both hosts MUST cancel and join owned runner work within the existing caller-bounded shutdown contract; expiration of a wait MUST NOT abandon outstanding activation work.
- **FR-009**: Host projections MUST consume runner-owned attempt state without introducing duplicate host retry counters, schedules or polling loops. Elsa refusal classification, public response policy, logging and telemetry remain host-owned.
- **FR-010**: This unit MUST preserve current readiness compatibility behavior. Adoption of optional settled-current observation at the four identified readiness projections is a separate unit pending the owner custom-registry decision; runner terminal history MUST NOT replace current readiness observation.
- **FR-011**: The existing Nuplane host profiles, auto-reload defaults, refusal callbacks, host-versus-shell ownership and configuration contracts MUST remain unchanged.
- **FR-012**: Existing regression assertions MUST remain effective. Verification MUST cover deterministic phase/retry/cancellation/fatal/custom-generation cases, actual rebuilt-host recovery, independent review, affected architecture/maps and valid behavioral mutation/restoration proof.
- **FR-013**: Final adoption MUST consume the verified complete stable upstream package families specified by the program, with coherent pins/locks, clean-cache locked restore and exact resulting-main evidence. Preview qualification MUST be identified separately from stable completion.
- **FR-014**: Foundation's fatal set MUST remain `OutOfMemoryException`, `StackOverflowException`, `AccessViolationException`, `AppDomainUnloadedException` and `BadImageFormatException`. An `OperationCanceledException` without cancellation of the supplied token remains an ordinary retryable activation failure; requested-token cancellation remains cancellation.
- **FR-015**: Workbench MUST retain its different exception boundary: supplied-token cancellation propagates from eager activation or records cancelled warmup, while other activation exceptions retain eager log-and-continue or failed warmup behavior. Foundation's fatal set MUST NOT be applied globally to Workbench.
- **FR-016**: Foundation MUST detach the caller's startup cancellation token before successful `StartAsync` returns. Cancellation while that call remains pending MAY abort the owned run, including its initial-pass handoff; later cancellation MUST NOT end background retries. This explicitly strengthens the legacy handoff boundary without extending the caller token into the host lifetime.
- **FR-017**: After joining owned work, Foundation MUST select concurrent background fatal failures in configured target order and retain all original failures for diagnostics. Caller-bounded waiting MUST leave the same owned join available to subsequent shutdown calls. Cancellation-callback failure MUST NOT skip the run join; multiple independent shutdown failures MUST be retained.
- **FR-018**: An unsuccessful runner callback MUST supply the failure sequence used by the host projection. A per-run, per-target diagnostic base MAY derive consecutive failures since Active; the host MUST NOT independently increment a retry count. `NotCurrent` MUST use an explicit safe activation classification, without a fabricated exception or EF refusal.
- **FR-019**: A successful owned activation or runner-observed external settlement MUST finish startup recovery for that target. A later deactivation MUST remain visible through live readiness and MUST NOT restart the completed startup run. This explicitly adopts the runner's earlier external-completion boundary. A recorded retry interval/deadline describes the last selected decision; operator-facing copy MUST NOT promise a future retry after recovery is terminal.
- **FR-020**: Failure projection MUST preserve the existing no-notification boundary: a raw-Active check hides an earlier row and suppresses a new failed write, while an Active lifecycle notification or owned success clears it. A suppressed write MUST NOT inflate the next visible consecutive count. The adapter MUST NOT infer an unobserved lifecycle reset for custom registries.

### Key Entities

- **Activation run**: Explicit target set, serial initial completion, per-target attempts and optional retries, plus owned cancellation/join lifetime.
- **Host activation profile**: Foundation retrying boot, Workbench one-shot eager boot, or Workbench post-listen default warmup; determines phase, target selection and local policy.
- **Host outcome projection**: Sanitized failure/refusal or warmup outcome/telemetry, associated with the actual successful generation where applicable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A rebuilt Foundation process recovers from temporary database unavailability without restart and retains the existing EF refusal/recovery behavior.
- **SC-002**: Deterministic tests demonstrate all three startup profiles, including phase ordering, single underlying eager/warmup activation and no unwanted Workbench retry.
- **SC-003**: Initial/background fatal failures, concurrent fatal targets, cancellation and bounded shutdown satisfy the existing behavior with no lost failure or orphaned activation.
- **SC-004**: Successful built-in and supported custom warmup scenarios retain the returned generation under a controlled concurrent-reload race.
- **SC-005**: Independent review, affected regression/host checks, architecture/maps and behavioral negative controls pass on recorded exact inputs, with no assertion weakening or duplicate activation/retry implementation.
- **SC-006**: Stable completion identifies released source/package versions, clean-cache locked restoration, actual host evidence and green resulting-main gates; preview-only proof never checks off stable acceptance.

## Assumptions

- General runner support is already delivered upstream; research must resolve adapter parity before implementation. A required upstream contract correction, if any, is coordinated separately before consumers depend on it.
- The qualified observer candidate is preserved as local commit `af83b06bb2b4dcd84b1269cc39575ff68033cbbf`; source adoption remains release-gated.
- This unit does not take over #2354 host-library composition extraction, #2164 package-generation readability, #2362 unloading, safe physical pruning or container PID-1 startup behavior tracked by #2128.
- Copilot and Greptile requests are waived by the owner; independent self-review and the remaining verification gates apply.
