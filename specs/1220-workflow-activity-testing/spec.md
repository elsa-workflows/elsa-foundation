# Feature Specification: Workflow and activity testing

**Feature Branch**: `agent/codex/3588-regression-testing`

**Created**: 2026-11-05

**Status**: Approved

**Input**: Deliver [#3588](https://github.com/elsa-workflows/elsa-foundation/issues/3588) and [#3892](https://github.com/elsa-workflows/elsa-foundation/issues/3892) together under [Program #3901](https://github.com/elsa-workflows/elsa-foundation/issues/3901), including design, implementation, Studio/headless qualification, review/merge and resulting-main acceptance. The issue bodies retain the full intake; this specification defines their shared behavior.

## Owner decisions

- Studio authoring and a headless CI runner execute the same saved cases.
- Controlled substitutes and explicit real integration tests against selected test systems are included.
- Multi-step scenarios include supported events/user actions and controlled time in the first delivery.
- Both full workflows and selected configured activity occurrences are explicit targets. Standalone reusable Activity Definition testing alone does not satisfy occurrence testing.
- 5 November: rerun against the current draft/chosen CI candidate by default, record its exact content, and require deliberate rebinding for removed, moved or incompatible activity targets. Explicit historical replay remains available.
- 5 November: a shared Studio Tests library owns saved cases/suites, with versioned export/import for source control and CI. Designer shortcuts use that library.
- 5 November: a suite uses its members' latest saved case revisions by default, resolving and freezing them once when the run starts. Exports freeze exact revisions for CI; running/history manifests never refresh mutable membership.

## User Scenarios & Testing

### User Story 1 — Test an activity and save a regression case (Priority: P1)

A workflow author selects a configured activity in the designer, supplies its supported inputs and surrounding context, runs it, inspects what happened and saves chosen expectations as a named case.

**Why this priority**: The author can check the activity they are editing without arranging an entire workflow journey to reach it.

**Independent Test**: Select a calculation inside a nested scope, provide explicit surrounding data, run only the selected occurrence, save an output assertion, reload Studio and rerun the saved case.

**Acceptance Scenarios**:

1. **Given** an explicitly selected supported occurrence, **when** the author opens Tests, **then** its identity, candidate and required setup are visible; an inspector's implicit container fallback is never selected for them.
2. **Given** complete setup, **when** the author runs a native leaf, **then** its configured bindings execute normally and no predecessor, sibling or unrelated successor runs. A selected supported composite runs its normally owned work.
3. **Given** a manual result, **when** the author saves it as a case, **then** setup is retained and assertions are deliberately chosen. An exploratory run with no assertions shows no test verdict; observed behavior never silently becomes its own expected answer.
4. **Given** a saved case and a changed candidate, **when** a target was removed, moved to another scope or made incompatible, **then** execution is unavailable with a useful reason and deliberate rebind creates a new case revision.
5. **Given** a required prior output, lexical variable, connection or fixture is absent, **when** the author prepares the run, **then** missing data is identified before execution; absent data is not invented, converted to null or obtained by running predecessors.

### User Story 2 — Verify a complete business conversation (Priority: P1)

An author saves a workflow case that starts an order, waits for approval, rejects it, advances time where appropriate and verifies both the business result and that payment was never invoked.

**Why this priority**: A workflow completing successfully does not establish correct business behavior, particularly across waits and asynchronous work.

**Independent Test**: Run the saved rejection case, then introduce a payment-on-rejection regression and observe an actionable failed assertion despite technically completed execution.

**Acceptance Scenarios**:

1. **Given** a captured workflow and case revision, **when** a supported action/event step is delivered, **then** it addresses the exact test-owned active wait using normal model semantics and an idempotent delivery identity.
2. **Given** a controlled timer, **when** test time advances, **then** only supported timers in that test scope become due. Host leases, expiry and other executions retain wall-clock behavior.
3. **Given** an expected rejection or fault, **when** its assertions hold, **then** the case passes while retaining the actual execution outcome. A completed workflow with a wrong output fails.
4. **Given** a non-invocation assertion, **when** it is evaluated, **then** the declared observation interval and asynchronous settlement are complete; an empty log while work remains pending cannot pass it.

### User Story 3 — Maintain a shared library and use it in CI (Priority: P1)

An author creates, edits and organizes workflow/activity cases in one Studio Tests library, runs selected cases or suites and exports the same revisions for headless execution.

**Why this priority**: Reusable regression checks must survive browser reloads, be reviewable in source control and work without Studio automation.

**Independent Test**: Save/reopen a mixed suite, export it, execute the packaged runner against the same candidate, compare verdicts and inspect retained history in Studio.

**Acceptance Scenarios**:

1. **Given** concurrently edited cases, **when** an outdated edit is saved, **then** the conflict is explicit and does not overwrite the newer revision.
2. **Given** a suite run starts, **when** someone edits a member case, **then** the run continues using the captured ordered set of exact case revisions.
3. **Given** the same candidate, case and dependency configuration, **when** Studio and headless execution run them, **then** deterministic business verdicts agree and reports identify their exact inputs.
4. **Given** an exported bundle, **when** it is imported, **then** unsupported format/extensions, tampered identities or conflicting content produce useful errors; connection secrets are absent.
5. **Given** historical results, **when** the workflow or case changes, **then** those results retain the exact tested candidate/case/dependency identities and remain distinct from new runs.

### User Story 4 — Control dependencies or explicitly test a real integration (Priority: P1)

An author chooses repeatable substitutes or explicit real test-system connections, understands which effects are permitted and sees what each result proves.

**Why this priority**: Tests must neither silently contact live systems nor imply that substituted behavior establishes integration correctness.

**Independent Test**: Run a controlled HTTP response and non-invocation case; separately run an explicit integration case against an owned local test system and prove actual transport.

**Acceptance Scenarios**:

1. **Given** controlled mode, **when** any requested target, effect, time or step boundary is unsupported/unconfigured, **then** the case is unavailable before dispatch, without silent real-I/O or real-time fallback.
2. **Given** integration mode, **when** execution starts, **then** chosen test-system connections and time policy are explicit and recorded using safe reference/revision identities.
3. **Given** a module-contributed interaction/assertion/effect profile, **when** it is installed or absent, **then** Studio and headless execution agree on support; missing or ambiguous providers are diagnosed.

### User Story 5 — Stop and inspect an isolated run truthfully (Priority: P1)

An author or CI job cancels a waiting/active case, sees the difference between cancellation requested and settled, and can inspect retained evidence without production-instance changes.

**Why this priority**: Tests that outlive their runner, leak ingress or incorrectly pass incomplete work undermine the product.

**Independent Test**: Start two concurrent test scopes plus an ordinary execution, deliver same-name events, cancel one test, expire another and verify isolation, cleanup and distinct results.

**Acceptance Scenarios**:

1. **Given** test-owned waits, **when** ordinary ingress or another test sends a matching stimulus, **then** it cannot resume those waits accidentally.
2. **Given** cancellation/timeout/expiry, **when** closure starts, **then** admission closes, owned execution and detached work are reconciled, cleanup is recorded and uncertain settlement is incomplete rather than passed.
3. **Given** runner interruption or lost authority, **when** execution cannot be confirmed, **then** history reports the interruption/uncertainty and no pass is inferred.

### Edge Cases

- Empty suites; intentional skips; all-skipped selections; mixed failed/unavailable cases; a case with no assertions.
- Duplicate dispatch/delivery after a lost response; same operation identity with changed content; concurrent cancellation and settlement.
- Explicit null versus omitted fixture values; renamed/moved scopes; duplicate node IDs; changed output contracts; expression dependencies on absent prior results.
- Nested/reusable occurrence dependencies that changed even though the outer workflow did not; retained candidate no longer available.
- Native composite, native BPMN structural activity, BPMN-bound task and direct model execution advertise only model-qualified support, with precise unavailable reasons for others.
- Missing extension or connection; revoked permission during a run or history read; redacted faults/outputs; disposed test-system fixtures.
- Virtual clock races with durable timer delivery and child work; infrastructure expiry during a long virtual scenario; cleanup failing after assertions passed.

## Requirements

### Functional Requirements

- **FR-001**: Provide one saved case/suite model, scenario execution service and assertion system for workflow and activity targets, used by manual Studio, saved Studio and headless execution.
- **FR-002**: Capture exact candidate content/artifact/source identity, target identity, case/suite revisions, mode, extension versions and required dependency identity before execution; never resolve mutable latest content mid-run.
- **FR-003**: Default reruns to the current chosen candidate with compatibility validation. Offer explicit retained-candidate replay, with clear unavailability when content/dependencies are not retained.
- **FR-004**: Identify an occurrence by stable node identity, owning structural path and expected contract/model identity. Removed, moved, ambiguous or incompatible targets require an explicit new binding and case revision.
- **FR-005**: Preserve configured occurrence inputs and normal model execution. Allow explicit, visible test input overrides and supported workflow/lexical variables, expressions, prior-output fixtures and connections; retain their provenance in the case revision.
- **FR-006**: Validate surrounding data before running; do not execute predecessor activities or invent defaults/nulls for missing required context. A default explicitly declared by the captured contract remains legitimate and visible.
- **FR-007**: Support qualified native leaf/composite/nested/reusable targets. A selected composite executes normally owned work; reaching the selected boundary ends execution rather than traversing its containing graph.
- **FR-008**: Assess BPMN and other model-bound targets through their owning model. Unsupported independent execution is explicitly unavailable; extraction must not bypass model ownership or token semantics.
- **FR-009**: Save/reopen/edit/rerun cases and suites in a tenant/backend-authorized Studio library with optimistic concurrency, immutable captured revisions and durable history.
- **FR-010**: Keep tests and results outside the workflow business definition. Capture exact case membership at suite start; subsequent authoring cannot change an admitted run.
- **FR-011**: Export/import versioned portable cases/suites/candidate manifests for source control and headless CI, with deterministic validation and no plaintext credentials.
- **FR-012**: Provide ordered start, supported event/action delivery, controlled-time advance, settlement and assertion steps. Synchronize on model-owned observable state, never arbitrary business sleeps.
- **FR-013**: Assertions cover outputs/outcomes, expected faults/waits, relevant paths and dependency invocation/non-invocation, with expected/actual differences and execution context.
- **FR-014**: Separate execution lifecycle from test verdict. Preserve passed, failed, skipped, cancelled, unavailable and incomplete. Expected fault/rejection may pass; completion alone may not.
- **FR-015**: Exploratory manual runs with no assertions display no verdict and cannot be reported as passed CI cases. Saving observed values as assertions requires deliberate author selection.
- **FR-016**: Controlled mode admits only qualified effect/time boundaries and configured substitutes. Unknown/unqualified effects or unsupported control requests are unavailable before dispatch; no live fallback.
- **FR-017**: Integration mode requires explicit test-system connection mappings and supported time policy. Record safe dependency provenance and actual interaction evidence, distinctly from substitutes.
- **FR-018**: Scope controlled time and supported timer delivery to the test. Host clock, leases, real timeout, expiry and retention use wall time. No global clock changes.
- **FR-019**: Bound asynchronous settlement and the observation interval for interaction assertions; pending/unknown work cannot establish non-invocation.
- **FR-020**: Isolate test starts, waits, children, detached work and ingress by trusted scope/tenant/backend authority, without production publication or production instance mutation.
- **FR-021**: Implement idempotent run/step admission and duplicate reconciliation; reject reuse of an operation identity with different material.
- **FR-022**: Define finite real duration, cancellation, expiry, interruption and cleanup. Cancellation intent is distinct from terminal settlement; cleanup failure or uncertain settlement cannot pass.
- **FR-023**: Reauthorize execution and evidence reads; use secret references and redaction. Opaque identifiers confer no authority, and imported content never selects trusted tenant/backend scope.
- **FR-024**: Retain safe history and machine-readable detailed reports. Provide a standard CI projection and documented process exits that distinguish success, assertion failure, inability to complete and cancellation; skipped/unavailable/incomplete selections cannot silently make a successful CI gate.
- **FR-025**: Expose legitimate module-owned target, setup/step, assertion, effect and time contribution seams, with explicit support discovery and missing/duplicate-provider diagnostics in both clients.
- **FR-026**: Reuse suitable identity/evidence conventions from spec 1179 without implementing competing agent-quality evaluation or depending on its undelivered saved evaluation feature.
- **FR-027**: Preserve ordinary Test Run/Test Drive behavior and normal runtime/model semantics. One interpreter and one shared testing product serve both issues.
- **FR-028**: Qualify actual composed Studio authoring/save/reload/run/history, selected activity conversion, full-workflow multi-step/time, owned real integration and packaged headless equivalence. Retain downloadable current-commit screenshots/traces and independent UX acceptance.
- **FR-029**: Keep failed/skipped/not-run/unverified qualification visible. Both features remain open until all agreed scope, required checks/review/merge and resulting-main qualification pass.
- **FR-030**: Allocate fresh mutable fixture and substitute state per case attempt. Clean up resources created during partially failed preparation as well as execution, and tear down only resources owned by that attempt. Record cleanup identity/outcome without deleting shared test-system data.
- **FR-031**: Publish the delivery support matrix and qualify at least one native leaf, selected native composite, nested-scope occurrence and reusable activity occurrence through the same supported context/normal execution path. Truthful unavailability for unqualified custom/model-bound profiles does not replace these required native witnesses.
- **FR-032**: A workflow designer test captures the current editor content, including unsaved edits, as its exact candidate without silently saving the business draft. Never run an older persisted draft while labeling it current. Where another authoring surface requires a saved revision, require an explicit Save and identify that revision before running.
- **FR-033**: Converting a manual run to a saved scenario includes author-confirmed supported interactive deliveries/time advances in their observed order, alongside setup and chosen assertions. Resolve run-specific wait identities to stable authored selectors with explicit ambiguity validation, never copy old runtime IDs as replay selectors.
- **FR-034**: Pending or invalid unapplied code-editor changes block candidate capture until the author explicitly applies or resets them; preserve the buffer and identify the action required. With autosave disabled, testing an applied unsaved change uses that new content and leaves the persisted business draft unchanged.
- **FR-035**: Infrastructure/control refusals, missing context/providers, lost authority, timer/effect capability failures and uncertain cleanup cannot satisfy an expected business-fault assertion. Fault checks identify the actual target/model business observation and retain its provenance; a failed preparation or unavailable execution remains unavailable/incomplete.

### Key Entities

- **Test case**: Named purpose, target binding, setup/fixtures, dependency/time policy, ordered steps/assertions and duration/cleanup policy; editable with a concurrency revision.
- **Case revision**: Immutable captured content/fingerprint and extension profile used for a run or export.
- **Test suite revision**: Immutable ordered logical case membership, required/skip intent and associations. Admission/export resolves latest saved members once into a separate immutable exact-revision manifest.
- **Candidate manifest**: Exact workflow/activity content, owning model, dependencies and compatibility facts used for execution.
- **Run/result**: Immutable admission identity and manifest, lifecycle observations, verdict/check differences, runtime links, interaction/clock evidence and settlement/cleanup outcome.
- **Test environment**: Authorized backend/tenant boundary, qualified module support and explicit safe test-system connection references.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A user completes selected nested activity → manual run → chosen output assertion → saved case → reload → rerun, with zero unrelated activity invocations and the correct retained identities.
- **SC-002**: The same exported mixed workflow/activity suite yields equivalent deterministic verdicts in Studio and the packaged headless runner for identical candidate/case/dependency material.
- **SC-003**: Deliberate wrong-output, wrong-outcome and forbidden-payment regressions each fail an actionable saved assertion; expected rejection/fault controls pass.
- **SC-004**: A full-workflow case delivers a supported action/event and advances a long controlled delay without waiting its business duration or changing other executions/infrastructure time.
- **SC-005**: Controlled missing/unsupported boundaries produce zero real external calls, while an explicit owned test-system integration proves real transport with recorded mode/dependency identity.
- **SC-006**: Missing context, removed/moved/incompatible targets and unavailable models refuse execution; deliberate rebinding produces a new revision and never changes historical results.
- **SC-007**: Concurrent-scope ingress, cancellation, expiry and interruption controls prove isolation and truthful settlement/cleanup reporting; no incomplete or unasserted run is passed.
- **SC-008**: Save/reopen/edit/conflict, permissions, keyboard/focus, empty/populated, narrow and theme journeys pass on the composed host with inspected downloadable current-commit evidence.
- **SC-009**: A module extension executes through both Studio and headless paths, and its absence/ambiguity yields the same useful unavailability.
- **SC-010**: Exact-head checks, independent required reviews, merge and resulting-main qualification are recorded before either feature is called delivered.
- **SC-011**: Representative native leaf/composite/nested/reusable occurrence cases prove only the selected semantically owned work runs. Two concurrent attempts sharing a case definition have separate mutable fixtures/substitute histories, and partial-preparation failure cleans up only its owned resources.

## Assumptions

- Owner authorization covers necessary scoped design, implementation and delivery transitions. No release or production deployment is included.
- Existing normal runtime, immutable artifact capture and durable test-scope/wait/cancellation primitives are reused; missing scoped time/occurrence behavior is implemented and qualified, not inferred from their existence.
- Arbitrary third-party CLR/script code is not automatically sandboxed. Only declared and qualified boundaries advertise controlled support; rejected profiles remain visible.
- Chosen integration systems and qualification fixtures are owned disposable test resources. No production credentials or destructive external operations are needed.
- No initiative parent or `Part of` link covers these features at establishment. Shared runtime primitives do not by themselves create such a boundary.
- Constitution provisional activity-version/state policies remain provisional; this feature does not ratify or amend them.
