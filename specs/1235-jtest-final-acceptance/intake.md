# Final JTest acceptance intake

This is a working companion to [spec.md](spec.md). It contains no supplied corpus records yet. Empty inventory is not evidence of zero cases, complete discovery or successful acceptance. Private paths, identities, hashes, payloads and secrets belong in a separate local ledger outside public commits.

## Initial intake state

| Item | State |
|---|---|
| Complete-suite folder location and readiness | Awaiting owner |
| Owner-declared suite/case/definition-version manifest | Awaiting material and completeness confirmation |
| JTest runner, schema, suite/export versions | Unknown |
| Elsa 3 engine/host and baseline configuration | Unknown |
| First representative suite and expected outcomes | Awaiting owner choice |
| Private evidence destination / approved public metadata | To establish before copying or publishing corpus-derived material |
| Harness template/environment capability | To verify in the supplied runner and assets |
| S-2 and S-9 delivery/integrated-head qualification | Required; live status is on the initiative board |
| JOIN-affected case count and dependency closures | Unknown; J-001 through J-005 remain OPEN |
| Spec agreement, plan/design, executable tasks and implementation | Not reached |

## First focused intake round

1. Establish the absolute source-folder location and whether assembly is complete. Obtain the expected suites/versions or export manifest and reconcile it with discovery, including arrange/assert assets. Do not adopt the previous 36-definition export as a fallback.
2. Pin the intended Elsa 3 engine/host and JTest version/export identity. Inspect one representative suite with its workflow closure and agree what success means before translating it.

Continue independent source/contract preparation while awaiting these answers. Further questions follow concrete files or discoveries rather than an all-at-once questionnaire.

## Inventory record structure

The following are field requirements for private records, not an implemented schema or populated inventory.

| Record | Required contents |
|---|---|
| Corpus snapshot | Local absolute root; immutable snapshot/export/source identity; relative file inventory and private content hashes; runner/schema/suite versions; owner-declared expected material; discovered/missing/extra/duplicate reconciliation; completeness acknowledgment; permitted sharing/evidence location |
| Suite and case | Stable local identity and safe public alias; suite version; case/assertion IDs; entrypoint; inputs; expected success/failure/wait; original arrange/actions/assertions and referenced files; setup/reset/cleanup; external requirements; ordering/shared state; baseline result and evidence |
| Definition version | Original file/hash and definition/version; published selection; regular/reusable role; direct and transitive reusable/dispatch dependencies; unresolved dynamic targets; selected version edges; callers; bucket and justification; JOIN scope flag; affected cases |
| Adjustment | Original and derived hashes/paths; exact patch; source location; why needed; unchanged assertions; intentional semantic change if any; all affected callers; coordinator agreement; original-baseline and adjusted-baseline relationship |
| Incompatibility | Stable reason identifier; invariant not preserved; diagnostic and conversion/publication stage; evidence; guidance; inherent or current implementation gap; reconsideration condition; dependent impact; parity explicitly not proven |
| Shared asset | Fixtures, request/response templates, environment files, helper scripts and arrange/assert workflows; all consumers; version/hash; external service and secret-reference requirements; reset/cleanup ownership |
| Host baseline | Elsa/JTest source or immutable build; modules/custom activities; provider/database and persistence profile; tenant/scope; auth/permission references; URLs/ports by environment binding; clocks/time zone/culture; serialization/capture/diagnostics; background/execution settings; readiness/start/reset commands; seed data; cleanup |
| Engine mapping | Stable case and assertion IDs; original API/method/path/body; verified Elsa 4 equivalent; rendered request; extracted identifiers; full typed observation source; wait/poll/deadline rules; genuine difference and template/environment binding; semantic equivalence rationale |
| Normalization | Exact field/path; reason; algorithm; permitted domain; identity-map or temporal/order constraints retained; agreed examples of equivalent and meaningfully different values; review decision |
| JOIN closure | Direct construct or implicit convergence; wait/race/per-arrival/re-entry semantics; dependency path to affected cases including shared arrange/assert; unresolved edges; deferral reason; baseline assertions; J-task links |
| Attempt | Corpus/host/harness/config/source fingerprints; case/variant/closure; start/reset/readiness; stimulus schedule; observations/deadlines; import/publication/execution/assertion/cleanup outcomes; incidents; private artifact paths; safe public receipt; predecessor/repeat reason |

Use exactly the three final definition buckets from FR-005. Keep unclassified/provisional intake state, JOIN eligibility and run outcome on separate axes. A missing fixture blocks a case; it does not create an incompatibility.

## Case mapping worksheet

For each case, fill these sections with actual files and verified host behavior. Do not commit private material in this worksheet.

| Step | Elsa 3 baseline | Elsa 4 equivalent | Proof needed |
|---|---|---|---|
| Setup/reset and seed data | Pending corpus | Pending mapping | Store isolation; dependency versions; no stale subscriptions/publications |
| Convert/import and publish | Original baseline deployment | Startup reconciliation or collection-import coverage | Exact selected closure; receipt; active publications; diagnostics; entrypoint coverage rationale |
| Arrange | Original suite assets | Translated template or engine-specific asset | Same starting semantic state, permissions and fixtures |
| Start/stimulate | Original actions/events/inputs | Verified API and bindings | Instance identity; start versus resume; correct correlation; payload |
| Observe/wait/resume | Original JSON paths/observers and timing | Adequate full-value/state source | Correct expected wait; readiness; bounded polling; required follow-up stimuli; no unexpected incidents |
| Assert | Original assertion IDs and expected values/types | Traceable equivalent assertions | Same semantic strength; field-specific agreed normalization only |
| Cleanup | Original reset/cleanup contract | Verified equivalent | No leaked state/subscription/process; failures retained |

Minimum representative selection to discuss after inventory: a deterministic value/output case, an input/event waiting-and-resume case, and a reusable dependency case. These are sampling for translation design, not replacements for complete-suite acceptance. Keep JOIN-affected representatives in intake but outside initial executable scope.

## Concrete choices to settle through supplied examples

| Choice | Candidate approach | What must be established |
|---|---|---|
| Shared suite semantics | Prefer shared scenario/case/assertion identities rendered through separate engine request/observation templates and environment bindings if the runner supports this cleanly | Actual runner/version/schema; template evaluation and escaping; path extraction; assertion expressiveness; baseline preservation; rendered-request retention |
| Fallback if shared rendering is unsuitable | Separate derived engine suites with the same semantic case/assertion identities and a checked mapping ledger | Maintenance cost, drift detection, ability to prove no assertion disappeared; coordinator choice |
| Genuine environment differences | Named bindings for base URLs, auth references, routes, JSON extraction paths and verified host-specific envelopes | Classify configuration versus an API contract difference versus a semantic difference; no engine-specific expected business result just to pass |
| Observation | Full typed values and lifecycle/bookmark/event evidence sufficient for the original assertion | Actual API/contracts and capture policy; previews or incidental UI casing cannot stand in for exact values |
| Wait/timeout | Scenario-specific readiness and progress conditions, stimulus schedule, observation interval and deadlines | Baseline timing, eventual consistency, quiet-period checks, expected terminal versus continued-wait state; avoid universal completion-only timeout |
| Normalization | Explicit path-specific identity/time/order rules with negative examples | Which values are generated, which are meaningful, and how references, chronology, multiplicity and causal order remain checked |
| Entry points | Coverage matrix for startup reconciliation and HTTP collection import | Which cases exercise which entrypoint, where restart/idempotency and dependent refusal proof belong, and reuse of the same selected closure |

No choice in this table is approved merely because it is described here.

## JOIN intake and deferred work

Inspect original graph topology and dependencies, not just named FlowJoin nodes. Flag implicit convergence, FlowFork/FlowJoin, WaitAny/WaitAll/WaitAllActive, fan-in and per-arrival, cancellation/race behavior, loops that re-enter and nested graph/reusable cases. These are detection categories to inspect; no supplied case is asserted to contain them yet.

Compute forward dependency closure and reverse affected-case closure, including shared setup and assertion workflows. Identify selected versions, indirect dispatch and unresolved dynamic edges. Uncertainty is a deferral until clarified, never proof that a case is JOIN-free. Preserve case semantics even if conversion currently refuses the source.

| Retained task | Initial state | Intake/qualification output |
|---|---|---|
| J-001 | OPEN | Complete direct/transitive case ledger and versioned dependency paths |
| J-002 | OPEN | Reconciliation with agreed JOIN contract and integrated implementation |
| J-003 | OPEN | Translated semantic stimuli, waits and assertions |
| J-004 | OPEN | Paired execution evidence, negative controls and converged review |
| J-005 | OPEN | Full-corpus reconciliation and full-acceptance verdict |

The authoritative obligations are in spec.md. No counts or classifications can be assigned before the supplied material is inspected. JOIN decisions and implementation stay in the separate initiative; acceptance tasks stay here.

## Workflow progression

1. Complete focused intake, immutable/private inventory, baseline and representative semantic mappings. Resolve concrete differences with the coordinator and record the decisions in the draft discussion.
2. Mature spec.md and its quality checklist. Obtain coordinator agreement on scope, dispositions/adjustment rules, full inventory and translation/observation choices. Keep the proposal draft while substantive questions remain.
3. Execute the Spec Kit plan workflow: load both constitutions, resolve research, define the necessary private-ledger and harness contracts, host validation guide and risk-based test design. Review the artifacts before generating executable tasks.
4. Generate dependency-ordered tasks from the reviewed plan. Carry J-001 through J-005 into those tasks without closing or moving away their remaining obligations. S-2/S-9 and separate JOIN delivery retain their own scope.
5. Begin agreed implementation only after the applicable gates. Run meaningful local checks and review/fix/review to convergence; retain failures and report CI as deferred.
6. Record the non-JOIN milestone and later full acceptance separately; open JOIN tasks prevent full completion and a terminal spec status.

No production implementation plan or executable implementation tasks have been generated in this initial drafting unit.
