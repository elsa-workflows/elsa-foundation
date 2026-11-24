# Feature Specification: Final JTest acceptance for Elsa 3 to Elsa 4 conversion

**Feature Branch**: `agent/codex/3052-jtest-final-acceptance`

**Status**: Draft

**Input**: Prove the Elsa 3 to Elsa 4 conversion against the complete JTest corpus: the Elsa 3 workflow definitions are
imported and converted by startup reconciliation, then the JTest suites written for those definitions run against
Elsa 4 and pass. The suite lives in the Elsa 3 converter area as a lasting workflow-regression suite. Work that JOIN
behaviour blocks stays in this specification and is done after the separate JOIN initiative delivers.

## Rule and boundary

The JTest suites are the baseline. A suite assertion that passes on the pinned Elsa 3 server records what Elsa 3 does;
the same assertion passing on Elsa 4, after conversion, is the acceptance evidence. No Elsa 3 host runs next to Elsa 4,
and no assertion is weakened to make Elsa 4 pass.

The work runs in three phases, each gated on the previous one:

1. **Baseline**: the corpus runs green on the pinned Elsa 3.6.3 server, after classified and approved suite fixes.
2. **Engine-neutral suites**: Elsa 3-specific values move out of the suite files into templates, environment or
   suite parameters, while the suites stay green on Elsa 3.
3. **Elsa 4**: an Elsa 4 template set with the same template names, parameters and outputs; the definitions load
   through startup reconciliation; every suite not blocked by JOIN behaviour, value capture or a 3.6.3 defect, and not carrying an approved
   incompatibility, passes on Elsa 4 (SC-004). Suites blocked by JOIN behaviour or value capture follow once
   their initiatives deliver.

**Example.** `if-then-else-tests.json` posts to the `qa/branching/flows` endpoint with `ifConditionValue: true` and
asserts that the `if` and `if_Then_WriteLine` activities both completed and `if_Else_WriteLine` never executed. The
suite asks the `elsa-activity-instance-*` templates for those facts. On Elsa 3 the templates call the Elsa 3 REST API;
on Elsa 4 the same template names call the Elsa 4 API and return the same outputs. The suite file and its expected
values do not change between engines.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A green Elsa 3 baseline (Priority: P1)

As the coordinator, I can run the complete corpus against the pinned Elsa 3 server and see every suite pass, or see why
it may not, before any conversion work relies on it.

**Why this priority**: A suite that fails on Elsa 3 cannot tell a conversion defect from an old test defect.

**Independent Test**: Start the pinned Elsa 3 server, import the definitions with the corpus's own setup suite, run
`jtest run` over every suite and read the result per suite.

**Acceptance Scenarios**:

1. **Given** the pinned Elsa 3 server and the unchanged corpus, **When** every suite runs, **Then** each failing
   assertion is classified as a suite defect, a behaviour the pinned version corrected, or a defect of the pinned
   version, with evidence and the suite's own description.
2. **Given** the classified list, **When** the coordinator approves it, **Then** suite defects and corrected behaviours
   are fixed in the suite with a stated reason per change, and suites hit by a defect of the pinned version are marked
   blocked with that defect named.
3. **Given** the fixed corpus, **When** it runs again on the pinned server, **Then** every suite not marked blocked
   passes.

### User Story 2 - Engine-neutral suites (Priority: P1)

As a suite author, I can run one suite file against either engine, because everything engine-specific lives in a
template, the environment file or a suite parameter.

**Why this priority**: The suite files carry the expected behaviour. Keeping them identical across engines is what
makes an Elsa 4 pass mean the same thing as an Elsa 3 pass.

**Independent Test**: After the refactor, the suites still pass on the pinned Elsa 3 server, and a diff of each suite
file shows only values moved behind a parameter, never a changed expectation.

**Acceptance Scenarios**:

1. **Given** a suite that hard-codes an Elsa 3 API path, response field or status value, **When** it is refactored,
   **Then** that value comes from a template output, the environment file or a suite parameter, and the suite's
   assertions and expected business values are unchanged.
2. **Given** the refactored corpus, **When** it runs on the pinned Elsa 3 server, **Then** the same suites pass as
   after User Story 1.

### User Story 3 - Converted definitions pass on Elsa 4 (Priority: P1)

As the coordinator, I can start an Elsa 4 host that converts the Elsa 3 definitions at startup and run the suites
against it with the Elsa 4 template set.

**Why this priority**: This is the acceptance proof for the initiative.

**Independent Test**: Start a fresh Elsa 4 host with the Elsa 3 startup reconciliation pointed at the corpus's
definitions, read the reconciliation receipt, then run `jtest run` over every suite with the Elsa 4 templates and
environment.

**Acceptance Scenarios**:

1. **Given** a fresh Elsa 4 host, **When** it starts, **Then** every definition is either imported and published or
   refused with its diagnostic, and the receipt matches each definition's recorded bucket.
2. **Given** a suite whose definitions converted, **When** it runs with the Elsa 4 templates, **Then** every assertion
   passes, with the suite file unchanged from User Story 2.
3. **Given** a suite that fails on Elsa 4 for a reason other than JOIN behaviour, **When** the cause is found, **Then**
   it follows the failure rule (FR-016): a template defect is fixed; a converter defect or Elsa 4 gap gets its own
   issue and blocks the suite until fixed, adjusted or bucketed `incompatible`; a definition that cannot convert
   faithfully is refused with guidance and bucketed `incompatible`. A refusal never counts as a pass.

### User Story 4 - JOIN-blocked suites finish later in this specification (Priority: P2)

As the coordinator, I can finish everything JOIN behaviour does not block, and still see exactly which suites remain
and why.

**Why this priority**: JOIN behaviour is owned by a separate initiative, but its suites are part of this acceptance.

**Independent Test**: Every suite marked JOIN-blocked names the definition, the join construct and the failure that
blocks it, and J-001 through J-005 are open in the tasks.

**Acceptance Scenarios**:

1. **Given** a suite whose definition contains a join, **When** it runs on Elsa 4, **Then** it is marked JOIN-blocked
   only if the join is what stops it; a suite whose path does not depend on the join stays in the main run.
2. **Given** every suite passing that SC-004 does not exempt, **When** status is reported, **Then** this is named the
   non-JOIN milestone, and the specification stays non-terminal while any JOIN or value-capture task is open.

### Edge Cases

- A suite depends on a definition shared with other suites (`branching-flows.json` backs five suites, including If
  and Switch); an adjusted copy changes every consumer, so the change and its effect are recorded for all of them.
- A fixed `wait` step is too short for one engine; raising it is a recorded suite change, never a silent edit, and a
  wait never replaces an assertion.
- A suite asserts on an activity ID; the converted definition must keep that ID, or the template maps it explicitly.
- A suite fails only intermittently; each run is kept, and a later pass does not erase an earlier failure.
- A definition converts but its suite exercises a feature Elsa 4 has no equivalent for; the suite records the
  incompatibility instead of rewriting the assertion.

## Requirements *(mandatory)*

### Functional Requirements

**Corpus**

- **FR-001**: The complete corpus MUST be the JTest-Nexxbiz repository at commit `b1cde13`: 36 Elsa 3 workflow
  definitions, 42 suite files, the shared templates, test files, environment file, global configuration and the Elsa 3
  setup suite. The first commit MUST copy these unchanged into the suite folder (the original's delivery pipelines and
  README are not test content and are not copied); every later change MUST be a reviewable diff from that copy.
- **FR-002**: The suite folder MUST live in the Elsa 3 converter test area (`extensions/Elsa3/tests/`), holding the
  original definitions, the suites, an Elsa 3 template set, an Elsa 4 template set, environment files per engine and
  any adjusted definition copies. It MUST NOT be part of any CI tier.
- **FR-003**: The copy in this repository MUST be the source of truth for this suite from the first commit. The
  original repository is not changed by this work; updating its templates is a hand-over at the end (FR-022).
- **FR-004**: No secret MUST be committed. Credentials and base URLs MUST come from environment variables bound in the
  environment files, as in the original corpus.

**Runner and baseline**

- **FR-005**: The suites MUST run with the public `jtest` command-line tool, latest released version, invoked as
  `jtest run`. They MUST NOT be wrapped in or rewritten as xunit tests. Report collection and pipeline integration are
  out of scope.
- **FR-006**: The Elsa 3 baseline MUST be an Elsa server built from `docker/ElsaServer.Dockerfile` at elsa-core
  release tag `3.6.3` (commit `7eac7de6`), the pinned baseline version. The build
  command and image digest MUST be recorded with each baseline run. The suite assertions are the
  baseline; no Elsa 3 host runs alongside Elsa 4 during acceptance. During Elsa 4 work an Elsa 3 run only settles a disputed assertion or verifies an approved
  suite change.
- **FR-007**: Before conversion work starts, the corpus MUST run on the Elsa 3 baseline. Every failing assertion MUST
  be classified as one of: suite defect, behaviour the pinned version corrected, or defect of the pinned version,
  with the evidence and the suite description. The coordinator MUST approve the whole classified list before any fix
  is applied.
- **FR-008**: A suite defect or a corrected behaviour MUST be fixed in the suite with the reason recorded per change.
  A defect of the pinned version MUST leave the assertion unchanged and mark the suite blocked with that defect named.
- **FR-009**: The Elsa 3 setup suite that imports and publishes the definitions MUST be used only for the Elsa 3
  baseline. Elsa 4 MUST load the definitions through the Elsa 3 startup reconciliation only.

**Engine-neutral suites and templates**

- **FR-010**: Each Elsa 3-specific value in a suite file (API paths, response fields, status strings, identifiers the
  engine generates) MUST move behind a template output, an environment value or a suite parameter. Expected business
  values and assertions MUST NOT change in that refactor, and the refactored suites MUST stay green on the Elsa 3
  baseline.
- **FR-011**: An Elsa 4 template set MUST provide every template the suites use, with the same names, parameters and
  outputs as the Elsa 3 set. A template that cannot produce an output with the same meaning on Elsa 4 MUST be reported,
  not approximated.
- **FR-012**: Any change to a suite file after the engine-neutral refactor MUST be recorded per assertion with its
  reason and approved by the coordinator. Raising a `wait` step is such a change. A timing change inside a template
  (the templates hold 7 of the 22 waits) is recorded the same way, for the engine set it changes.
- **FR-013**: Templates MUST observe the full value an assertion checks. A preview, a case-insensitive lookup or a
  summary MUST NOT stand in for an exact value, and a template MUST NOT normalize a value unless the suite asked for it.

**Elsa 4 run and dispositions**

- **FR-014**: The Elsa 4 run MUST start a fresh host with the Elsa 3 startup reconciliation reading the corpus's
  definitions, with each approved adjusted copy taking the place of its original, and MUST keep the reconciliation receipt with the run.
- **FR-015**: Every definition MUST end with exactly one bucket: `convert-as-is` (unchanged source passes its suites, or, for a
  definition no suite exercises or whose only suites are blocked by a 3.6.3 defect, is imported and published unchanged
  with no diagnostic),
  `convert-with-adjustments` (an adjusted copy, kept next to the unchanged original, with the exact change and reason,
  approved by the coordinator) or `incompatible` (approved by the coordinator; refused with a diagnostic and guidance, or a feature with no Elsa 4
  equivalent). Execution status and JOIN blocking MUST be recorded separately from the bucket.
- **FR-016**: Translating API calls in templates MUST NOT move a definition out of `convert-as-is`. A refusal or a
  publication alone MUST NOT count as a passing suite. A suite failing on Elsa 4 for a reason other than JOIN
  behaviour MUST follow one rule: a template defect is fixed in the template; a converter defect or Elsa 4 gap gets
  its own issue and the suite is `blocked: <issue>` until it is fixed, an approved adjusted copy passes, or the
  definition is bucketed `incompatible`; missing setup is `blocked: <prerequisite>`, never incompatible. Only suites
  blocked by JOIN behaviour, value capture or a 3.6.3 defect, or carrying an approved incompatibility, are exempt from
  the non-JOIN milestone (SC-004).
- **FR-017**: Where an adjusted copy is needed, the converter MUST NOT gain a workaround for it; a construct that
  cannot convert faithfully MUST be refused with guidance, and a genuine Elsa 4 gap gets its own issue.

**Work owned by separate initiatives**

- **FR-018**: A suite MUST be marked JOIN-blocked only when join behaviour is what stops it on Elsa 4. The record MUST
  name the definition, the join construct and the observed failure. A suite whose path does not depend on the join
  MUST stay in the main run.
- **FR-019**: JOIN-blocked suites MUST stay in this specification. Deferred tasks J-001 to J-005 MUST stay open until
  their own evidence exists, and a non-JOIN milestone MUST NOT mark this specification Implemented.
- **FR-024**: A suite MUST be marked `blocked: value capture` only when a value one of its assertions reads comes back
  truncated or unavailable under the host's capture limits. The record MUST name the suite, the key and the limit hit.
  Such suites stay in this specification, like JOIN-blocked ones; complete value capture belongs to the separate
  value-capture initiative, and deferred tasks V-001 to V-003 MUST stay open until their own evidence exists.

**Prerequisites, evidence and hand-over**

- **FR-020**: The Elsa 4 acceptance run MUST be repeated on an initiative head where the JSON data integrity
  baseline (spec 1207) is implemented (its status Implemented), not merely specified. Phases 1 and 2 and Elsa 4 template development MAY start before that.
- **FR-021**: Each run MUST keep the commit, engine and image or build, `jtest` version, environment file (without
  secret values), per-suite result and the reconciliation receipt for Elsa 4. A failed run MUST stay recorded next to
  later runs.
- **FR-022**: When this specification is complete, a hand-over MUST be prepared for the Nexxbiz-specific workspace to
  bring the engine-neutral suites and templates back to the original repository. That update is not part of this
  specification.
- **FR-023**: The suites, templates and run instructions have no Studio surface; no Studio work is in scope.

### Key Entities

- **Corpus copy**: the unchanged original at its source commit plus the reviewed diffs on top of it.
- **Suite**: one JTest file, its definitions, its result per engine, and any blocked mark with its reason.
- **Template set**: the Elsa 3 or Elsa 4 implementation of the same named templates.
- **Definition disposition**: the bucket, the adjusted copy or refusal, and the suites it affects.
- **Run record**: what ran, on what, with which result.

## Spec decisions

| ID | Question | Decision |
|---|---|---|
| Q1 | Which material is the complete corpus? | The JTest-Nexxbiz repository at `b1cde13`. 36 definitions and 42 suites. |
| Q2 | Where does the suite live? | In the Elsa 3 converter test area, because the flow is reconcile-then-run. A broader Elsa 4-native workflow-regression suite is a later, separate piece of work. |
| Q3 | Can the corpus be committed? | Yes, unchanged; it carries no secrets. |
| Q4 | What is the Elsa 3 baseline? | The suite assertions, made green on an Elsa server built from elsa-core tag `3.6.3` (the pinned baseline version). No paired dual-host runs. |
| Q5 | How are failures on the baseline settled? | Classified into three kinds and approved as one list by the coordinator before any fix (FR-007, FR-008). |
| Q6 | Which runner? | `jtest run`, latest version; no xunit wrapper; CI and reports later. |
| Q7 | How are engine differences expressed? | Same-name Elsa 4 templates; Elsa 3-specific values in suite files move behind templates or parameters first. |
| Q8 | Is the Elsa 3 setup suite needed on Elsa 4? | No. Startup reconciliation replaces it. |
| Q9 | How is JOIN handled? | Blocked only where the join actually stops a suite; the rest goes first; JOIN work stays here. |
| Q12 | What if a suite needs a complete value Elsa 4 only captures as a bounded snapshot? | It is blocked on the separate value-capture initiative, handled like JOIN: marked only where a truncated or unavailable value stops it, kept in this specification, finished when that initiative delivers (FR-024, V-001 to V-003). |
| Q10 | Which suites go first? | The six HTTP suites (one definition, no join), then single-outcome. |
| Q11 | Where does the corpus live after the copy? | Here; the original repository gets a template update through a hand-over at the end. |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 - Baseline**: Every one of the 42 suites passes on the Elsa 3.6.3 baseline server, or is marked blocked by a named
  defect of that version, with every suite change approved and reasoned.
- **SC-002 - Engine-neutral**: Every suite file is identical between the Elsa 3 and Elsa 4 runs, and still passes on
  the Elsa 3 baseline.
- **SC-003 - Dispositions**: All 36 definitions have exactly one bucket with its evidence (definitions whose suites
  are JOIN-blocked or value-capture-blocked are bucketed when the JOIN and value-capture tasks close).
- **SC-004 - Non-JOIN milestone**: Every suite not blocked by JOIN behaviour, by value capture (FR-024), by a 3.6.3
  defect (SC-001) or by an
  approved incompatibility passes on
  Elsa 4, on an initiative head with spec 1207 implemented (its status Implemented). This is a milestone, not completion.
- **SC-005 - Full acceptance**: SC-001 to SC-004 hold on the final initiative head, J-001 to J-005 and V-001 to V-003
  are closed with
  evidence, and every suite passes, carries an approved incompatibility, or is blocked by a named 3.6.3 defect
  (SC-001).

## Deferred JOIN tasks retained in this specification

These tasks are OPEN and carry into the tasks list.

| ID | State | Task and closure evidence |
|---|---|---|
| J-001 | OPEN | List every JOIN-blocked suite with its definition, join construct and observed Elsa 4 failure. |
| J-002 | OPEN | Reconcile those suites with the JOIN initiative's agreed contract and delivered implementation; agree any adjustment. |
| J-003 | OPEN | Make the JOIN-dependent templates and waits work on Elsa 4 without changing expectations. |
| J-004 | OPEN | Run the JOIN-blocked suites on Elsa 4 on the qualified head and record the results. |
| J-005 | OPEN | Reconcile the complete disposition list, rerun the whole corpus and record the coordinator's full-acceptance verdict. |

## Deferred value-capture tasks retained in this specification

These tasks are OPEN and carry into the tasks list.

| ID | State | Task and closure evidence |
|---|---|---|
| V-001 | OPEN | List every `blocked: value capture` suite with the key it reads and the capture limit hit. |
| V-002 | OPEN | Once the value-capture initiative delivers on the initiative branch, configure the run to capture those values as it provides and adjust the Elsa 4 templates without changing expectations. |
| V-003 | OPEN | Run those suites on a fresh Workbench from the qualified head and record the results; J-005 reconciles them with the rest. |

## Dependencies and boundaries

- [Startup reconciliation, spec 235](../235-elsa3-json-reconciliation/spec.md) loads and converts the definitions. This
  specification uses it as it is and changes no converter behaviour except through a separate defect or specification.
- [Mapping rules](../../extensions/Elsa3/src/Mapping/README.md) and
  [migration readiness](../../docs/reference/elsa3-migration-readiness.md) explain expected diagnostics.
- [JSON data integrity, spec 1207](../1207-json-data-integrity-baseline/spec.md) must be implemented on the initiative head
  before the qualifying Elsa 4 run.
- JOIN behaviour belongs to the separate JOIN initiative, and complete value capture to the separate value-capture
  initiative; their acceptance in this corpus belongs here.
- [Elsa constitution](../../.specify/memory/constitution.md) E2.7 keeps Elsa 3 behind the import boundary; the Elsa 3
  server runs only as a separate baseline container.

## Assumptions

- Docker is available locally to run the Elsa 3 image.
- The latest `jtest` release reads the corpus's suite format; if it does not, that is a phase-1 finding.
- The converter carries Elsa 3 definition and activity IDs over; where it does not, the Elsa 4 templates map them and
  the mapping is recorded.
