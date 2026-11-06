# Feature Specification: Business decisions

**Feature Branch**: `agent/codex/3602-business-decisions`
**Created**: 2026-11-02
**Status**: Implemented
**Input**: [#3602](https://github.com/elsa-workflows/elsa-foundation/issues/3602), including the 1 November interview and the owner's 2 November autonomous delivery request.
**Program-goal state**: `none/free-flow`. This feature does not widen Unified Execution #2892 or activate a new program.

## User Scenarios & Testing

### User Story 1 — Author and publish a reusable decision (Priority: P1)

A policy author maintains a named table, independently of a workflow, and publishes an immutable version after validation.

**Why this priority**: A business policy needs its own identity and lifecycle to be reused safely.

**Independent Test**: In Studio, create “Determine approval requirements”, define purchase amount and customer category, enter the issue's four rules, validate, publish, reload, and inspect the same version and rules.

**Acceptance Scenarios**:

1. Given authoring permission, creating and saving a decision retains its name, input/output definitions, ordered rules and annotations after reload.
2. Given two editors of the same draft revision, saving the second after the first returns a conflict and preserves both the server's committed state and the second editor's unsaved work for explicit resolution.
3. Given an invalid or unsupported table, validation identifies the decision, row, column and reason; publication fails without creating a published version.
4. Given a published version, later draft edits cannot mutate that version or its recorded content identity.
5. Given keyboard navigation and a narrow viewport, an author can reach, edit and identify every table cell and validation finding; horizontal table scrolling preserves surrounding controls.

### User Story 2 — Evaluate and understand a decision (Priority: P1)

A caller supplies typed input values and receives the result and the rules responsible for it. The caller performs any business actions.

**Why this priority**: A rendered table alone is not a business-decision capability.

**Independent Test**: Evaluate the approval example at 1000, 1000.01, 10000 and 10000.01, with both relevant categories; inspect results, matched rows, selected rows and exact decision identity.

**Acceptance Scenarios**:

1. Given supported inputs and a frozen decision, repeated evaluations return equivalent results and rule explanations.
2. Given overlapping matches, the selected hit policy determines the result or returns an explicit policy violation; explanations distinguish matched rules from selected rules.
3. Given missing inputs, explicit null, incorrect types, unsupported numeric values or expression failures, the caller receives a structured outcome rather than an invented default or partial success.
4. Given exhausted evaluation limits or cancellation, the call stops with a distinguishable outcome and produces no business actions.
5. Given no matching rule, the documented default/no-match behavior is visible, including the difference between no result and a successful null result.

### User Story 3 — Catch a policy regression (Priority: P1)

An author saves representative inputs and expected results, then reruns them against a draft candidate or a specific published version.

**Why this priority**: Authors must be able to assess the business effect of a change before publication.

**Independent Test**: Save the four approval boundary cases, deliberately change a threshold, rerun, and inspect the failed expected value with the exact tested candidate identity.

**Acceptance Scenarios**:

1. A saved case retains typed inputs, expected status/result, its own revision and a readable name.
2. A test run freezes the selected draft revision/content or published version before evaluation; a concurrent edit cannot relabel the run as testing newer content.
3. A deliberate threshold regression fails the affected expectation with its output path, expected value, actual value and matched/selected rule identities.
4. An identical retry of a completed run does not append a contradictory duplicate receipt; a changed candidate is a distinct run.

### User Story 4 — Interchange supported DMN tables (Priority: P1)

An author moves a supported table between Elsa and external DMN tooling and can see which documents are retained for viewing versus admitted for execution.

**Why this priority**: Interoperability belongs in the first release.

**Independent Test**: Import an external-tool table, evaluate representative cases, edit and export it, reimport it through the external tool, and verify the same supported configuration and results.

**Acceptance Scenarios**:

1. Supported tables retain decision/rule identity, supported types, hit policy, ordering and result semantics through both round-trip directions.
2. Unsupported expressions, dependent decisions, executable extensions or other unsupported executable semantics are reported explicitly and block execution/publication.
3. Retaining an unsupported document does not imply it is runnable. Its original XML remains downloadable without silent conversion into a reduced model.
4. Supported edits preserve diagram and opaque extension content covered by the interoperability contract; unsupported edit/preservation combinations refuse the mutation rather than discard content silently.
5. Malformed XML, external entities and oversized documents fail before executable admission.

### User Story 5 — Reuse the exact policy in ordinary applications and workflows (Priority: P1)

An application evaluates a decision directly. Workflow authors use the same published policy from native and BPMN workflows.

**Why this priority**: Business policies must work independently and in normal orchestration.

**Independent Test**: Use a packaged ordinary application and normal native/BPMN callers to evaluate the same frozen approval version and compare results/explanations.

**Acceptance Scenarios**:

1. Standalone evaluation works without installing Studio, a workflow engine, a database or an HTTP host.
2. A workflow selection of a specific version or latest-at-publication resolves to exact immutable content when the workflow is published.
3. Editing or republishing the decision while a workflow is suspended does not change the decision used after retry/recovery.
4. Missing or mismatched frozen content fails explicitly; runtime never resolves a replacement from mutable design state.
5. BPMN callers evaluate the policy through a normal bound task and route on returned data. Existing BPMN gateway expression behavior remains valid.

### User Story 6 — Protect tenant and sensitive policy data (Priority: P1)

An administrator controls who may read, edit, publish, invoke, test and inspect retained values. Tenant boundaries hold on every operation.

**Independent Test**: Exercise two tenants and principals with different grants through asset, version, import/export, invocation, cases, history and explanation operations.

**Acceptance Scenarios**:

1. A request cannot select another tenant by supplying an asset, version, case or run identifier.
2. Invocation permission alone does not grant retained-input/history access, authoring or publication.
3. Users lacking sensitive-value permission cannot recover protected values from explanations, validation errors, test comparisons, exports, logs or persisted receipts.
4. Permission changes apply to the next protected operation; the browser's cached capability state cannot grant server authority.

### Edge Cases

- UNIQUE overlaps; ANY equal/different typed outputs; FIRST shadowed rows; ordered multi-result output; no-match/default/null distinctions.
- Quoted strings containing commas, quotes or range-like punctuation; number boundaries and locale changes; null comparisons; an omitted input versus explicit null.
- Duplicate XML IDs/names, unknown semantic attributes/namespaces, wrong row widths, invalid type references and executable extensions.
- Conflicting saves/publications, duplicate operation IDs, completed tests racing edits, stale browser requests after tenant/backend/user navigation.
- Oversized XML, deeply nested input, long strings, excessive rows/cells, cancelled compilation/evaluation and unavailable storage.
- A decision changes while a published native/BPMN workflow waits or retries; runtime has no design-store access.

## Requirements

### Functional Requirements

- **FR-001**: Provide independently managed, named, tenant-scoped decision assets with mutable drafts and immutable published versions.
- **FR-002**: Provide accessible table authoring of inputs, outputs, ordered rules, annotations and supported hit policies, with revision conflicts handled explicitly.
- **FR-003**: Define and expose one versioned DMN compatibility profile; importability, editability, executability and publication eligibility are separate reported properties.
- **FR-004**: Support common business-table comparisons, ranges, alternatives/lists and typed results. Refuse unsupported FEEL before execution/publication. This boundary was selected by Sipke through a card on 2 November 2026.
- **FR-005**: Specify types, number limits, null/missing handling, output shape, hit-policy behavior, ordering, errors and resource limits in a testable interoperability contract.
- **FR-006**: Evaluation returns data and an actual evaluator explanation with exact decision/version/content/profile/semantics identity and matched/selected rule identities. It executes no external business actions.
- **FR-007**: Retain exact candidate identity in draft tests and exact immutable identity in published evaluation. Equivalent retries cannot silently select a newer policy or implementation semantics.
- **FR-008**: Provide saved cases, structured expected-result comparison and permission-controlled test history; detect a deliberately introduced rule regression.
- **FR-009**: Provide supported DMN import/export in both directions with preservation guarantees and explicit refusals for unsupported semantics/preservation combinations.
- **FR-010**: Support standalone packaged evaluation and optional normal native/BPMN integrations with equivalent results and explanations for the same frozen content/input.
- **FR-011**: Resolve workflow version selection at publication, embed the required immutable executable content, and recover without consulting mutable decision design state.
- **FR-012**: Preserve the existing finite-inference Rules model, its contracts, artifacts, receipts and integration behavior.
- **FR-013**: Enforce tenant and server-side operation permissions across all reads, mutations, tests, execution and explanations, with separate sensitive-value access.
- **FR-014**: Bound XML parsing, compilation, input size and evaluation, returning explicit invalid/unsupported/unavailable/conflict/budget/cancelled outcomes without partial success.
- **FR-015**: Keep routine diagnostics free of raw business values. Stored cases/history declare retention and deletion behavior and do not bypass the permission boundary through comparison or error detail.
- **FR-016**: Document supported packaging/hosting shapes and module-author integration; basic decision cases do not depend on #3588.

### Key Entities

- **Decision asset**: Stable tenant-scoped policy identity and current draft/publication references.
- **Decision draft**: Editable policy content, presentation and concurrency revision.
- **Decision version**: Immutable publication identity, source/content identity, profile/semantics and executable material.
- **Decision evaluation**: Frozen identity, structured outcome, result and actual rule explanation.
- **Decision case**: Named representative inputs and expected outcome with sensitivity and revision.
- **Decision test run**: Exact candidate and case snapshots with comparisons and protected history.
- **Compatibility report**: Located errors/warnings and independent preservation/edit/execution/publication eligibility.

## Success Criteria

- **SC-001**: The complete create → edit → validate → test → publish → reload journey passes in the normal Studio host.
- **SC-002**: Every case in the supported interoperability matrix preserves its configured meaning and expected results through both round-trip directions; every unsupported matrix case is explicitly refused for execution/publication.
- **SC-003**: The packaged direct caller, native workflow and BPMN workflow return equivalent expected results and rule explanations for the same frozen policy and input matrix.
- **SC-004**: Saved cases expose a deliberate approval-threshold regression and name the failed expectation and exact candidate tested.
- **SC-005**: Every documented concurrency, invalid-input, null/missing, policy, expression, dependency, budget and cancellation outcome has an executable witness.
- **SC-006**: The two-tenant permission matrix prevents every attempted cross-tenant or unauthorized operation and protects sensitive values in all tested surfaces.
- **SC-007**: A suspended/retried workflow still evaluates its original published policy after a newer decision version is published, with design storage unavailable.

## Assumptions and boundaries

The first release serves input-to-result tables, not fact inference, rule chaining or business actions. Full FEEL, full DMN conformance and full Camunda compatibility are not implied. The research and contracts choose an explicit interoperable subset. Broader types, dependent decisions and functions require a later profile rather than silent widening.

Research/specification/design and delivery are authorized by the current request. Sipke explicitly approved the separate Decisions domain and necessary constitutional amendment on 5 November 2026; [the approval receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3602#issuecomment-6670626201) authorizes admission and autonomous end-to-end implementation. Draft/provisional constitutional material is not ratified by this feature.

User Tasks #3577, regression suites #3588, releases #3590 and optional BPMN editor #3597 are adjacent consumers, not prerequisites or scope expansions. No release, deployment, vendor purchase or publisher activation is authorized.
