# Feature Specification: Execution Evidence foundation vertical slice

**Feature Branch**: `779-execution-evidence-foundation`

**Created**: 2026-08-05

**Status**: Approved

**Input**: Build the Execution Evidence foundation vertical slice for GitHub issue #1133. A host explicitly enables the domain, a remote caller opens an evidence session, and an associated workflow produces deterministic committed workflow/activity evidence that can be queried or awaited through a neutral HTTP API while existing Runtime modules remain unaware of Execution Evidence.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Enable isolated evidence capture (Priority: P1)

A shell composer wants to enable Execution Evidence only in selected hosts, and a test runner wants to associate one workflow scenario with one evidence session, so that unrelated workloads do not produce or leak evidence.

**Why this priority**: Explicit activation and isolation are the safety boundary for the entire domain. Without both gates, enabling the module would add unwanted runtime work and shared-host evidence could not be trusted.

**Independent Test**: Compose one host with the domain enabled and one without it; open a session in the enabled host, associate one workflow, and run both the associated and an unscoped workflow. Inspect the session and confirm that only the associated workflow has evidence and that the host without the domain has no evidence-specific behavior.

**Acceptance Scenarios**:

1. **Given** a host with Execution Evidence disabled, **When** workflows execute normally, **Then** no evidence-specific registration, branch, allocation, serialization, or persistence work is introduced into existing Runtime modules.
2. **Given** a host with Execution Evidence enabled but no open evidence session, **When** an unscoped workflow executes, **Then** it completes without producing evidence.
3. **Given** an enabled host and an open evidence session, **When** a caller associates a workflow with that session and drives its ordinary minimal execution path, **Then** the committed workflow and activity facts are isolated under that session.

---

### User Story 2 - Capture committed workflow and activity facts reliably (Priority: P1)

A test author wants the first evidence records to represent only committed semantic facts, with stable identity and ordering, so that retries and failed persistence cannot create false positives or silently lose proof.

**Why this priority**: The value of evidence depends on its atomicity and determinism. A record for rolled-back work, or an ambiguous missing record, cannot support reliable verification.

**Independent Test**: Run a session-scoped workflow through a successful path containing activities, repeat enrichment for the same checkpoint, inject preparation and checkpoint-persistence failures, then inject a post-commit materialization failure and a duplicate delivery. Inspect checkpoint outcome, evidence identity, sequence, and materialized record count.

**Acceptance Scenarios**:

1. **Given** a session-associated workflow checkpoint with committed workflow/activity transitions, **When** the checkpoint succeeds, **Then** one bounded evidence batch is recorded as one opaque post-commit intent and its typed records are materialized with stable identities, a monotonic workflow-local sequence, and stable checkpoint-local ordinals.
2. **Given** the same checkpoint commit is enriched more than once, **When** the resulting batches are compared, **Then** their identities, payloads, and fingerprints are identical and no new semantic occurrence is created.
3. **Given** required evidence preparation or intent persistence fails before checkpoint commit, **When** the checkpoint is attempted, **Then** the checkpoint does not succeed and no evidence intent or record is exposed for that attempt.
4. **Given** a checkpoint has committed but evidence materialization fails, **When** delivery is retried or redelivered, **Then** the workflow state remains committed, the evidence range remains incomplete until delivery settles or reports integrity failure, and duplicate delivery does not create duplicate records.

---

### User Story 3 - Query and await evidence through a neutral API (Priority: P1)

A remote test runner wants to manage a session, retrieve only the evidence relevant to its scenario, and wait for committed facts without fixed sleeps, so that verification remains portable and diagnosable.

**Why this priority**: Remote access and explicit wait outcomes make the slice usable by test systems without coupling Elsa to a test framework or requiring callers to inspect internal runtime state.

**Independent Test**: Use the authorized HTTP surface to create, inspect, complete, query, await, and delete a process-local session while driving a normal workflow through its ordinary API. Verify filters, opaque continuation, wait outcomes, integrity state, and complete-session deletion.

**Acceptance Scenarios**:

1. **Given** an authorized caller, **When** it opens, inspects, completes, and deletes an evidence session, **Then** each lifecycle operation returns the session's current lifecycle and integrity state, and deletion removes the session as one complete unit.
2. **Given** a session containing workflow and activity evidence, **When** the caller queries by session, kind, workflow, activity, subject, correlation, or sequence and continues with the returned cursor, **Then** the response contains only matching records and the cursor remains opaque to the caller.
3. **Given** a wait for matching evidence, **When** the record is materialized, the deadline expires first, or the session reaches a completed range without a match, **Then** the API distinguishes a match, an inconclusive timeout, and the completed-range outcome. The completed-range outcome is observable in this slice but is not definitive negative proof; #1134 adds settled barriers, general sequence-gap detection, gap-free completeness, and the full definitive-negative semantics.
4. **Given** the process-local store loses state because its process ends, **When** a caller reconnects, **Then** the slice makes no crash-completeness claim and does not claim recovered evidence or completeness across the process-loss boundary.

---

### User Story 4 - Govern baseline kinds and leave a safe extension seam (Priority: P2)

A module author wants to register a typed, versioned evidence kind without changing the baseline domain, and a consumer wants to inspect the common envelope even when it does not understand a registered kind, so that the protocol can evolve without arbitrary payloads or accidental collisions.

**Why this priority**: A governed catalog keeps the foundation stable while allowing later lifecycle, causation, value, and provider slices to contribute deliberately.

**Independent Test**: Register the baseline workflow/activity kinds and one additional typed kind, then attempt a conflicting registration and an unregistered ad hoc payload. Query the registered kind through a consumer that knows only the common envelope.

**Acceptance Scenarios**:

1. **Given** a baseline or contributed kind registration with a stable string, schema version, typed payload contract, and capture metadata, **When** the host starts, **Then** the registration is available through the catalog.
2. **Given** two registrations that conflict on a kind or schema contract, **When** the host starts, **Then** startup fails deterministically rather than selecting one registration implicitly.
3. **Given** an unregistered arbitrary dictionary payload, **When** it is submitted as evidence, **Then** it is rejected.
4. **Given** a registered kind unknown to a consumer, **When** the consumer filters or reads the record, **Then** it can inspect the common envelope without interpreting the typed payload.

### Edge Cases

- A session identifier is missing, invalid, or belongs outside the caller's authorization and tenant/access scope; the API rejects the operation without exposing another session.
- A workflow is associated with a session after it has already committed work; only subsequently captured committed facts are in scope, and earlier unscoped work is not retroactively reconstructed.
- A checkpoint contains multiple workflow or activity transitions; each committed transition remains a distinct typed record with deterministic checkpoint-local ordering.
- A checkpoint is skipped or contains no committed semantic transition; it does not create a misleading evidence batch or record.
- A post-commit delivery is attempted more than once, including after a failure following the materialization write; stable evidence identity makes the operation idempotent.
- A query cursor is malformed, bound to another session or query shape, or reused after deletion; the API rejects it explicitly rather than silently returning a different range.
- A wait deadline expires while the workflow remains open or delivery is pending; the result is inconclusive. General sequence-gap detection and the definitive treatment of a gap-free range are deferred to #1134, so this slice does not turn either condition into a negative assertion.
- A caller tries to delete an active or incomplete session; deletion follows the session lifecycle contract and does not silently erase a range whose integrity state is unknown.
- A value-capture request is present even though this slice does not capture state, input, output, or payload values; the slice remains metadata-only and no profile request causes value capture. Profile enforcement, sanitization, redaction, truncation, and disposition behavior are deferred to #1136.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The slice MUST establish three separately composable modules: `Elsa.Workflows.ExecutionEvidence.Core` for provider-neutral contracts, `Elsa.Workflows.ExecutionEvidence` for capture/session services and the process-local implementation, and `Elsa.Workflows.ExecutionEvidence.Api` for the neutral HTTP surface. Existing Runtime modules MUST NOT reference Execution Evidence contracts, settings, models, events, or conditional branches.
- **FR-002**: The host MUST require explicit Execution Evidence activation before any evidence-specific services are available, and an enabled host MUST require an explicitly opened evidence session before it captures a workflow.
- **FR-003**: The evidence-session association MUST be carried through the minimal workflow execution path used by this slice so that committed workflow and activity facts remain isolated under one `EvidenceSessionId`. Test-run and test-case identifiers MAY be supplied as correlation metadata but MUST NOT replace the runtime session identity.
- **FR-004**: The common evidence envelope MUST expose stable evidence identity, evidence-session identity, kind and schema version, workflow identity and workflow-local sequence, checkpoint identity and checkpoint-local ordinal, occurred time as diagnostic metadata, optional activity identity, optional causation identity, correlation metadata, and a typed payload. Wire identity MUST use stable kind strings and explicit schema versions rather than CLR names.
- **FR-005**: The foundation catalog MUST define the successful minimal-path baseline kinds `workflow.started`, `workflow.completed`, `activity.started`, and `activity.completed`, each with schema version `1` and a typed payload sufficient to identify the affected workflow or activity and its committed transition. The slice MUST NOT claim the later full lifecycle catalog.
- **FR-006**: The catalog MUST support provider-neutral registration of additional typed, versioned kinds with declared capture metadata. Conflicting registrations MUST fail deterministically at startup, and unregistered arbitrary payloads MUST be rejected while registered unknown kinds remain inspectable through their common envelope.
- **FR-007**: The `.Core` contract surface MUST include provider-neutral session, capture-profile, cursor, query, store, integrity, and contribution contracts. The slice MUST reserve the explicit value dispositions `captured`, `redacted`, `omitted`, and `truncated` for future providers, but MUST remain metadata-only: no capture-profile request causes value capture. State mutation, input/output/payload capture, profile enforcement, sanitization, redaction, truncation, and disposition behavior belong to #1136 and are outside this slice.
- **FR-008**: For every eligible committed checkpoint, capture MUST produce one complete bounded evidence batch and one opaque post-commit intent. Evidence intent, batch, and record identities MUST be deterministic from stable commit identity and fixed discriminators; enrichment MUST NOT read current time, randomness, or mutable external state.
- **FR-009**: Required evidence preparation or persistence MUST be part of checkpoint success. A failed or skipped checkpoint MUST NOT expose an evidence intent or evidence record, and canonical evidence MUST NOT be emitted best-effort before or after a checkpoint without a recoverable intent.
- **FR-010**: An evidence-owned post-commit delivery path MUST materialize records into the process-local store with at-least-once semantics and idempotency by stable evidence identity. Delivery failure MUST NOT roll back committed workflow state, but MUST remain visible as pending or failed delivery until it is resolved; this slice makes no general completeness claim from that state.
- **FR-011**: The evidence store MUST preserve strict monotonic ordering per workflow and stable checkpoint-local ordinals. The slice MUST expose explicit visibility for pending delivery, duplicate handling, and process-local integrity limitations and MUST NOT promise one global semantic order or the general sequence-gap/completeness behavior owned by #1134 across concurrent workflows.
- **FR-012**: Authorized HTTP endpoints MUST support evidence-session creation, inspection, completion, filtered query, cursor continuation, cursor-based wait, delivery/process-local integrity reporting, and deletion of a complete session. Deletion of an active or incomplete session MUST be rejected, and deletion of a completed session MUST remove its records and delivery state as one unit. Existing tenant and access-context rules MUST apply, and the API MUST NOT expose provider offsets or require a test-framework assertion model.
- **FR-013**: Wait responses MUST distinguish a matching record, an inconclusive timeout, and an observable completed range without a match. The completed-range outcome MUST NOT be treated as definitive negative proof in this slice, and timeout alone MUST never prove absence; #1134 owns settled barriers, general sequence-gap detection, gap-free completeness, and the full definitive-negative wait semantics.
- **FR-014**: The initial store MUST be process-local in memory. It MUST preserve commit visibility, stable identity, ordering, filtering, cursor binding, wait outcomes, explicit integrity state, and complete-session deletion, but MUST make no crash, restart, failover, or distributed durability claim.
- **FR-015**: Contract, registration, API integration, and backend end-to-end tests MUST verify module composition, session gating, minimal workflow/activity capture, deterministic enrichment, strict checkpoint failure behavior, idempotent delivery, query/wait semantics, authorization, and cleanup through ordinary workflow APIs.
- **FR-016**: Registration tests MUST prove that module absence introduces no evidence-specific Runtime registrations or dependencies. The feature MUST include host-composition examples, feature documentation, the domain extension-point catalog, README/reference material, and affected generated map updates required by the repository's documentation gates.
- **FR-017**: Benchmarks MUST record reproducible first-slice baselines for at least these modes: Execution Evidence absent, enabled but unscoped, and enabled with scoped metadata-only capture. The results MUST report throughput and allocation observations without turning them into a later regression budget.

### Key Entities

- **Evidence session**: An explicitly opened, authorized capture scope identified by `EvidenceSessionId`, with lifecycle, correlation metadata, delivery state, and process-local integrity limitations.
- **Evidence record**: One immutable typed fact about a committed workflow or activity transition, identified by a stable evidence identity and carried in the common envelope.
- **Evidence kind registration**: The governed declaration of a stable kind string, schema version, typed payload contract, and capture metadata.
- **Evidence batch and intent**: The bounded, deterministic group of records derived from one eligible checkpoint and the opaque recoverable delivery unit committed with that checkpoint.
- **Evidence cursor**: An opaque continuation position bound to a session query or wait request.
- **Evidence capture profile**: A provider-neutral session-level contract for selecting value subjects and dispositions; in this slice it provides extension room only and does not enable value capture.
- **Evidence integrity state**: The explicit status describing pending or settled delivery, duplicate handling, session completion, and process-local limitations. In this slice it does not establish gap-free range completeness or support a definitive negative assertion; those semantics belong to #1134.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can compose the three initial modules independently according to their dependency envelopes, and a host with the domain absent has zero evidence-specific Runtime registrations, settings, branches, models, serialization, or persistence work.
- **SC-002**: In one process, an authorized caller can create, inspect, complete, query, await, and delete an evidence session through the neutral HTTP API, with all lifecycle responses identifying the current lifecycle, delivery, and process-local integrity state.
- **SC-003**: A successful session-associated minimal workflow path produces at least one committed workflow record and one committed activity record, each with a stable kind/schema identity, stable evidence identity, workflow-local sequence, and checkpoint-local ordinal; repeated enrichment of the same checkpoint produces the same batch and record identities.
- **SC-004**: An enabled but unscoped workflow produces zero evidence records, and a failed or skipped checkpoint produces zero evidence intents and zero evidence records for the attempted transition.
- **SC-005**: Across failure-injection tests, required evidence preparation/persistence failure prevents checkpoint success; post-commit materialization failure leaves the checkpoint committed; and one or more duplicate deliveries leave exactly one materialized record per stable evidence identity.
- **SC-006**: The query API supports every required session, kind, workflow, activity, subject, correlation, and sequence filter with opaque cursor continuation, and cursor misuse is rejected rather than silently broadened.
- **SC-007**: The wait API distinguishes a match, an inconclusive timeout, and an observable completed range without a match; it never reports timeout or the completed-range outcome as definitive negative proof in #1133. Settled barriers, general sequence-gap detection, gap-free completeness, and definitive-negative semantics are verified by #1134.
- **SC-008**: Contract and registration tests prove governed catalog conflict handling, rejection of unregistered payloads, forward inspection of an unknown registered kind, module-absence isolation, and expected host composition; API and backend end-to-end tests pass for the ordinary workflow path.
- **SC-009**: The first-slice benchmark records throughput and allocation baselines for the absent, enabled-unscoped, and scoped metadata-only modes, and the required feature documentation, extension-point catalog, host examples, and generated-map updates are present before the work unit is ready for implementation review.

## Assumptions

- The existing generic Runtime checkpoint-enricher and opaque post-commit intent seams remain available and semantically sufficient; this slice consumes them through Execution Evidence-owned adapters and does not add evidence-specific Runtime concepts.
- The first slice uses a process-local in-memory evidence store. Groundwork evidence persistence, restart/failover recovery, distributed completeness, provider conformance, retention cleanup, quotas, and crash durability belong to later work.
- The ordinary workflow API can drive a minimal workflow/activity path and can carry the session association without requiring a test-framework-specific Elsa contract.
- The minimal baseline covers successful workflow/activity transitions only. Bookmarks, incidents, checkpoints as a user-visible catalog family, full lifecycle outcomes, stimuli, scheduling, child-workflow causation, state mutations, and value evidence are later slices; the common envelope leaves deliberate extension room for them.
- The stable baseline kind strings and schema versions in FR-005 are the foundation protocol for this slice. Future breaking payload changes require a new schema version rather than changing the meaning of an existing kind.
- Existing endpoint authorization, tenant scope, and access-context rules protect session lifecycle, query, wait, and deletion; this slice does not create a separate evidence-specific authorization policy engine.
- Session completion is a caller-visible lifecycle state and may expose a completed range without a matching record, but #1133 does not treat that outcome as definitive negative proof. Pending delivery and process-local integrity limitations remain visible; #1134 adds settled barriers, general sequence-gap detection, gap-free completeness, and definitive-negative semantics. Explicit deletion removes a session as one complete unit; automatic retention and per-record expiry are not part of this slice.
- Elsa provides neutral protocol and conformance fixtures as the contract evolves; J-Test owns its assertion DSL, framework lifecycle, retry policy, and adapter implementation outside this repository.
- The constitution and ADRs used here remain draft/proposed material. This specification preserves their current status; plan and implementation review must validate or explicitly accept the relevant decisions rather than silently treating them as ratified.

## Out of Scope

- Groundwork evidence query-store persistence, durable recovery, restart/failover claims, and distributed provider behavior.
- General sequence-gap detection, settled barriers, gap-free range completeness, and definitive-negative wait semantics owned by #1134.
- Full committed lifecycle coverage for bookmarks, incidents, suspension/resumption, cancellation/fault variants, checkpoint/barrier catalog facts, stimuli, scheduling, timers, child workflows, and cross-workflow causation scenarios.
- State-mutation evidence, input/output/payload value capture, capture-profile enforcement, sanitization, redaction, truncation behavior, and any generalized data-classification policy engine beyond provider-neutral contract room.
- Studio, dashboards, timelines, human-facing UI, or replacement of inspection projections, logs, metrics, traces, or OpenTelemetry.
- Always-on or retroactive capture, evidence for attempted or rolled-back behavior, global session ordering, exactly-once external delivery, cross-store ACID, quotas, storage-pressure eviction, and indefinite default retention.
- An Elsa-owned test assertion DSL, test-run/test-case lifecycle, pass/fail model, or J-Test implementation code.
- Evidence-specific contracts, settings, models, events, branches, or registrations added to existing Runtime modules.
