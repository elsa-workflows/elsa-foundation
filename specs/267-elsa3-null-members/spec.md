# Feature Specification: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

**Feature Branch**: `agent/claude/3200-elsa3-null-input`

**Created**: 2026-10-22

**Status**: Implemented

**Status scope:** the original bare-JSON-null delivery and the separate #3281 follow-up are implemented. The
follow-up's local evidence and remaining external integration/formal merge gates are recorded below.

**Issue**: [#3200](https://github.com/elsa-workflows/elsa-foundation/issues/3200), part of epic
[#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052)

**Input**: An Elsa 3 activity property that is JSON `null` converts without a diagnostic. Publishing the imported
version then fails with `400 Activity node '<node>' input 'possibleOutcomes' does not declare an expression type`. In
the JTest corpus this blocks 2 of the 8 converting definitions from publishing (composition-bulk-dispatch-sub-workflow,
performance-bigdata). Both have an Elsa 3 RunJavaScript with `"possibleOutcomes": null`.

**Depends on**: spec 236 (#2706, merged), the activity map and its member rules. Spec 264 (#3122, merged) fixed the
same mistake for workflow variables.

**Principle**: faithful or refuse. A member converts only when Elsa 4 behaves as Elsa 3 did at run time. Otherwise the
converter refuses with a stable diagnostic and guidance on what to change in the Elsa 3 workflow.

**Boundary**: converter only (`Extensions/Elsa3`) and the spec 236 member map. Elsa 4 activity defaults, publication
and the runtime do not change. **Scope**: JSON `null` members. Absent and blank members whose converted value differs
from Elsa 3's are [#3281](https://github.com/elsa-workflows/elsa-foundation/issues/3281).

## Root cause

The converter keeps a JSON `null` member as a binding that holds nothing and names no expression language. Publication
compiles every binding it is given and reads one with no expression language as an expression, which it refuses. A
null output fails the same way, because publication requires an output binding to name a variable.

## What a JSON null meant to the Elsa 3 runtime

The rule of this spec is: **a null member converts to what the Elsa 3 runtime ran the activity with.** It is stated per
member, from the Elsa 3 source (release/3.6.3, checked against the 3.5.3 tag), not from a reading of what `null` means.

### How Elsa 3 loads and runs a member

- An activity is loaded by deserializing its JSON into its class. A member that is JSON `null` sets the input property
  itself to null, replacing the class's initial value. A member that is absent keeps the initial value.
- At run time, an input whose property is null runs with the input's declared default (`[Input(DefaultValue = …)]` or
  its default-value provider), and otherwise with nothing: a value type reads as its type default (`false`, `0`), a
  reference type as `null` (`ActivityExecutionContextExtensions.InputEvaluation.cs`, `EvaluateInputPropertyCoreAsync`).
- A *deliberate* null is a different JSON shape: an input that holds a literal null,
  `{"typeName": …, "expression": {"type": "Literal", "value": null}}`. It runs as an explicit null, with no default.
  The export therefore does tell a deliberate null from a bare JSON `null`. Elsa 3 ran the two the same, except for a
  member with a declared default. The converter already reads the deliberate shape as unbound, like a JSON `null`, and
  keeps doing so (FR-005); the declared-default exception is #3281. No corpus definition has a deliberate null.
- A workflow-as-activity input that is JSON `null` is skipped when loading, so it runs with the reusable's own default.
- A JSON `null` output means nothing captures it.

### Per member

"Typed null" means a `Literal` binding whose value is null; Elsa 4 publishes it for an input that accepts null.

| Elsa 3 member (`null`) | Elsa 3 declared default | What Elsa 3 ran with | Conversion |
|---|---|---|---|
| RunJavaScript.PossibleOutcomes | none | null: completes with `Done` | typed null |
| Any output | – | nothing captured | no binding |
| If / While / FlowDecision.Condition | none | `false` | no binding (Elsa 4 default `false`) |
| For.Start, For.End | none | `0` | no binding (Elsa 4 default `0`) |
| For.Step | none (initial value 1 is replaced) | `0`: the loop never advances | **refuse** |
| For.OuterBoundInclusive | `true` in 3.6.3; none in 3.5.3 (`false`) | `true` (3.6.3, the reference) | emit `true` (Elsa 4 default `false`) |
| ForEach.Items | none | null: the activity faults | **refuse** |
| WriteLine.Text | none | null: writes an empty line | emit `""` (Elsa 4 requires text) |
| Fault.Code, Category, FaultType | none | null; the activity substitutes `0`, `General`, `System` | emit `0`, `General`, `System` |
| Fault.Message | none | null | typed null (Elsa 4 then words the fault "The workflow faulted.") |
| SetVariable.Value, SetOutput.OutputValue | none | null: sets the variable or output to null | **refuse**: Elsa 4 cannot publish a null intrinsic value ([#3280](https://github.com/elsa-workflows/elsa-foundation/issues/3280)) |
| SetOutput.OutputName | none | rejected as a missing required input | **refuse** |
| Correlate.CorrelationId, SetName.Value | none | null: clears the correlation id or instance name | emit `""` (Elsa 4 clears both on an empty string; it cannot publish a null intrinsic value) |
| HttpEndpoint.Path | none | no path: an invalid endpoint | **refuse** |
| HttpEndpoint.SupportedMethods | none | null; the activity substitutes `GET` | typed null (Elsa 4 also substitutes `GET`) |
| HttpEndpoint.Authorize | none | `false` | no binding (Elsa 4 default `false`) |
| HttpEndpoint.Policy, RequestTimeout, RequestSizeLimit | none | null | typed null |
| SendHttpRequest / FlowSendHttpRequest.Url | none | rejected as a missing required input | **refuse** |
| SendHttpRequest / FlowSendHttpRequest.Method | `GET` | `GET` | no binding (Elsa 4 default `GET`) |
| SendHttpRequest / FlowSendHttpRequest.Content | none | null: no body | typed null |
| SendHttpRequest / FlowSendHttpRequest.ContentType, Content bound | none | null: **the body is not sent** | **refuse** |
| SendHttpRequest / FlowSendHttpRequest.ContentType, Content unbound | none | null: no body | typed null |
| SendHttpRequest / FlowSendHttpRequest.RequestHeaders | none (initial value replaced) | null: the activity faults | **refuse** |
| FlowSendHttpRequest.ExpectedStatusCodes | provider: `[200]` | `[200]`: outcomes `200`, `Unmatched status code` | emit `[200]` (Elsa 4 default none) |
| WriteHttpResponse.StatusCode | `OK` | `200` | no binding (Elsa 4 default `200`) |
| WriteHttpResponse.Content, ContentType | none | null | typed null |
| WriteHttpResponse.ResponseHeaders | none (initial value replaced) | null: the activity faults | **refuse** |
| Delay.TimeSpan | provider: 1 minute | 1 minute | emit 1 minute (Elsa 4 default 0) |
| Timer.Interval | `00:01:00` | 1 minute | emit 1 minute (Elsa 4 requires it) |
| Cron.CronExpression | none | null: no trigger; the activity completes (3.6.3) | **refuse** |
| Event.EventName, PublishEvent.EventName | none | rejected as a missing required input | **refuse** |
| PublishEvent.CorrelationId, Payload | none | null | typed null |
| PublishEvent.IsLocalEvent | none | `false` | no binding (Elsa 4 default `false`) |
| DispatchWorkflow / ExecuteWorkflow.WorkflowDefinitionId | none | rejected as a missing required input | **refuse** |
| DispatchWorkflow / ExecuteWorkflow.CorrelationId, Input | none | null | typed null |
| DispatchWorkflow.WaitForCompletion | none | `false` | no binding (Elsa 4 default `false`) |
| ExecuteWorkflow.WaitForCompletion | none | `false`: runs the child inline until it completes or goes idle | unchanged: spec 236 maps ExecuteWorkflow to a Dispatch that waits, with its own behaviour warning |
| RunJavaScript.Script | none (initial value `""` replaced) | null: does nothing | **refuse** |
| A workflow-as-activity input | the reusable's input default | skipped when loading | no binding |

Members the activity map removes (R3) or restricts (R6) already treat `null` as absent, which matches Elsa 3: a null
removed input ran with nothing. They do not change.

## Measured baseline (JTest corpus, 2026-10-22, `main` at `9613cf4ad`)

A throwaway test, deleted afterwards, imports each of the 36 definitions on its own into the `Elsa3ImportHost`
(HTTP and scheduling composed), publishes each one that converts, and starts each published definition that has no HTTP
trigger.

| Step | Definitions (of 36) |
|---|---|
| Converts | 8 |
| Publishes | 2 (performance-baseline-dispatch-sub, simple-write-hello-world) |
| Runs | 2 (the same two) |

Publishing reports only its first error. The 6 that convert but do not publish stop at:

| First publish error | Definitions | Issue |
|---|---|---|
| `possibleOutcomes` does not declare an expression type | composition-bulk-dispatch-sub-workflow, performance-bigdata | #3200 (this spec) |
| A `Variable` binding with no reference key | events-listener, performance-commit-after-every-activity | #3201 |
| `VF-COER-001`: `Headers` value `{}` cannot become `System.String[]` | javascript-array-methods, javascript-object-methods | #3202 |

JSON `null` members in the corpus, by activity: HttpEndpoint (10 members, up to 30 times each), WriteLine.text (27),
WriteHttpResponse.contentType (20), RunJavaScript.possibleOutcomes (17) and result (8), and a few on Dispatch,
BulkDispatch, Switch, If and PublishEvent.

## Measured after (JTest corpus, 2026-10-23, this branch on `main` at `9613cf4ad`, and again merged with `d567a948d`)

Same throwaway test as the baseline, deleted afterwards. Both runs give the same numbers. The import host imports
`storage-drivers-variables-sub-workflow` as a reusable activity, which still needs #3023, so it counts 8 conversions
where the converter-only count in epic #3052 is 9.

| Step | Baseline | After |
|---|---|---|
| Converts | 8 | 8 (the same eight; the 28 that do not convert report the same diagnostics) |
| Publishes | 2 | 3 (+ composition-bulk-dispatch-sub-workflow) |
| Runs | 2 | 2 |

Both #3200 definitions are past the `possibleOutcomes` error (SC-001). Their next blockers, outside this spec:

| Definition | Next blocker | Issue |
|---|---|---|
| composition-bulk-dispatch-sub-workflow | Publishes. Started with no input, its FlowDecision reads the declared input `ShouldFault`, which Elsa 4 refuses to read when unsupplied; Elsa 3 read it as null | [#3288](https://github.com/elsa-workflows/elsa-foundation/issues/3288) |
| performance-bigdata | Publish: the RunJavaScript's `arguments` expression declares `Object`, the input expects `JsonElement` | [#3287](https://github.com/elsa-workflows/elsa-foundation/issues/3287) |

The other four converting definitions stop at the same first errors as in the baseline (#3201, #3202). No warning in the
corpus comes from this spec (SC-005): it adds none.

Found while testing: running any imported RunJavaScript is blocked today, by #3195 when the script reads nothing and by
#3287 when it reads something. The integration test therefore proves the RunJavaScript case at publish, and the run on
WriteLine, PublishEvent, Correlate and SetName nulls (SC-003).

## Clarifications

### Session 2026-10-22 (maintainer, before specification)

- Q: Should Elsa 4's own defaults move to Elsa 3's where they differ? → A: No. Elsa 4's defaults were chosen for the
  Elsa 4 runtime (HttpEndpoint's response mode already defaults to synchronous). The converter emits Elsa 3's value
  where Elsa 4's default differs.
- Q: Should the converter warn when it reproduces Elsa 3's value? → A: No. Nothing changes for the user. A refusal is
  only for a member whose Elsa 3 behaviour Elsa 4 cannot reproduce.
- Q: A null member where Elsa 3 hung, faulted, dropped data or rejected the activity? → A: Refuse, with one stable code
  that names the member and how to fix it in the Elsa 3 workflow.
- Q: A null `Fault.Message`? → A: Convert silently. Only the human-readable text differs.
- Q: `For.OuterBoundInclusive` null, where 3.5.3 and 3.6.3 differ? → A: Follow 3.6.3: `true`.
- Q: A null intrinsic value? → A: Emit an explicit null where Elsa 4 can publish one, otherwise refuse. Elsa 4 clears
  the correlation id and the instance name on an empty string, so those two convert to `""`.
- Q: ExecuteWorkflow's `WaitForCompletion`? → A: Out of scope; spec 236's mapping stands.
- Q: RunJavaScript's `arguments` faulting when unset (#3195)? → A: Separate issue.

### Session 2026-10-23 (review by Frans van Ek on PR #3264, settled by the maintainer)

- Review: "null equals absent" is a design choice the spec did not justify; the rule should reproduce what the Elsa 3
  runtime did, with evidence per member. → A: Reframed. The rule is now stated per member from the Elsa 3 source (the
  table above). Where Elsa 3 ran with null and Elsa 4 accepts null, the member converts to a typed null. The
  conversions are unchanged otherwise.
- Review: setting a variable to null is valid; the refusal list should shrink to what Elsa 4 cannot represent. → A: The
  `SetVariable` / `SetOutput` refusal stays because Elsa 4 cannot publish a null intrinsic value (`VF-ACT-004`).
  [#3280](https://github.com/elsa-workflows/elsa-foundation/issues/3280) asks Elsa 4 to accept one; the converter then
  emits a typed null instead.
- Review: the absent-member changes are not about null. → A: Split out to
  [#3281](https://github.com/elsa-workflows/elsa-foundation/issues/3281). This spec covers JSON `null` members only.

## User Scenarios & Testing *(mandatory)*

The actor is a workflow author or operator who brings Elsa 3 workflow definitions (JSON) into Elsa 4 through the
collection import, then publishes and runs them.

### User Story 1 - A workflow with null activity members publishes and runs as in Elsa 3 (Priority: P1)

An author imports an Elsa 3 workflow whose activities carry `null` members, as Elsa 3 exports do for members the author
never set, and none of them is a member this spec refuses. The import succeeds, publishing succeeds, and the run
behaves as the workflow did in Elsa 3.

**Why this priority**: this is #3200. Elsa 3 exports are full of `null` members, and today they block publishing.

**Independent Test**: import and publish a workflow whose RunJavaScript has `"possibleOutcomes": null` and
`"result": null`; import, publish and run one whose WriteLine, PublishEvent, Correlate and SetName have null members.
Running an imported RunJavaScript is blocked separately (#3195, #3287).

**Acceptance Scenarios**:

1. **Given** an Elsa 3 RunJavaScript with `"possibleOutcomes": null`, **When** it is imported and published, **Then**
   publishing succeeds, with the possible outcomes bound to null, as in Elsa 3. (Running it waits on #3195 and #3287.)
2. **Given** an Elsa 3 activity output that is `null`, **When** it is imported and published, **Then** publishing
   succeeds and nothing is captured, as in Elsa 3.
3. **Given** an Elsa 3 WriteLine with `"text": null`, **When** it runs, **Then** it writes an empty line (its text is
   `""`), as in Elsa 3.

---

### User Story 2 - A null member runs with Elsa 3's value where Elsa 4's default differs (Priority: P1)

An author imports an Elsa 3 workflow with `null` members on activities whose Elsa 4 default differs from what Elsa 3
ran with. The converted workflow runs with Elsa 3's values, with no warning, because nothing changed.

**Why this priority**: a silently changed default changes what a migrated workflow does.

**Independent Test**: convert each such member `null`, and check that the converted node carries Elsa 3's value.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 Delay with `"timeSpan": null`, **When** it converts, **Then** it waits one minute, as in Elsa 3.
2. **Given** an Elsa 3 For with `"outerBoundInclusive": null`, **When** it converts, **Then** its end bound is
   inclusive, as in Elsa 3.6.3.
3. **Given** an Elsa 3 FlowSendHttpRequest with `"expectedStatusCodes": null`, **When** it converts, **Then** it
   expects status 200, and the Elsa 3 connections from its `200` and `Unmatched status code` outcomes convert.
4. **Given** an Elsa 3 Correlate or SetName with a `null` value, **When** it runs, **Then** it clears the correlation id
   or the instance name, as in Elsa 3.
5. **Given** any of these conversions, **When** the import is analysed, **Then** no warning is reported for them.

---

### User Story 3 - A null member Elsa 4 cannot reproduce is refused with guidance (Priority: P2)

An author imports an Elsa 3 workflow with a `null` member where Elsa 3 hung, faulted, dropped data or rejected the
activity, or where Elsa 4 cannot represent what Elsa 3 did. The converter refuses that activity and says which member
to set, or that the activity can be removed.

**Why this priority**: faithful or refuse. Converting these would change what the workflow does without telling anyone.

**Independent Test**: convert each refused member `null`, and check for the refusal code, the member and the guidance.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 For with `"step": null` (Elsa 3 ran with a step of 0, so the loop never advanced), **When** it
   converts, **Then** it is refused, naming `step`.
2. **Given** an Elsa 3 SendHttpRequest with bound content and `"contentType": null` (Elsa 3 sent no body), **When** it
   converts, **Then** it is refused, naming `contentType`.
3. **Given** an Elsa 3 SetVariable with `"value": null`, **When** it converts, **Then** it is refused, naming `value`
   and #3280.

### Edge Cases

- An input holding a literal null follows FR-005, including the named declared-default exceptions in the follow-up.
- An empty string or an empty JSON array or object is not `null`. It converts by the existing rules; blank strings are
  #3281.
- A member that is absent converts by the existing rules; absent members whose value differs from Elsa 3's are #3281.
- A `null` member on an activity resolved by name (an alias, a catalog key or a workflow-as-activity reference) has no
  recorded Elsa 3 default, and gets no binding, so the Elsa 4 contract's default applies.
  - For a workflow-as-activity reference this is faithful: Elsa 3 skipped a null member, and the reusable ran with its
    own default.
  - For an alias or a catalog key, Elsa 3 ran its own activity class, with that class's declared default or the type's
    default, which the converter cannot see. Where the Elsa 4 activity declares a different default, the converted
    value could differ. This was a known limit of this spec (found in review). Since #3293 the conversion reports it
    with `ELS3-BEHAVIOUR-NULL-TAKES-ELSA4-DEFAULT`, naming the Elsa 4 default and what to set to keep Elsa 3's value.
- A `null` R1 or R5 input of a mapped activity that the table above does not list is a gap in the table. The
  activity-map coverage test fails on it (FR-008); at run time such a member gets no binding.
- R7 structure members (Switch and FlowJoin modes, Fork's join mode, SetVariable's variable, Complete's outcomes), ports
  and custom properties are not in the table. Their rewrites and rules already read a null as unset, and this spec does
  not change them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A JSON `null` activity member MUST convert to what the Elsa 3 runtime ran the activity with, as recorded
  per member in the table above.
- **FR-002**: Where Elsa 3 ran with null and the Elsa 4 input accepts null, the member MUST convert to a typed null.
  Where Elsa 3 ran with a value Elsa 4's default equals, the member MUST get no binding.
- **FR-003**: Where Elsa 3 ran with a value that differs from Elsa 4's default, the member MUST be bound to Elsa 3's
  value as a literal (the table's "emit" rows). The activity map is the one record of those values; it gains
  FlowSendHttpRequest `[200]` and the Correlate / SetName empty string.
- **FR-004**: A `null` output MUST get no binding (nothing is captured).
- **FR-005**: An input holding a literal null MUST convert by the original JSON-null table except for the named
  declared-default cases amended by FR-014 below. Those cases use their deliberate-null effective value or refusal,
  rather than acquiring the bare-null declared default. Research R-5 records the original delivered conflation;
  the #3281 follow-up corrects only those named exceptions.
- **FR-006**: A `null` member whose Elsa 3 behaviour Elsa 4 cannot reproduce or represent MUST be refused with one new
  stable diagnostic code (the table's "refuse" rows). The message names the member and the reason; the guidance says
  to set a value in the Elsa 3 workflow, or to remove the activity.
- **FR-007**: The converter MUST NOT report a warning for a `null` member it converts under FR-002, FR-003 or FR-004.
- **FR-008**: Each R1 (carried) and R5 (narrowed) input of a mapped Elsa 3 activity MUST have a recorded `null`
  conversion, or an Elsa 3 default it emits. A test fails when one has neither.
- **FR-009**: The spec 236 member map document MUST record each member's `null` conversion, in one section of its own
  that names every member.
- **FR-010**: Elsa 4 activity defaults, publication and the runtime MUST NOT change. The original delivered unit
  covered bare JSON-null members; the #3281 amendment below adds only the named converter corrections.

### Key Entities

- **Null member**: an Elsa 3 activity member whose JSON value is `null`, as opposed to an input holding a literal null.
- **Elsa 3 run value**: what Elsa 3 ran a null member with: the input's declared default, the type's default, or null.
- **Activity map**: the converter's table of Elsa 3 members, the Elsa 4 keys they map to, and Elsa 3's value where it
  differs from Elsa 4's default.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: composition-bulk-dispatch-sub-workflow and performance-bigdata no longer stop at the `possibleOutcomes`
  error at publish. Each is measured again and its next blocker, if any, is recorded here and in epic #3052.
- **SC-002**: No JTest definition that converts on `main` (8) stops converting.
- **SC-003**: An integration test imports, publishes and runs a workflow with `null` activity members, and the run
  behaves as in Elsa 3.
- **SC-004**: Every row of the per-member table has a test with the member `null`, for each activity the row names.
  The workflow-as-activity row shares the by-name path with a catalog key, which has its own case.
- **SC-005**: The converted workflows of the corpus carry no warning that this spec introduced.
- **SC-006**: The spec 236 member map document records the `null` conversion of every mapped member (FR-009).
- **SC-007**: The change touches no Elsa 4 activity, publication or runtime code (FR-010). The diff outside
  `Extensions/Elsa3` and `specs/` is empty, and no Elsa 4 test changes.

## Assumptions

- Elsa 3's selected baseline is release/3.6.3, checked against the 3.5.3 tag. The original For default choice is settled
  above; follow-up research also records Cron and source-layout differences rather than claiming identical runtimes.
- The JTest corpus's tool version is 3.5. No corpus definition has a `null` `For.OuterBoundInclusive`.
- The measurement method matches specs 247, 248, 262 and 264: throwaway tests, deleted before the commit.
- Original delivery exclusions: Elsa 4 changes (#3280), absent and blank members (#3281), #3195 (RunJavaScript
  `arguments`), #3201, #3202, ExecuteWorkflow's mapping (spec 236), engine-start reconciliation (spec 235).
  The #3281 amendment below brings only its named converter cases into the new follow-up scope.

## Converter default follow-up — #3281

**Follow-up status:** Implemented separately after the requirements proposal merged. The implementation does not
claim S-2 policy integration or completion of formal review/coordinator merge.

The selected legacy baseline is 3.6.3, checked against 3.5.3. This follow-up applies the existing faithful-or-refuse
rule to absent, blank and deliberately null members. The source evidence and current-target constraints are in
[research R-12](research.md#r-12-3281-source-audit-and-shape-boundary) and
[research R-13](research.md#r-13-current-source-recheck-and-initiative-integration).
Source inspection is not historical runtime execution.

### User Story 4 — Defaults do not change during migration (Priority: P1)

An author imports a workflow that omits an input, deliberately clears it, supplies a blank value, or clears a status
list. The converted workflow uses the effective legacy value, or import names the member that cannot be reproduced.
It never silently substitutes a different Elsa 4 default.

**Independent test:** import, publish and execute representative For/Delay/HTTP workflows; compare concrete values,
requests and selected paths. Refused variants leave no imported versions or publications.

**Acceptance scenarios:**

1. An absent ContentType with potentially non-null Content is refused because Elsa 3 sent no body. An explicit blank
   string ContentType retains JSON-writer behavior. Empty string Content is non-null and is included in this boundary.
2. Absent/defaulted FlowSendHttpRequest.ExpectedStatusCodes emits `[200]`. A deliberate null or explicit empty list
   must not become omission or `[200]`. With the current target's different non-2xx routing, those cleared-list uses
   are refused, naming ExpectedStatusCodes.
3. Deliberate-null For.OuterBoundInclusive uses false and Delay.TimeSpan uses zero duration; bare JSON null retains
   the delivered true/one-minute behavior.
4. Zero, negative or invalid Timer intervals, and HTTP response statuses that Elsa 4 would normalize to 200, are
   refused rather than omitted or replaced by defaults.
5. Blank Cron/Script uses receive member-specific guidance for legacy disabled/no-work behavior. Valid supported
   nonblank inputs, custom maps without the new corrections, caller JSON and duplicate-binding checks retain their
   contracts.

### Requirements

- **FR-011**: For the named built-in members, conversion MUST distinguish absence, bare JSON null, a supported empty
  input wrapper, a resolved Literal expression whose value is null or omitted, literal blank strings, and explicit
  empty collections. It MUST inspect source shape before normalization erases that distinction. Text such as
  `"null"`, explicit arrays/objects and arbitrary expressions MUST NOT become absence through a general `!IsBound`
  test. Bare JSON-null handling from the delivered table remains unchanged by this follow-up.
- **FR-012**: Missing or supported-empty ContentType on SendHttpRequest/FlowSendHttpRequest MUST be refused when
  Content is or may be non-null: Elsa 3 skipped the body, whereas the target can send it. The legacy guard is
  **non-null**, not nonempty/nonwhitespace. A present blank literal ContentType MUST retain JSON-writer behavior
  within the existing Content contract, or receive a member-specific refusal if that use cannot be reproduced.
- **FR-013**: Missing or supported-empty FlowSendHttpRequest.ExpectedStatusCodes MUST emit `[200]`. Literal null,
  blank values that evaluate to a null collection, and explicit empty lists MUST remain distinct from that default.
  Faithfulness includes **every selected path on 200, 404 and 500**, not only absence of numeric branches. Elsa 3
  selects Done for every response when cleared; the current target selects Done for 2xx and Failed otherwise.
  Therefore these cleared-list forms MUST currently be refused with member-specific guidance. The merged outcome
  contract MUST NOT change to hide the mismatch. Nonempty supported lists retain their existing mapping.
- **FR-014**: The named declared-default exceptions MUST use the legacy effective value or be refused according to
  the matrix. Timer literals that are zero, negative or invalid MUST NOT select one minute. Response status literals
  `0`, negative values, literal-null-to-zero and invalid blank enum text MUST NOT become 200. A computed status whose
  compatible range cannot be established MUST be refused rather than evaluated or defaulted during import.
  Blank Cron/Script refusals MUST describe the conversion/catalog constraint accurately; the target RunJavaScript
  runtime already has a blank-script path. Fault.Message retains its accepted silent message-text difference.
- **FR-015**: Corrections MUST be data of the existing built-in member map and use existing diagnostics/mapper paths.
  Custom maps without corrections, the public member-rule constructor, by-name/replacement handling and caller JSON
  MUST retain their contracts. Refusals name the original member path and corrective guidance, with no duplicate
  required-input diagnostic on a refused node. Elsa 4 runtime, defaults and outcome behavior MUST NOT change.

| Member/use | Missing/supported empty input | Deliberate literal-null | Literal blank / other boundary |
|---|---|---|---|
| SendHttpRequest/FlowSendHttpRequest.ContentType, Content potentially non-null | Refuse legacy no-body mismatch | Retain delivered no-body refusal | Blank string selects JSON writer; it is not absence |
| Same ContentType, Content known null/unbound | Retain existing no-body behavior | Retain typed null | Retain compatible behavior within existing Content contract |
| FlowSendHttpRequest.ExpectedStatusCodes | Emit `[200]` | Refuse incompatible cleared-list routing | Blank-to-null and explicit `[]` also refuse; never substitute `[200]` |
| Cron.CronExpression | Refuse disabled/no-trigger conversion | Retain refusal | Refuse blank disabled/no-trigger conversion |
| RunJavaScript.Script | Refuse through current required converted-input contract | Retain refusal | Refuse blank/no-work conversion; do not claim the runtime cannot execute blank script |
| Fault.Message | Retain accepted silent conversion | Retain typed null | Retain accepted message-text treatment |
| For.OuterBoundInclusive | Retain true | Emit false | Emit false when the typed blank evaluates null |
| Delay.TimeSpan | Retain one minute | Emit explicit zero duration | Emit zero when the typed blank evaluates null |
| Timer.Interval | Retain one minute | Refuse zero recurrence | Refuse blank/zero/negative/invalid authored interval |
| SendHttpRequest/FlowSendHttpRequest.Method | Retain GET | Retain effective GET fallback | Refuse invalid blank method rather than target whitespace-to-GET normalization |
| WriteHttpResponse.StatusCode | Retain 200 | Refuse raw zero versus target 200 | Refuse zero/negative literals and invalid blank enum text; preserve supported positive status values |

Supported empty wrappers established by the tagged loader are `{}`, `{"typeName":"…"}` and
`{"typeName":"…","expression":{}}`. A resolved Literal wrapper with omitted `value` is deliberate literal-null,
not a defaulted empty wrapper. An `expression:null` or primitive expression is not that empty-wrapper path;
unsupported metadata must be refused or handled by an independently evidenced existing rule, never silently repaired
into a default. The 3.5.3 For-default and Cron differences remain recorded in research; the runtimes are not described
as identical.

### Boundary with S-2

The [active spec1232 proposal](https://github.com/elsa-workflows/elsa-foundation/pull/3726) owns general missing/null/
undefined representation, requiredness, native omitted-input defaults, intrinsic nullability, default-on-null and
workflow policy recording/pinning. This follow-up introduces no policy, marker, coalescing rewrite or policy precedence.

A converter-authored zero TimeSpan preserves a legacy CLR zero value; it is **not** a claim that TimeSpan has an S-2
supported runtime type default. Nullable null and `[]` remain different values; their refusal here is about HTTP
outcome fidelity, not a license for a null policy to replace them. Delivered bare-JSON-null rows are unchanged by this
follow-up; this does not prevent S-2's separately owned SetVariable/SetOutput null amendment. Variable initialization
and missing computed-name rules remain owned by ADR0089/S-2/S-4.

### Acceptance

- **SC-008**: Real-converter/catalog tests cover each changed row and meaningful shape, including supported empty
  wrappers versus Literal-without-value, explicit empty arrays/JSON-list text, literal `"null"`, malformed metadata,
  zero/negative/invalid literals, supported values, original paths, duplicate bindings and caller immutability.
  Delivered bare-null controls remain passing.
- **SC-009**: Actual import/publication/execution proves the corrected For boundary, zero Delay, effective GET,
  ContentType/body distinctions and default `[200]` routing with a local HTTP receiver. Use 200/404/500 controls.
  Cleared-list and other refused uses leave no imported definition/version/publication writes and make no HTTP call.
- **SC-010**: Removing the relevant correction reaches an intended failing assertion; restoration passes. Whole
  touched projects reconcile discovery/execution and preserve every failed/skipped/not-run receipt. Independent
  implementation review continues until a fresh round has no remaining verified findings.
- **SC-011**: Production changes stay in `Extensions/Elsa3`; no runtime/default/outcome/policy contract changes.
  Local affected builds/tests, architecture/maps and fresh current-corpus measurements carry exact-source evidence.
  CI waits are deferred to initiative integration under the dispatch; CI success is never inferred. Formal Code
  Review and coordinator merge remain gates. When S-2 implementation is available, recheck these corrected member
  values with its converter policy option on/off; unavailable S-2 integration is explicitly qualified.

Final follow-up acceptance is recorded by the separate implementation PR; pending checks remain explicitly qualified. Historical spec267 receipts and #3201's distinct receipts do not prove this follow-up.

### Follow-up local validation receipt

Implementation PR [#5440](https://github.com/elsa-workflows/elsa-foundation/pull/5440) targets the initiative. Exact
code/test source `c6a2fc021fa978aa3e083256364b494fd0a6ce57` was checked against baseline
`694debeb44176f3fd61f9059b94f6a6931c162e8`, containing merged #3930 and #3945.

| Gate | Fresh result |
|---|---|
| Complete Mapping project | 1,763 listed/executed/passed; zero failures/skips |
| Complete HTTP/SQLite import integration project | 164 listed/executed/passed; zero failures/skips |
| Architecture | 606 discovered definitions, 621 executed/passed cases; two deferred theories expand to 6 and 11 cases; zero failures/skips |
| Evaluated Release/isolated Debug graphs and generated filters | Both restores and the canonical maps check pass; no solution-filter change |
| Causal controls | Original baseline matrix 50 failures/23 passes; expanded correction removal 52/30; verified review regressions 10/81 and 2/1; restored whole projects pass |
| Independent local review | Correctness, standards/constitution and issue acceptance converge with zero remaining verified findings; all five surviving findings fixed in normal commits |

Owned real hosts prove For counts, explicit zero Delay due time, default GET/no-body behavior, and `[200]` response
paths on 200/404/500. Refused variants leave design/publication rows unchanged and make no HTTP request. The selected
3.6 JSON writer's no-charset header cannot be retained by emitting application/json alone, so a blank ContentType
with potentially non-null Content is refused. Unproven computed/referenced String bodies are refused because they
may be empty and lose body headers on the target.

The fresh, clean, read-only 36-definition corpus at `9c01aa4b53ccbd47e09922d7cc49a694e5f3d2b0` measures **16 imports,
15 publications and 5 observed completions before and after**. All 36 import/publication/completion flags match.
Twenty import refusals, one publication HTTP400 and ten non-completions with incidents remain visible. One original
first-incident observation differs (Running/1 incident versus Faulted/2); a focused repeat on each source remains
Running/1 after a one-second reread. Original observations are retained. These stage counts are not general corpus
execution/value parity. Private payloads and definition IDs are not committed or uploaded.

The initial scratch compile/fixture and dual-diagnostic expectation attempts are retained. The first architecture
run had 619 passes/two checkout setup failures; its maps check reported two filter differences. Reconstructing the
owned native checkout with Linux line endings and the complete tracked inventory, including ignored-but-tracked
files, makes both gates pass without changing repository snapshots or filters. The
[local gate](https://github.com/elsa-workflows/elsa-foundation/pull/5440#issuecomment-7086113646) and
[corpus receipt](https://github.com/elsa-workflows/elsa-foundation/pull/5440#issuecomment-7086123495) retain the exact
commands, results and source boundaries.

T032/spec1232 policy-on/off integration is **NOT RUN**: that implementation is unavailable and separately owned.
Historical Elsa 3 evidence remains exact-tag source inspection, not historical runtime execution. CI is deferred to
initiative integration and no CI pass is claimed. Formal Code Review and coordinator acceptance/merge remain.
