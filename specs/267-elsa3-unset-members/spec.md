# Feature Specification: Elsa 3 converter: unset activity members keep Elsa 3's value

**Feature Branch**: `agent/claude/3200-elsa3-null-input`

**Created**: 2026-10-22

**Status**: Draft

**Issue**: [#3200](https://github.com/elsa-workflows/elsa-foundation/issues/3200), part of epic
[#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052)

**Input**: An Elsa 3 activity property that is JSON `null` converts without a diagnostic. Publishing the imported
version then fails with `400 Activity node '<node>' input 'possibleOutcomes' does not declare an expression type`. In
the JTest corpus this blocks 2 of the 8 converting definitions from publishing (composition-bulk-dispatch-sub-workflow,
performance-bigdata). Both have an Elsa 3 RunJavaScript with `"possibleOutcomes": null`.

**Depends on**: spec 236 (#2706, merged), the activity map and its member rules. Spec 264 (#3122, merged) fixed the
same mistake for workflow variables.

**Principle**: faithful or refuse. A member converts only when Elsa 4 behaves as Elsa 3 did. Otherwise the converter
refuses with a stable diagnostic and guidance on what to change in the Elsa 3 workflow.

**Boundary**: converter only (`Extensions/Elsa3`) and the spec 236 member map. Elsa 4 activity defaults, publication
and the runtime do not change. Elsa 4's defaults were chosen for Elsa 4 and stay as they are.

## Root cause

The converter keeps a JSON `null` member as a binding that holds nothing and names no expression language. Publication
compiles every binding it is given and reads one with no expression language as an expression, which it refuses. A
null output fails the same way, because publication requires an output binding to name a variable.

The converter already knows what an unset member meant in Elsa 3 for an absent member: the activity map records Elsa
3's effective value wherever it differs from Elsa 4's default, and the converter emits it. A `null` member skips that
rule today, because it is kept as a binding instead of being treated as unset.

## What Elsa 3 did with an unset member (3.6.3 and 3.5.3)

An Elsa 3 activity is loaded from JSON into its class. Three shapes reach the converter:

| Elsa 3 JSON | What Elsa 3 ran with |
|---|---|
| Member absent | The property's initial value in the activity class |
| `"member": null` | The input's declared default, otherwise the type's default (`false`, `0`, `null`) |
| An input holding a literal null | An explicit null, with no default |

Elsa 3's exporter writes every member, so its exports carry `null` for a member the author never set. An absent member
appears only in hand-written JSON. For every member the converter maps, the first two shapes give the same result,
except where this spec refuses (FR-006). A workflow-as-activity input that is `null` or absent is skipped in both
cases. A `null` output is not captured.

3.5.3 and 3.6.3 differ in one place: a `null` `For.OuterBoundInclusive` reads as `false` in 3.5.3 and `true` in 3.6.3.
This spec follows 3.6.3.

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

## Clarifications

### Session 2026-10-22 (maintainer, before specification)

- Q: Does a JSON `null` member mean something different from an absent one? → A: No. Both are *unset*, and an unset
  member keeps the value Elsa 3 gave it. Users must not find that a default changed.
- Q: Should Elsa 4's own defaults move to Elsa 3's where they differ? → A: No. Elsa 4's defaults were chosen for the
  Elsa 4 runtime (HttpEndpoint's response mode already defaults to synchronous). The converter emits Elsa 3's value
  where Elsa 4's default differs.
- Q: Should the converter warn when it emits Elsa 3's value? → A: No. Nothing changes for the user, so there is nothing
  to warn about. A warning or refusal is only for an unset member whose Elsa 3 behaviour Elsa 4 cannot reproduce.
- Q: An unset member where Elsa 3 hung, faulted, dropped data or rejected the activity? → A: Refuse, with one stable
  code that names the member and how to fix it in the Elsa 3 workflow.
- Q: An unset `Fault.Message`? Elsa 3 faulted with .NET's generic exception text; Elsa 4 says "The workflow
  faulted." → A: Convert silently. Only the human-readable text differs; it is not worth porting or warning about.
- Q: `For.OuterBoundInclusive` set to `null`? → A: Follow 3.6.3: `true`.
- Q: An intrinsic value that is unset (SetVariable, SetOutput, Correlate, SetName)? Elsa 3 sets the target to null;
  Elsa 4 refuses a literal null for an intrinsic value at publication. → A: Emit an explicit null where Elsa 4 can
  publish one, otherwise refuse. Elsa 4 reads an empty string as null for the correlation id and the instance name, so
  those two convert to an empty string. A variable or workflow output cannot be set to null by a literal, so those two
  are refused.
- Q: ExecuteWorkflow's `WaitForCompletion`? → A: Out of scope. Spec 236 maps ExecuteWorkflow to a Dispatch that waits,
  with its own behaviour warning; that is a mapping, not a default.
- Q: RunJavaScript's `arguments` faulting when unset (#3195)? → A: Separate issue; it is an Elsa 4 default defect.
- Q: Track the absent-member fixes in a separate issue? → A: No. Everything is under #3200 and this spec.

## User Scenarios & Testing *(mandatory)*

The actor is a workflow author or operator who brings Elsa 3 workflow definitions (JSON) into Elsa 4 through the
collection import, then publishes and runs them.

### User Story 1 - A workflow with null activity members publishes and runs as in Elsa 3 (Priority: P1)

An author imports an Elsa 3 workflow whose activities carry `null` members, as every Elsa 3 export does for members the
author never set, and none of them is a member FR-006 refuses. The import succeeds, publishing succeeds, and the run
behaves as the workflow did in Elsa 3 (FR-002 to FR-004).

**Why this priority**: this is #3200. Every Elsa 3 export has `null` members, and today they block publishing.

**Independent Test**: import, publish and run a workflow whose RunJavaScript has `"possibleOutcomes": null` and
`"result": null`, and whose WriteLine has `"text": null`.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 RunJavaScript with `"possibleOutcomes": null`, **When** it is imported and published, **Then**
   publishing succeeds and the run completes through the activity's `Done` path, as in Elsa 3.
2. **Given** an Elsa 3 activity output that is `null`, **When** it is imported and published, **Then** publishing
   succeeds and nothing is captured, as in Elsa 3.
3. **Given** an Elsa 3 WriteLine with `"text": null`, **When** it runs, **Then** it writes an empty line, as in Elsa 3.

---

### User Story 2 - An unset member keeps Elsa 3's value where Elsa 4's default differs (Priority: P1)

An author imports an Elsa 3 workflow whose members are unset (`null` or absent) on activities whose Elsa 4 default
differs from Elsa 3's. The converted workflow runs with Elsa 3's values, with no warning, because nothing changed.

**Why this priority**: a silently changed default changes what a migrated workflow does, which is the failure the
maintainer ruled out.

**Independent Test**: convert each member below with `null` and with the member absent, and check that the converted
node carries Elsa 3's value.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 Delay with `"timeSpan": null`, **When** it converts, **Then** it waits one minute, as in Elsa 3.
2. **Given** an Elsa 3 For with `"outerBoundInclusive": null`, **When** it converts, **Then** its end bound is
   inclusive, as in Elsa 3.6.3.
3. **Given** an Elsa 3 FlowSendHttpRequest with `"expectedStatusCodes"` unset, **When** it converts, **Then** it
   expects status 200, and the Elsa 3 connections from its `200` and `Unmatched status code` outcomes convert.
4. **Given** an Elsa 3 Correlate or SetName with an unset value, **When** it runs, **Then** it clears the correlation
   id or the instance name, as in Elsa 3.
5. **Given** any of these conversions, **When** the import is analysed, **Then** no warning is reported for them.

---

### User Story 3 - An unset member Elsa 4 cannot reproduce is refused with guidance (Priority: P2)

An author imports an Elsa 3 workflow with an unset member where Elsa 3 hung, faulted, dropped data or rejected the
activity. The converter refuses that activity and says which member to set, or that the activity can be removed.

**Why this priority**: faithful or refuse. Converting these would change what the workflow does without telling anyone.

**Independent Test**: convert each refused member unset, and check for the refusal code, the member and the guidance.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 For with `"step": null` (Elsa 3 ran with a step of 0, so the loop never advanced), **When** it
   converts, **Then** it is refused, naming `step`.
2. **Given** an Elsa 3 SendHttpRequest with bound content and an unset content type (Elsa 3 sent no body), **When** it
   converts, **Then** it is refused, naming `contentType`, whether the member is `null` or absent.
3. **Given** an Elsa 3 SetVariable with an unset value, **When** it converts, **Then** it is refused, naming `value`.

### Edge Cases

- An input holding a literal null (an explicit expression with a null value) is not unset: it converts as an explicit
  null, as today.
- An empty string or an empty JSON array or object is not `null`. It converts by the existing rules, except for the two
  members FR-006 refuses when *blank*: `Cron.CronExpression` and `RunJavaScript.Script`. Blank means unset, empty or
  whitespace only; Elsa 3 treated all three the same for these two members.
- A member the map removes (R3) or restricts (R6) already treats `null` as absent. That does not change.
- A member that is unset on an activity resolved by name (an alias, a catalog key or a workflow-as-activity reference)
  has no recorded Elsa 3 default. It gets no binding, and the Elsa 4 contract's default applies. Elsa 3 skipped such a
  member too.
- A member whose Elsa 4 input is required and has no default, and whose Elsa 3 value is not recorded in the map, is
  refused (FR-006) rather than left to fail at publication.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A JSON `null` activity member MUST be treated exactly as an absent member: as *unset*.
- **FR-002**: An unset input whose Elsa 3 value equals the Elsa 4 contract's default (or whose Elsa 4 input has no
  default and accepts absence) MUST get no binding.
- **FR-003**: An unset input whose Elsa 3 value differs from the Elsa 4 default MUST be bound to Elsa 3's value as a
  literal. The activity map is the one record of those values. The members are:

  | Elsa 3 member | Converted value |
  |---|---|
  | For.OuterBoundInclusive | `true` (3.6.3) |
  | Delay.TimeSpan | 1 minute |
  | Timer.Interval | 1 minute |
  | WriteLine.Text | empty string |
  | Fault.Code, Fault.Category, Fault.FaultType | `0`, `General`, `System` |
  | FlowSendHttpRequest.ExpectedStatusCodes | `[200]` (new in the map) |
  | Correlate.CorrelationId, SetName.Value | empty string (Elsa 4 reads it as null) |

- **FR-004**: An unset output MUST get no binding (nothing is captured).
- **FR-005**: An input holding a literal null MUST keep converting as an explicit null.
- **FR-006**: An unset member whose Elsa 3 behaviour Elsa 4 cannot reproduce MUST be refused with one new stable
  diagnostic code. The message names the member and the reason; the guidance says to set a value in the Elsa 3
  workflow, or to remove the activity. The members are:

  | Elsa 3 member, when unset | Elsa 3 behaviour |
  |---|---|
  | For.Step | step 0: the loop never advances |
  | ForEach.Items | faults |
  | SendHttpRequest / FlowSendHttpRequest.RequestHeaders | faults |
  | WriteHttpResponse.ResponseHeaders | faults |
  | SendHttpRequest / FlowSendHttpRequest.ContentType, when Content is bound (`null` or absent) | the body is not sent |
  | Cron.CronExpression, blank (unset, empty or whitespace only) | no trigger; the activity completes |
  | RunJavaScript.Script, blank (unset, empty or whitespace only) | does nothing |
  | HttpEndpoint.Path, SendHttpRequest.Url, Event.EventName, PublishEvent.EventName, DispatchWorkflow / ExecuteWorkflow.WorkflowDefinitionId, SetOutput.OutputName | rejects the activity as missing a required input |
  | SetVariable.Value, SetOutput.OutputValue | sets the variable or workflow output to null, which Elsa 4 cannot publish as a literal |

- **FR-007**: The converter MUST NOT report a warning for an unset member it converts under FR-002, FR-003 or FR-004.
  An unset `Fault.Message` converts silently: Elsa 4's fault message text differs from Elsa 3's, and nothing else does.
- **FR-008**: The SendHttpRequest content-type default the converter emits today (`application/json` when content is
  bound) MUST be removed: FR-006 refuses that case instead.
- **FR-009**: The spec 236 member map document MUST record each unset rule above next to the member it applies to.
- **FR-010**: Elsa 4 activity defaults, publication and the runtime MUST NOT change.

### Key Entities

- **Unset member**: an Elsa 3 activity member that is JSON `null` or absent.
- **Elsa 3 effective value**: what Elsa 3 ran an unset member with: the class's initial value, the input's declared
  default, or the type's default.
- **Activity map**: the converter's table of Elsa 3 members, the Elsa 4 keys they map to, and Elsa 3's effective value
  where it differs from Elsa 4's default.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: composition-bulk-dispatch-sub-workflow and performance-bigdata no longer stop at the `possibleOutcomes`
  error at publish. Each is measured again and its next blocker, if any, is recorded here and in epic #3052.
- **SC-002**: No JTest definition that converts on `main` (8) stops converting.
- **SC-003**: An integration test imports, publishes and runs a workflow with `null` activity members, and the run
  behaves as in Elsa 3.
- **SC-004**: Every member in FR-003 and FR-006 has a test with the member `null`, and a test with it absent where the
  two used to convert differently.
- **SC-005**: The converted workflows of the corpus carry no warning that this spec introduced.
- **SC-006**: The spec 236 member map document has an unset rule for every member in FR-003 and FR-006 (FR-009).
- **SC-007**: The change touches no Elsa 4 activity, publication or runtime code (FR-010). The diff outside
  `Extensions/Elsa3` and `specs/` is empty, and no Elsa 4 test changes.

## Assumptions

- Elsa 3's behaviour is taken from the release/3.6.3 source, checked against the 3.5.3 tag. They differ only for
  `For.OuterBoundInclusive`, settled above.
- The JTest corpus's tool version is 3.5. No corpus definition has a `null` `For.OuterBoundInclusive`.
- The measurement method matches specs 247, 248, 262 and 264: throwaway tests, deleted before the commit.
- Out of scope: Elsa 4 default changes, #3195 (RunJavaScript `arguments`), #3201, #3202, ExecuteWorkflow's mapping
  (spec 236), engine-start reconciliation (spec 235).
