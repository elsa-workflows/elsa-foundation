# Feature Specification: Final JTest acceptance for Elsa 3 to Elsa 4 conversion

**Feature Branch**: `agent/codex/3052-jtest-final-acceptance`

**Status**: Draft

**Maturity**: Provisional intake draft. Corpus completeness, baseline, scenario mappings and harness choices are not settled. This document is not approved to implement or an acceptance result.

**Input**: Establish execution parity for the complete supplied JTest-Nexxbiz suites and workflow definitions, with explicit conversion dispositions and faithful arrange/assertion adaptations. Deliver a separately named non-JOIN milestone while retaining eventual JOIN acceptance in this specification.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish the complete acceptance corpus (Priority: P1)

As the acceptance owner, I can identify every supplied suite, definition version and supporting asset, establish a reproducible Elsa 3 baseline, and see omissions before results are reported.

**Why this priority**: A subset of exported definitions cannot establish suite completeness or execution parity.

**Independent Test**: Reconcile a read-only inventory with the owner's expected suite/version manifest and resolve every referenced asset. A missing fixture or unknown version keeps intake incomplete.

**Acceptance Scenarios**:

1. **Given** a supplied folder, **When** intake inventories it, **Then** suites, cases, definition versions, dependency edges and arrange/action/assertion assets are reconciled against an explicit completeness declaration; discovered files alone do not prove completeness.
2. **Given** duplicate versions, missing reusables, unresolved templates or absent environment bindings, **When** the inventory is reviewed, **Then** each ambiguity remains visible with its affected cases and no fabricated acceptance denominator.
3. **Given** private assets or secret-bearing configuration, **When** public documentation is prepared, **Then** only approved safe metadata or invented regression fixtures enter public commits; originals, private identifiers and secrets remain local.

### User Story 2 - Classify conversion without losing original intent (Priority: P1)

As a workflow owner, I can distinguish definitions that convert unchanged, definitions that need a precise adjustment, and definitions that cannot convert faithfully.

**Why this priority**: A converter refusal and an execution-parity result answer different questions.

**Independent Test**: Trace each classification through the unchanged original, selected dependency versions, converter receipt and semantic case. For an adjustment, inspect its exact patch and rationale; for an incompatibility, inspect its stable explanation and refusal evidence.

**Acceptance Scenarios**:

1. **Given** an unchanged source definition with its complete dependency closure, **When** it imports, publishes and passes the translated semantic assertions, **Then** it may receive execution-parity evidence under `convert-as-is`.
2. **Given** a necessary adjustment, **When** an adjusted copy is accepted, **Then** the original remains preserved, the precise change and reason are recorded, and parity applies only to the explicitly identified semantic contract and variant. A changed business outcome cannot be described as parity with the original.
3. **Given** an incompatible construct, **When** conversion is refused safely, **Then** the result records the diagnostic, violated semantic requirement and guidance, with publication/execution not run. It does not count as execution parity.
4. **Given** a missing host feature or fixture, **When** a case cannot run, **Then** it remains blocked for that reason rather than being relabelled incompatible merely to clear the milestone.

### User Story 3 - Run equivalent non-JOIN scenarios (Priority: P1)

As the acceptance owner, I can run each eligible semantic scenario against pinned Elsa 3 and Elsa 4 hosts and compare its intended observations, including waits and resumes.

**Why this priority**: Import, publication and a no-input execution probe do not prove the behavior a JTest case asserts.

**Independent Test**: On isolated stores, execute the same agreed inputs and stimulus schedule through verified engine-specific arrange/action/assertion mappings. Check typed results, relevant intermediate states, side effects and cleanup against the baseline contract.

**Acceptance Scenarios**:

1. **Given** different engine APIs, **When** an arrange or assertion is translated, **Then** its original assertion identity, meaning, observation source and Elsa 4 equivalent are traceable; changing an endpoint or JSON path does not remove a semantic assertion.
2. **Given** a scenario that should wait for an event or input, **When** it reaches the expected wait, **Then** acceptance observes the correct wait and correlation, supplies the required stimulus, and asserts the prescribed resume path and terminal or continued-wait state. A completion timeout is not substituted for that journey.
3. **Given** generated IDs, timestamps or nondeterministic ordering, **When** results are compared, **Then** only previously justified field-specific normalization applies; reference identity, causal order, type, null/missing distinctions, payloads and required counts remain checked.
4. **Given** an unexpected incident or deadline expiry, **When** a run ends, **Then** the attempt remains failed, timed out, blocked or inconclusive as appropriate; a later successful repeat does not erase it.

### User Story 4 - Complete deferred JOIN acceptance (Priority: P2)

As the acceptance owner, I can reach a useful non-JOIN milestone without losing the remaining JOIN-dependent cases or mistaking that milestone for final completion.

**Why this priority**: JOIN behavior is outside the initial executable scope but remains necessary for full acceptance.

**Independent Test**: Reconcile all JOIN-affected cases and dependency paths with the eventual agreed JOIN contract, execute their paired scenarios and close the specific deferred tasks below with reviewed evidence.

**Acceptance Scenarios**:

1. **Given** explicit, implicit or nested JOIN behavior, **When** intake computes affected cases, **Then** each case and every transitive caller or shared setup depending on that behavior is marked deferred with the dependency path. A case whose closure is not established cannot enter the non-JOIN run set.
2. **Given** a successful non-JOIN milestone with open JOIN tasks, **When** status is reported, **Then** the milestone is named non-JOIN acceptance and this specification remains nonterminal.
3. **Given** completion of the separate JOIN work, **When** final acceptance is considered, **Then** this specification still requires its own current-head JOIN qualification and closure evidence; external issue closure alone is insufficient.

### Edge Cases

- Several suite exports refer to different versions of the same definition; dependency selection must be explicit rather than silently choosing the newest.
- Reusable cycles, missing references, indirect dispatch targets, dynamically resolved calls or shared arrange workflows make a closure uncertain.
- Several incoming paths converge without an explicit FlowJoin; JOIN detection cannot rely on activity names alone.
- A trigger starts a new instance when a scenario expected a resume, or resumes the wrong correlated instance.
- A wait is legitimate but an incident is unexpected; neither `Running` nor absence of completion proves a healthy wait.
- Observation is eventually consistent, a response is a preview, or the selected persistence/capture policy does not expose the value an assertion needs.
- A baseline case fails, is flaky, depends on unavailable external services, or has no expected result. Its acceptance contract stays unresolved.
- Cleanup fails after a successful assertion or a rerun sees stale publications, subscriptions or persisted state.
- An adjusted dependency changes several callers; the patch, bucket impact and rerun closure include all of them.
- Privacy filtering would remove an assertion's only evidence. Preserve the complete evidence privately and identify the public evidence limit.

## Requirements *(mandatory)*

### Functional Requirements

**Corpus and baseline**

- **FR-001**: Intake MUST first establish the absolute local folder location, readiness, source/export identity and owner's completeness declaration for ALL JTest-Nexxbiz material. The historical 36-definition export MUST NOT be substituted for the complete suite.
- **FR-002**: The inventory MUST cover suite/runner/schema versions; cases; original definition identities and versions; reusable and invoked dependencies; arrange/actions/assertions; fixtures; request templates; environment files and bindings; credential requirements by reference only; expected results; triggers/events; setup/reset/cleanup; and baseline host configuration. Counts and hashes MUST reconcile with the declared material, including missing and ambiguous items.
- **FR-003**: Originals MUST remain unchanged and identifiable by private content hashes. Adjusted definitions and translated suites MUST be separate derived assets with traceable lineage. Public metadata MUST use approved safe case aliases; file names, local paths, hashes or IDs that disclose private material MUST remain private unless explicitly cleared.
- **FR-004**: Each semantic case MUST map to a reproducible Elsa 3 baseline and an Elsa 4 equivalent: exact engine and host sources/versions, modules/custom activities, catalog and converter revision, selected definitions, persistence/provider, capture/diagnostic settings, authentication requirements, clock/time zone/culture, external services, startup/readiness and store-reset conditions. Differences MUST be explicit. Baseline failures MUST remain unresolved until understood, not become candidate passes.

**Definition dispositions**

- **FR-005**: At intake closure every supplied definition version MUST have exactly one final bucket: `convert-as-is`, `convert-with-adjustments`, or `incompatible`. Classification is provisional until justified; unresolved intake is a state, not a fourth final bucket. Every suite case MUST link all definition versions it exercises. JOIN scope and execution status MUST be separate fields from the bucket.
- **FR-006**: `convert-as-is` MUST mean the workflow source is unchanged. Translation of the harness's arrange/action/assertion API calls does not itself move a definition into the adjustment bucket.
- **FR-007**: `convert-with-adjustments` MUST retain the original and precise patch, source/target hashes, reason, affected dependencies/callers, preserved semantic assertions and any intentional behavior difference. The coordinator MUST agree the adjustment before it is used for acceptance. Verify the original Elsa 3 baseline and the adjusted variant's relationship to it; where the adjustment can execute in Elsa 3, run both against the preserved assertions. Otherwise record the limitation and agree the equivalence evidence before claiming parity. Behavior changes require an explicit revised acceptance contract; neither weakened assertions nor unrecorded source edits are allowed.
- **FR-008**: `incompatible` MUST carry a stable reason identifier, semantic explanation, diagnostic/code and observed conversion stage, guidance and dependency impact. An input that cannot convert faithfully MUST be refused safely rather than produce a runnable artifact claiming equivalence; affected dependents MUST retain the root cause. Distinguish inherent incompatibility from a currently unimplemented capability and name the condition for reconsideration. Missing material, credentials or host setup MUST be reported as blocked intake/run prerequisites rather than semantic incompatibility.
- **FR-009**: Each run MUST distinguish conversion/import, publication, execution and assertion outcomes. A safe refusal is useful converter evidence but MUST NOT contribute to an execution-parity numerator. Import or publication alone MUST NOT establish parity. Changes from convertible to refused MUST be visible as such even if the refusal is safe.

**Semantic translation and observation**

- **FR-010**: Every case MUST retain stable assertion identities and map its arrange, action, observations and cleanup from Elsa 3 to Elsa 4, including requests, payloads, URLs, response/body paths, status semantics, captured identifiers, external effects and expected failures. API contracts MUST be inspected and verified on the pinned hosts; old API calls MUST NOT be blindly reused. Representative translation/normalization controls MUST demonstrate that a meaningful wrong value, state or routing result is still detected by the preserved semantic assertions.
- **FR-011**: Intake MUST evaluate shared semantic scenarios with engine-specific request/assertion templates and environment variables for genuine differences. Select a shared rendering approach or separately maintained engine assets with shared case/assertion identities only after verifying runner capabilities and representative cases with the coordinator. Parameters MUST NOT disable assertions, conceal errors or replace expected semantic outcomes per engine just to pass.
- **FR-012**: For waiting/resume scenarios, the case contract MUST specify start versus resume, readiness and wait conditions, bookmark/event/correlation matching, stimulus payload and schedule, negative observations where relevant, resume multiplicity, expected final or continued-wait state, polling cadence and bounded deadlines. An unsolicited no-input probe MUST NOT replace the scenario.
- **FR-013**: Every normalization MUST identify fields, reason, rule and an example that would still fail for a meaningful mismatch. ID normalization MUST preserve identity/reference relationships; time normalization MUST preserve required offsets, chronology and durations; order normalization MUST apply only to a contractually unordered collection and preserve membership, multiplicity and causal ordering elsewhere. No blanket removal of IDs, time, order, casing, numeric precision, nulls, missing values or undefined behavior is allowed.
- **FR-014**: Assertions MUST observe the full intended semantic value and state through an adequate source. Preview text, case-insensitive lookup or a UI summary MUST NOT silently replace an assertion on exact JSON keys, types or values. A translated assertion that cannot yet be observed stays blocked pending an agreed observation method.
- **FR-015**: Fresh, isolated local runs MUST execute the actual arrange/action/assertion journey and setup/reset/cleanup, not just imported definitions. Startup conversion/reconciliation and collection import MUST both be represented in the agreed coverage matrix, including reusable dependency publication and refusal propagation. Their per-case coverage and restart/idempotency checks MUST be settled during intake without duplicating the converters or adding Elsa 3 runtime compatibility to Elsa 4.

**JOIN boundary and prerequisites**

- **FR-016**: The initial executable acceptance scope MUST exclude JOIN behavior. Intake MUST inspect explicit joins, implicit graph convergence, fan-in, per-arrival, wait-any/all/active, cancellation/race and loop re-entry dependencies, including nested reusables, dispatched workflows and shared arrange/assertion workflows. Record both the direct affected definitions/cases and their complete transitive dependency paths; uncertain closures remain outside the non-JOIN run set.
- **FR-017**: JOIN cases MUST remain inventoried with their intended baseline assertions, provisional bucket and deferral reason. Deferral MUST NOT be implemented by deleting branches, replacing a join with a simpler control flow, weakening assertions, or treating all deferred cases as incompatible. An independently isolated subcase may enter the non-JOIN milestone only after its own dependency closure and semantic contract are justified.
- **FR-018**: The deferred JOIN tasks J-001 through J-005 below MUST remain OPEN in this specification until their individual closure evidence exists. The separate JOIN initiative owns JOIN decisions and implementation; this specification owns the corpus acceptance proof. A successful non-JOIN milestone MUST NOT mark this specification `Implemented` or declare the conversion initiative fully complete.
- **FR-019**: Before acceptance qualification, verify the live delivery, integration and relevant local evidence for S-2 null/missing/undefined policies (spec 1232) and S-9 serialization integrity (spec 1207). Record exact commits, configuration and unresolved boundaries through the initiative board. Prior technical repairs, approved specs or unrelated green checks MUST NOT substitute for those implementations. Their worktrees and owned implementation scope remain separate.

**Evidence and lifecycle**

- **FR-020**: Each attempt MUST retain the corpus fingerprint, baseline/candidate and harness sources, environment/configuration fingerprint without secret values, selected versions/closures, effective templates and normalization rules, timestamps/deadlines, raw observations, assertion results, incidents, cleanup result and reason for any blocked/not-run step. Failed attempts and repeats MUST remain separately traceable; a later head requires newly scoped qualification.
- **FR-021**: Public receipts MUST contain only safe metadata and accurate evidence limits. Complete private evidence and secret values MUST stay outside public commits/uploads; credentials MUST use local references or secure environment bindings. Invented public fixtures MAY demonstrate a regression but MUST NOT be represented as the private corpus or its parity result.
- **FR-022**: Results MUST reconcile total supplied suites, cases and definition versions with the three buckets, executable eligibility, JOIN deferrals and blocked/not-run cases. Report import/publication/refusal counts separately from semantic execution/assertion counts. Publish no pass-rate headline that treats natural waits, missing stimuli or safe refusals as execution successes or failures without the case contract.
- **FR-023**: Intake MUST be agreed before this draft advances to the reviewed Spec Kit plan, design contracts and dependency-ordered tasks; substantive harness or converter implementation MUST NOT start before specification agreement. Plan/tasks MUST retain the same deferred JOIN obligations. A new architecture decision MUST follow its own agreement path rather than being silently decided by an acceptance adapter.
- **FR-024**: Work implemented under this objective MUST undergo review, verified fixes and renewed review until no verified findings remain, retaining failed attempts and honest local evidence. CI is deferred to the initiative's later gate; this work MUST NOT wait for CI, claim CI passed, or change workflows/protections to obtain acceptance.

### Key Entities

- **Corpus snapshot**: the private immutable source set, runner/suite versions, completeness declaration and inventory reconciliation.
- **Definition variant**: original definition/version and closure, final conversion bucket, optional adjusted copy and exact patch or incompatibility explanation.
- **Semantic case**: stable safe alias, definition variants, ordered arrange/stimulus/observe/assert/cleanup contract, original and translated assertion identities.
- **Engine binding**: verified per-engine request/observation templates, environment references, host identity and genuine differences.
- **JOIN deferral**: affected case, direct JOIN construct or transitive dependency path, baseline semantics and open acceptance obligation.
- **Attempt receipt**: exact sources/configuration, stage outcomes, raw private evidence, safe public metadata and failed/repeated-attempt lineage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 — Intake agreed**: 100% of the owner-declared suite/case/definition-version set and referenced assets are reconciled; every gap, baseline identity, API/assertion mapping choice and observation/normalization rule needed for execution is resolved or explicitly blocks advancement. Discovery alone cannot satisfy this criterion.
- **SC-002 — Dispositions complete**: Every supplied definition version has exactly one justified bucket. Every adjusted variant has its preserved original, exact approved patch and reason; every incompatible variant has a stable semantic explanation and verified safe disposition. No unclassified definition is hidden by the totals.
- **SC-003 — Non-JOIN acceptance milestone**: Every agreed eligible non-JOIN semantic case has passing paired baseline/candidate assertions and successful cleanup on the qualified source, with zero unresolved mismatches or blocked/not-run required cases in that milestone. Refusal coverage, incompatibilities and all deferred JOIN cases are reported separately. This is a milestone, not full completion.
- **SC-004 — Full acceptance**: SC-001 through SC-003 remain true on the final qualified integrated source, every JOIN obligation J-001 through J-005 is closed with current evidence, and every inventoried case has its agreed terminal acceptance disposition. Any intentional incompatibility remains expressly distinguished from execution parity; no unresolved JOIN or other required case is waived by this criterion.
- **SC-005 — Evidence trustworthy**: Every claimed result resolves to its exact source, corpus and assertion set; every failed/repeated attempt remains traceable; no private corpus content or secret is included in public changes. Review of implemented work leaves zero verified unresolved findings.

## Deferred JOIN tasks retained in this specification

These tasks are OPEN. They are acceptance work to carry into the later Spec Kit tasks; this table neither begins JOIN implementation nor substitutes for the reviewed plan/tasks workflow.

| ID | State | Task and required closure evidence |
|---|---|---|
| J-001 | OPEN | Enumerate all direct and implicit JOIN cases and full transitive dependency closures, including shared setup and reusable/dispatch paths; reconcile the deferred case ledger with the complete corpus. |
| J-002 | OPEN | Reconcile those cases and original assertions with the separate JOIN work's agreed contract and integrated implementation; identify temporary versus inherent incompatibilities and obtain agreement for any precise adjustment. |
| J-003 | OPEN | Translate and qualify deferred JOIN arrange/actions/assertions, including arrival cardinality, waits, resumes, re-entry and cancellation behavior actually present in the supplied cases; retain the original assets and approved observation/normalization rules. |
| J-004 | OPEN | Run paired Elsa 3/Elsa 4 JOIN cases on the qualified integrated head and host configuration; retain stage, execution, assertion and cleanup outcomes, failures and meaningful negative controls. Fix verified findings and review again. |
| J-005 | OPEN | Reconcile the complete case/disposition ledger and renewed non-JOIN evidence with JOIN results, close every required acceptance gap with reviewed evidence and record the coordinator's full-acceptance verdict before a terminal spec status. |

## Dependencies and boundaries

- [Startup reconciliation, spec 235](../235-elsa3-json-reconciliation/spec.md) provides conversion/import on startup. Its original unchanged-suite ambition is refined here by explicit harness API adaptations; no amendment to its converter implementation is implied by this draft.
- [Mapping rules](../../Extensions/Elsa3/src/Mapping/README.md) and [migration readiness](../../docs/reference/elsa3-migration-readiness.md) supply candidate diagnostics and known constraints. Case-level classification must verify the current code and host; historical readiness prose is not a measured corpus result.
- S-2/spec 1232 and [S-9/spec 1207](../1207-json-data-integrity-baseline/spec.md) are separate prerequisite delivery scopes. The live board and receipts hold their status; this spec does not implement them.
- JOIN decisions/implementation remain in the separate JOIN initiative; routing and live links belong to the initiative board and proposal description. Their acceptance remains here as J-001 through J-005.
- [Framework constitution](../../.specify/memory/constitution-framework.md) sections 2.21 and 2.23 require faithful behavior and risk-based tests. [Elsa constitution](../../.specify/memory/constitution.md) E2.7 keeps Elsa 3 behind the import boundary: two isolated baseline/candidate hosts, not two engines in one process. Activity versioning/Model X and E2.9 are provisional; this specification does not ratify or extend them.
- Runtime instance migration, broad feature implementation, JOIN design, workflow/protection changes and initiative landing are outside this initial drafting unit.

## Assumptions

- The complete corpus is external and private; no file or suite count is assumed until the owner confirms completeness.
- A baseline case expresses intended behavior only after its assertions and host context are inspected together. Existing native concept scripts and historical no-input probes are research inputs, not baseline parity evidence.
- Intake questions and concrete choices are maintained in [intake.md](intake.md) and the draft proposal discussion. Plan/design/tasks generation follows agreement rather than inventing answers in this draft.
