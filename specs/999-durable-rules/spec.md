# Feature Specification: Independently Packaged Durable Rule Execution Model

**Feature Branch**: `agent/codex/3181-durable-rules`

**Created**: 2026-10-21

**Status**: In progress — scope and implementation plan reviewed under the owner-delegated program authority; implementation and local native acceptance are on the branch, with exact-head PR review/CI and resulting-main gates still open for [#3181](https://github.com/elsa-workflows/elsa-foundation/issues/3181), under [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). See the [task list](tasks.md) and its evidence links for completed versus pending qualification.

**Input**: Owner-approved production fourth-model objective in Program #2892 and claimed implementation leaf #3181. The bounded finite fixed-point witness from research #2901 supplies the semantic example. Production lifecycle, packaging, persistence, native invocation, and provider acceptance are the delivery scope of this specification; the [task list](tasks.md) records current implementation and verification status.

## User Scenarios & Testing

### User Story 1 - Run a separately packaged rule model (Priority: P1)

A host integrator installs the Rules model as a deliberate deployment dependency and invokes a finite rule artifact directly. The model computes its declared fixed point, retains its own committed progress, and returns a stable result without the host referencing the model's implementation project or the shared workflow runtime selecting behavior by model name.

**Why this priority**: The production fourth model must be independently packaged and executable as its own semantic model. Agent collaboration is composition and does not prove this capability.

**Independent Test**: Restore and run the actual locally packed Rules artifacts in a clean consumer host with ordinary package references and no direct or transitive project reference to the engine implementation. Execute the retained finite witness and compare its final facts and round history with the declared result. An in-process source-host test alone is insufficient.

**Acceptance Scenarios**:

1. **Given** a supported installed Rules deployment and a valid finite model, **When** a direct invocation runs, **Then** the model reaches the same deterministic fixed point and terminal result as the required finite witness below.
2. **Given** a round adds no new facts, **When** that round is committed, **Then** the model succeeds; **Given** the final permitted round adds facts, **When** it is committed, **Then** the model ends with the defined round-budget fault rather than silently truncating or starting another round.
3. **Given** the deployment identity does not match the invocation's frozen semantic pin, **When** execution is attempted, **Then** it reports recoverable unavailability and leaves the invocation available for a compatible deployment.

### User Story 2 - Recover and cancel durable model work (Priority: P1)

An operator can restart or replace a worker without losing or duplicating the model's logical progress. Cancellation is an outcome of the Rules model itself, including when it races with admission or a stored-but-unconsumed result.

**Why this priority**: A separately packaged interpreter is not a production model unless its own decisions and terminal outcomes survive process loss and stale work.

**Independent Test**: Stop one real process between committed rounds and at a pending-receipt boundary, then resume the same invocation from a successor process using the same durable store and artifact pin, with mutable design storage unavailable to the successor. Exercise cancellation before admission and after a receipt is stored but before it is consumed, including a late predecessor response.

**Acceptance Scenarios**:

1. **Given** a round result has been accepted but not consumed, **When** a worker process exits and another claims the invocation, **Then** the successor observes that same pending result and either consumes it once or stops according to the terminal policy; it does not create a new logical round from it.
2. **Given** cancellation is recorded before admission, **When** the matching invocation is later admitted, **Then** the prior cancellation is replayed as the terminal model outcome; a different invocation fingerprint at the same scoped identity conflicts.
3. **Given** cancellation races with result consumption, **When** either transition wins first, **Then** already committed facts remain authoritative, unconsumed facts are not applied after cancellation, and no late result can resurrect the model or advance its parent.
4. **Given** a former worker returns after its ownership has expired or been replaced, **When** it submits work, **Then** that response cannot alter authoritative state or expose a successor's work.

### User Story 3 - Invoke the same model from persisted native workflows (Priority: P1)

A workflow author can use the packaged Rules model as a child of real persisted Sequence and Flowchart workflows. Native composites retain their existing order, sibling, and join semantics; after successful child completion the workflow continues normally, and fault or cancellation settles through the existing child boundary.

**Why this priority**: The production objective requires a model that integrates with Elsa while remaining a distinct execution domain. Its adapter must not replace native orchestration or the parent workflow's lifecycle authority.

**Independent Test**: Publish and run persisted Sequence and Flowchart examples through the rebuilt Workbench host. Both call the same Rules implementation as direct execution. Observe successful continuation into the following activity and fault/cancellation settlement, including Flowchart sibling/join behavior.

**Acceptance Scenarios**:

1. **Given** a persisted Sequence or Flowchart invokes Rules and the model succeeds, **When** the result is durably consumed by the parent, **Then** Sequence advances to its next activity and Flowchart continues according to its own join rules.
2. **Given** the model faults, is cancelled, or permanently refuses an invocation, **When** the parent observes that terminal outcome, **Then** the existing native child contract settles it once and no model-specific scheduling semantics leak into the composite.
3. **Given** the native parent is already terminal or its equivalent consumption is already durable, **When** Rules retries terminal delivery, **Then** the adapter can prove that state and retire delivery idempotently without changing the child outcome.
4. **Given** required Rules capability is temporarily unavailable, **When** the workflow attempts the invocation, **Then** the responsibility remains retryable and is not converted into a permanent model refusal or abandoned native delivery.

### User Story 4 - Upgrade and qualify the supported deployment (Priority: P2)

A host operator can add or roll back the Rules deployment while existing native workflows and admitted Rules runs retain their original meaning and remain recoverable by a capable deployment.

**Why this priority**: Safe adoption depends on storage-provider behavior and on preserving accepted model and native state across deployment changes.

**Independent Test**: Run the persistence invariant suite against SQLite, PostgreSQL, SQL Server, and MySQL. Preserve a native execution and an admitted Rules run, apply the additive schema/deployment change, and exercise both a compatible recovery and an incompatible successor followed by recovery using the retained compatible deployment.

**Acceptance Scenarios**:

1. **Given** existing native execution state predates Rules persistence, **When** the additive Rules schema is installed, **Then** the existing execution's state, identity, and continuation remain unchanged.
2. **Given** an admitted Rules run is pinned to one semantic implementation, **When** a successor lacks or mismatches that pin, **Then** the run remains unchanged and unavailable rather than reinterpreted or repinned; a compatible retained worker can complete it under its original pin.
3. **Given** the deployment is rolled back, **When** new Rules admission is disabled, **Then** existing accepted responsibilities remain readable and recoverable by capable readers/workers; rollback does not destructively convert or abandon active state.
4. **Given** any required database provider is unavailable during qualification, **When** the provider suite is evaluated, **Then** the provider gate is reported as unverified/failing rather than passed by skip.

### Required finite witness

Start with `{seed}` and five uniquely named rules: `seed -> left`, `seed -> right`, `left AND right -> joined`, `joined -> final`, and `unreachable -> never-derived`. One committed round adds `{left,right}`, the next adds `{joined}`, the next adds `{final}`, and the fourth adds nothing and proves completion. Final facts are exactly `{final,joined,left,right,seed}` in ordinal canonical order. Both `unreachable` and `never-derived` remain absent. Every receipt lists all applicable rules, including those whose conclusion was already present; additions contain only new facts.

This preserves the semantic witness from [research #2901](https://github.com/elsa-workflows/elsa-foundation/issues/2901), pinned source `4b464e1637483e89a11f632f2c99c5779cb4e0de`, `tools/experiments/UnifiedExecution.RuleModelProof/Assets/finite-rules.artifact.json`. Its historical prototype is not production acceptance evidence. With `MaxRounds = 3`, the third round commits `final` and terminates Faulted with `rules.round-budget-exhausted`; the same facts are explicitly non-converged because the no-addition round was not committed. With `MaxRounds = 4`, the fourth round succeeds. Nonpositive limits are invalid invocation input and create no Rules run. A waiting native parent still receives its normal durable dispatch-refusal outcome once its committed destination is proved; no child record is fabricated. The authoring/direct facade resolves an omitted limit to the absent finite-vocabulary count plus one, sufficient for all additions and closure, before freezing the invocation and its fingerprint. That captured positive value is reused on retry and recovery, never recomputed against a later design or deployment. An explicit positive lower limit is permitted and follows the same fault rule.

### Edge Cases

- A null tenant identifier is an exact scope value, not a wildcard or fallback; same invocation key with matching immutable identity replays, while changed identity conflicts.
- Cancellation can precede admission and should not require the old interpreter to understand the run's semantic schema; an envelope that cannot prove its scope/key/destination is invalid and must not invent a run.
- A deterministic permanent Rules-level invalidity is refused only when established by a compatible interpreter. Missing package, unsupported capability, or package/state/schema mismatch is recoverable unavailability with no refusal tombstone.
- A receipt may be equivalent despite different physical attempt identifiers; retain the winning attempt attribution, prevent conflicting receipts, and do not apply the same logical result twice.
- Omitted applicable conclusions, duplicate/noncanonical facts, stale ownership, wrong invocation, and changed input state must not mutate committed progress.
- A lease expiry does not prove that physical computation stopped; any late result from a replaced owner remains unable to commit.
- Empty/no-new-fact completion and a last-round result that still adds facts have distinct terminal outcomes.
- Parent cancellation or termination may retire owed native delivery only when the adapter has durable proof; it does not rewrite an already terminal child result.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST provide a bounded deterministic fixed-point language over propositional facts. Each round evaluates all applicable rules against one frozen input state, and its accepted result identifies the complete set of firings and conclusions. Rules have unique identifiers, positive conjunctive premises and one conclusion. Variables, terms, bindings, negation, retraction, arbitrary code, and a general-purpose rules product are outside this feature.
- **FR-002**: The Rules model MUST be independently deployable as actual package artifacts through the supported host's normal package composition. The clean acceptance host MUST run those artifacts without a project reference, including a transitive project reference, to the engine implementation. The shared runtime and host MUST NOT gain model-name-specific branches.
- **FR-003**: Each invocation MUST bind an exact scope, stable invocation identity, immutable canonical model content, initial facts, round limit, and semantic implementation/schema pin. Model content identity, semantic implementation identity, package-archive provenance, and execution-environment provenance MUST remain distinguishable. Deployment changes MUST NOT rewrite an admitted invocation's identity or pin. Execution and recovery MUST require only the frozen runnable artifact and runtime configuration; mutable draft, history, layout and design storage MUST be unavailable during the recovery acceptance case.
- **FR-004**: The system MUST persist each model transition and its owed work/result atomically in the Rules execution domain. Accepting a round result and consuming it are distinct transitions: acceptance stores one complete immutable result; consumption applies it at most once and either starts one next round or settles a terminal result.
- **FR-005**: The system MUST use exact scope and immutable invocation identity to make admission, cancellation, refusal, and replay idempotent. Reuse with the same identity returns the existing disposition; a different fingerprint for the same scoped key conflicts without changing existing state.
- **FR-006**: The system MUST provide durable model-owned cancellation before admission/dispatch and during execution. Cancellation and round consumption MUST serialize so that committed facts are preserved, unconsumed conclusions are not applied after cancellation, and terminal outcomes cannot be overwritten by late work. Cancellation before admission returns no admitted facts; cancellation after admission retains initial and committed facts, including initial facts when no round has yet committed.
- **FR-007**: The system MUST distinguish invalid input, permanent model refusal, and recoverable unavailability. Permanent refusal requires a structurally valid invocation and a deterministic permanent model-level reason established by a compatible interpreter. Unsupported local capability, package identity, or schema MUST remain recoverably unavailable without mutating or refusing the invocation.
- **FR-008**: Work ownership MUST fence stale workers from accepting or consuming results. Process replacement MUST recover the same logical invocation and pending result without duplicating logical advancement. The system MUST NOT claim a lease timeout proves that physical work stopped.
- **FR-009**: The system MUST expose exact-scope, bounded inspection of run status, committed progress, result/receipt identity, terminal outcome, and safe causal details. Progress MUST distinguish accepted-pending work from committed facts and MUST avoid secrets or unbounded payloads in routine views.
- **FR-010**: Direct execution and native workflow invocation MUST use the same packaged model semantics. The native adapter MUST preserve the existing durable child/checkpoint/outbox/bookmark boundary, provide result facts and terminal outcomes to the parent, and acknowledge delivery only after durable proof of parent consumption, equivalent prior consumption, or parent terminal state. Rules state and native checkpoint state are separate authorities; no cross-store atomic transaction is assumed.
- **FR-011**: Persisted Sequence and Flowchart parents MUST retain their existing ordering and join semantics while invoking Rules. Rules success, fault, cancellation, refusal, and temporary unavailability MUST settle or preserve parent responsibility through the existing native integration seam without introducing a second orchestration authority or actor/membership service. The model MUST NOT allocate an actor per rule or node.
- **FR-012**: The Rules persistence implementation MUST qualify its required transition and concurrency invariants on the first-party SQLite, PostgreSQL, SQL Server, and MySQL providers, including admission races, conditional update/fence races, scoped identity, delivery, and additive schema coexistence.
- **FR-013**: Schema and deployment evolution MUST be additive for existing native executions. An incompatible successor MUST leave an admitted run under its original identity and pin for recovery by a compatible deployment. Rollback MUST stop new admission while preserving the ability to read and settle accepted responsibilities.
- **FR-014**: The delivery MUST include scoped author/integrator guidance for the actual supported package and runtime profile, recovery behavior, inspection limits, and known exclusions. It MUST NOT add a full operator console or unrelated UI as part of this feature.
- **FR-015**: Existing native and ETL behavior, architecture/filter rules, focused meaningful removal controls, reviewed exact-head checks, and resulting-main verification MUST pass before this leaf is considered delivered. The controls MUST demonstrate failure with a relevant safeguard removed and success after exact restoration.

### Key Entities

- **Rule artifact**: Immutable canonical finite model content, its content identity, language/canonicalization version, and initial bounded execution policy.
- **Rule invocation**: One exact scoped request to execute an artifact, with stable identity, semantic implementation pin, persisted model status, and committed progress.
- **Round result**: A complete deterministic account of one logical evaluation over a frozen input state, including its firing/conclusion evidence and separate physical-attempt attribution.
- **Terminal result**: The immutable success, round-budget fault, cancellation, or permanent refusal outcome and the committed facts the parent or direct caller may observe.
- **Native delivery**: The owed terminal result for one frozen native destination, retired only after durable parent-consumption or terminal proof.
- **Deployment receipt**: Trusted evidence identifying the actual package deployment and selected semantic assets separately from model content and runtime-environment provenance.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A clean PackageReference-only consumer restores and executes the actual packed Rules artifacts without any project reference to the engine implementation; the finite witness produces its declared result and inspectable committed progress.
- **SC-002**: Package acceptance records the actual package identities and selected runtime assets and verifies the semantic implementation pin against the loaded deployment. Model-content hash, NuGet content identity, raw archive digest, and runtime provenance are reported as distinct evidence; a manifest hash or source revision alone does not satisfy this criterion.
- **SC-003**: The complete persistence invariant matrix passes on SQLite, PostgreSQL, SQL Server, and MySQL with required providers present. Missing provider execution is recorded as unverified/failing, never as a pass.
- **SC-004**: Separate predecessor/successor processes recover the same run across both a committed-round boundary and a pending-result boundary, with mutable design storage unavailable, no duplicate logical advancement, no loss of the winning result attribution, and no acceptance from a stale owner.
- **SC-005**: Direct invocation and persisted Sequence/Flowchart invocation run the same packaged semantics. Persisted parents continue after successful completion; success, fault, cancellation, refusal, and temporary unavailability retain their declared native outcomes, and existing Flowchart sibling/join behavior remains intact.
- **SC-006**: Cancellation-before-admission and cancellation-after-accepted-but-unconsumed-result tests pass; a matching later admission replays cancellation, a changed fingerprint conflicts, committed facts are retained, and late work cannot change model or parent state.
- **SC-007**: Permanent refusal, malformed/invalid request, and unsupported/mismatched package or schema are separately demonstrated. Invalidity/refusal/unavailability each produce their declared no-mutation or durable-terminal behavior.
- **SC-008**: Additive upgrade leaves an existing native execution unchanged; an incompatible successor cannot repin or reinterpret an admitted Rules run, a compatible retained deployment can resume it, and rollback preserves accepted responsibilities while stopping new admissions.
- **SC-009**: Exact-scope bounded progress inspection reports accepted versus committed state and safe causal progress without exposing secrets or unbounded receipts; no extra operator UI is required.
- **SC-010**: Focused removal controls fail when cancellation identity, worker fencing, complete round validation, one-time receipt consumption, package identity verification, or native proof-before-acknowledgement is removed, and pass again after exact restoration. The repository compatibility, architecture/filter, review, exact-head, and resulting-main gates are recorded with skips and limitations stated.

## Assumptions

- The owner-approved production goal is a distinct fourth semantic model. This spec does not reopen research #2901 or claim that agent collaboration is the fourth model.
- The initial language is finite, deterministic, positive propositional fixed-point evaluation only. No variables, terms, bindings, user-authored general language, or optimizer is included.
- Host operators deliberately install trusted local packages. This feature does not download arbitrary code at runtime, establish publisher authenticity, or provide a marketplace.
- Rules owns its persisted model transitions, receipts, cancellation, and terminal outcome. The native adapter owns proof of parent delivery using the existing checkpoint/outbox/bookmark contracts. Native composites continue to own their scheduling and joins.
- The initial package qualification is a conventional file-based, framework-dependent PackageReference consumer on the repository's stable .NET 10 servicing line, with verifiable on-disk selected assets. Nuplane-loaded modules, single-file, trimmed, ReadyToRun, custom load contexts, and general deployed-image qualification need separate evidence.
- Four first-party EF providers are part of acceptance: SQLite, PostgreSQL, SQL Server, and MySQL. Same-machine database/process evidence does not imply separate physical machines, arbitrary actor-provider qualification, or power-loss testing.
- The feature adds bounded progress/inspection and practical author/integrator documentation, not a full operations console. Task-runner/pause, actor qualification, variable-rule semantics, ETL/BPMN expansion, and broad product/presentation acceptance remain outside this unit.
- This specification consolidates the reviewed direction for claimed #3181. It is not compiled API or acceptance evidence. The domain admission, detailed plan, contracts and executable acceptance entry points must be reviewed before delegated implementation readiness. The prior source audit found the existing CLR activity publication path sufficient without waiting for #2891/#1785. Planning must refresh that overlap and record concrete reuse/adoption before any capability-registration change; no competing registration implementation is authorized by this specification.
