# Feature Specification: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

**Feature Branch**: `agent/claude/3200-elsa3-null-input`

**Created**: 2026-10-22

**Status**: Implemented

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

## Measured after (JTest corpus, 2026-10-23, this branch on `main` at `9613cf4ad`)

Same throwaway test as the baseline, deleted afterwards.

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

**Independent Test**: import, publish and run a workflow whose RunJavaScript has `"possibleOutcomes": null` and
`"result": null`, and whose WriteLine has `"text": null`.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 RunJavaScript with `"possibleOutcomes": null`, **When** it is imported and published, **Then**
   publishing succeeds and the run completes through the activity's `Done` path, as in Elsa 3.
2. **Given** an Elsa 3 activity output that is `null`, **When** it is imported and published, **Then** publishing
   succeeds and nothing is captured, as in Elsa 3.
3. **Given** an Elsa 3 WriteLine with `"text": null`, **When** it runs, **Then** it writes an empty line, as in Elsa 3.

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

- An input holding a literal null (the deliberate-null shape) converts by the same table as a JSON `null` (FR-005).
- An empty string or an empty JSON array or object is not `null`. It converts by the existing rules; blank strings are
  #3281.
- A member that is absent converts by the existing rules; absent members whose value differs from Elsa 3's are #3281.
- A `null` member on an activity resolved by name (an alias, a catalog key or a workflow-as-activity reference) has no
  recorded Elsa 3 default. It gets no binding, and the Elsa 4 contract's default applies, as the reusable's did in
  Elsa 3.
- A `null` member of a mapped activity that the table above does not list is a gap in the table. The activity-map
  coverage test fails on it (FR-008); at run time such a member gets no binding.

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
- **FR-005**: An input holding a literal null MUST convert as a JSON `null` member does, by the same table. (Corrected
  in planning: the converter already reads it as unbound; research R-5.)
- **FR-006**: A `null` member whose Elsa 3 behaviour Elsa 4 cannot reproduce or represent MUST be refused with one new
  stable diagnostic code (the table's "refuse" rows). The message names the member and the reason; the guidance says
  to set a value in the Elsa 3 workflow, or to remove the activity.
- **FR-007**: The converter MUST NOT report a warning for a `null` member it converts under FR-002, FR-003 or FR-004.
- **FR-008**: Each member of a mapped Elsa 3 activity MUST have a recorded `null` conversion. A test fails when a
  mapped member has none.
- **FR-009**: The spec 236 member map document MUST record each member's `null` conversion next to the member.
- **FR-010**: Elsa 4 activity defaults, publication and the runtime MUST NOT change. Absent and blank members do not
  change in this spec (#3281).

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
- **SC-004**: Every row of the per-member table has a test with the member `null`.
- **SC-005**: The converted workflows of the corpus carry no warning that this spec introduced.
- **SC-006**: The spec 236 member map document records the `null` conversion of every mapped member (FR-009).
- **SC-007**: The change touches no Elsa 4 activity, publication or runtime code (FR-010). The diff outside
  `Extensions/Elsa3` and `specs/` is empty, and no Elsa 4 test changes.

## Assumptions

- Elsa 3's behaviour is taken from the release/3.6.3 source, checked against the 3.5.3 tag. They differ only for
  `For.OuterBoundInclusive`, settled above.
- The JTest corpus's tool version is 3.5. No corpus definition has a `null` `For.OuterBoundInclusive`.
- The measurement method matches specs 247, 248, 262 and 264: throwaway tests, deleted before the commit.
- Out of scope: Elsa 4 changes (#3280), absent and blank members (#3281), #3195 (RunJavaScript `arguments`), #3201,
  #3202, ExecuteWorkflow's mapping (spec 236), engine-start reconciliation (spec 235).
