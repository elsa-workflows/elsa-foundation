# Feature Specification: Elsa 3 converter faithful fixes from the JTest measurement

**Feature Branch**: `agent/claude/3051-elsa3-faithful-fixes`

**Created**: 2026-10-19

**Status**: In progress

**Issue**: [#3051](https://github.com/elsa-workflows/elsa-foundation/issues/3051) (epic [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052))

**Input**: The four class-a rows of the 2026-10-19 JTest conversion measurement (comment on #2707): converter
refusals where Elsa 4 behaves the same as Elsa 3.

**Depends on**:
- Spec 236 (#2706, merged): the activity map, its member decisions ([member-map.md](../236-elsa3-activity-map/member-map.md)) and the coverage test.
- Spec 238 (merged): the start-path rule for flowchart triggers.
- Spec 247 (#2717, PR #3028, merged): JavaScript host functions. Without it, JavaScript diagnostics still block some
  of the expected definitions.

**Principle**: faithful or refuse. A member converts only when Elsa 4 behaves the same; otherwise the converter refuses
with a stable diagnostic and guidance. This spec adds no workaround beyond the four rows.

**Owner decision (2026-10-19)**: @j03y-nxxbz approved amending the spec 236 member map for rows 1 and 2: the
"Elsa 4 required members with no Elsa 3 source" list (`WriteLine.text`) and the `customProperties.canStartWorkflow`
row ("elsewhere R3").

## User Scenarios & Testing *(mandatory)*

The actor is a workflow author or operator who converts Elsa 3 workflow definitions (JSON) to Elsa 4, through the
collection import or, later, engine-start reconciliation (spec 235).

### User Story 1 - A WriteLine without text converts (Priority: P1)

An Elsa 3 WriteLine whose Text is unset writes an empty line. The author expects the converted Elsa 4 WriteLine to
write an empty line too, instead of the definition being refused because a required input is unbound.

**Why this priority**: 24 nodes in 7 of the 36 JTest definitions. It blocks most of the simple definitions.

**Independent Test**: Convert a definition with a WriteLine that has no `text`, and with one whose `text` is an empty
literal; both convert, and the Elsa 4 node's `text` is the literal `""`.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 WriteLine with no `text` property, **When** it is converted, **Then** conversion succeeds and the Elsa 4 `text` input is bound to the literal `""`.
2. **Given** an Elsa 3 WriteLine whose `text` is an empty literal, **When** it is converted, **Then** the Elsa 4 `text` input is bound to the literal `""`.
3. **Given** an Elsa 3 WriteLine whose `text` is a non-empty literal or an expression, **When** it is converted, **Then** the value is carried as before.
4. **Given** another activity whose required Elsa 4 input has no Elsa 3 source (for example PublishEvent without an event name), **When** it is converted, **Then** it is still refused with `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND`.

---

### User Story 2 - "Can start workflow" on a non-trigger only picks the flowchart start (Priority: P1)

In Elsa 3, `customProperties.canStartWorkflow` on an activity that is not a trigger (WriteLine, SetVariable,
FlowDecision, Correlate, RunJavaScript, SetOutput, For, …) has one effect: when no trigger fired, the flowchart starts at
the first such activity (`Flowchart.GetStartActivity`, 3.5.3). The converter already uses the flag to choose the
Elsa 4 start node. The author expects the flag to be consumed there and then dropped, instead of the definition being
refused with `ELS3-ACTIVITY-REMOVED-INPUT-BOUND`.

**Why this priority**: 12 nodes in 12 of the 36 JTest definitions.

**Independent Test**: Convert a flowchart whose first activity is a WriteLine marked `canStartWorkflow`, and a Sequence
holding such a WriteLine; both convert, the flowchart's start node is the marked WriteLine, and no node carries a
`CanStartWorkflow` input.

**Acceptance Scenarios**:

1. **Given** a non-trigger marked `canStartWorkflow = true` in a flowchart, **When** it is converted, **Then** conversion succeeds and that activity is the start node if Elsa 3 would start there.
2. **Given** a non-trigger marked `canStartWorkflow = true` outside a flowchart, **When** it is converted, **Then** conversion succeeds without a diagnostic for the flag (Elsa 3 gives it no behaviour there).
3. **Given** a trigger, **When** it is converted, **Then** the existing behaviour holds: HttpEndpoint and Event carry the flag to their `CanStartWorkflow` input; Cron and Timer still require `true` and are refused mid-flow.
4. **Given** a marked non-trigger that is not the start node and that another activity leads into, **When** it is converted, **Then** it is not refused as a misplaced start trigger: the spec 238 start-path rule is about triggers, and a non-trigger never fires.

---

### User Story 3 - Dispatch and ExecuteWorkflow input typed ObjectDictionary converts (Priority: P2)

Elsa 3 types the `input` of DispatchWorkflow and ExecuteWorkflow as `ObjectDictionary`, a string-keyed object. Elsa 4's
input accepts JSON objects. The author expects it to convert like any other dictionary type.

**Why this priority**: 2 nodes in 2 definitions, but those are among the 6 that convert end to end once all rows land.

**Independent Test**: Convert a DispatchWorkflow whose `input` is an expression declared `ObjectDictionary`; it converts
to the Elsa 4 `Inputs` member.

**Acceptance Scenarios**:

1. **Given** a DispatchWorkflow or ExecuteWorkflow `input` declared `ObjectDictionary`, **When** it is converted, **Then** no `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` is raised and the value is carried.
2. **Given** a JSON-only member bound to an expression declared as a non-JSON type (for example `Int32`), **When** it is converted, **Then** it is still refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`.

---

### User Story 4 - Values normalization already fitted are not refused again by value-flow lowering (Priority: P1)

Once rows 1–3 are fixed, value-flow lowering runs and raises `VF-IMP-008` on values whose Elsa 3 declared type name
differs from the Elsa 4 member type although the value fits the Elsa 4 contract:

- WriteHttpResponse `StatusCode` declared `HttpStatusCode` (an enum name normalization already turns into an integer) against Elsa 4 `Int32` (51 nodes);
- WriteHttpResponse `Headers` declared `HttpHeaders` (a dictionary of string arrays) against Elsa 4 `IDictionary<string, string[]>` (51 nodes);
- HttpEndpoint `SupportedMethods` declared `List<String>` against Elsa 4 `ICollection<string>` (12 nodes).

The author expects these to convert.

**Why this priority**: 114 diagnostics; without it no HTTP definition converts once rows 1–3 land.

**Independent Test**: Convert a WriteHttpResponse with `statusCode` typed `System.Net.HttpStatusCode` (literal
`Accepted`) and `responseHeaders` typed `HttpHeaders`, and an HttpEndpoint with `supportedMethods` typed `List<String>`;
all convert, and the status code arrives as `202`.

**Acceptance Scenarios**:

1. **Given** the three cases above as literals, **When** they are converted, **Then** no `VF-IMP-008` is raised and the values are carried (the status code as its integer).
2. **Given** an expression bound to a member whose declared Elsa 3 type is genuinely incompatible with the Elsa 4 member type (for example `Boolean` for an `Int32` member), **When** it is converted, **Then** `VF-IMP-008` is still raised.

---

### Edge Cases

- A WriteLine `text` bound to an expression or a memory reference is never replaced by the default.
- A non-trigger marked `canStartWorkflow` that is not the start node: Elsa 3 ignores the flag, so the converter drops it without a diagnostic.
- An activity resolved without a map entry (an alias, a verbatim catalog key or a plan replacement) is unchanged by row 2.
- An `HttpStatusCode` value that is an expression: unchanged, still refused by the narrowed contract unless declared as an integer type.
- `List<Object>` bound to an `ICollection<string>` member is still a mismatch.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When an Elsa 3 WriteLine leaves `text` unset or empty, the converter MUST emit the Elsa 4 `text` input as the literal `""` (R2 default flip) and MUST NOT raise `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND` for it.
- **FR-002**: The required-input check MUST still refuse every other required Elsa 4 input with no Elsa 3 source, as listed in the member map.
- **FR-003**: `customProperties.canStartWorkflow = true` on an activity whose map entry declares no `CanStartWorkflow` rule (a non-trigger) MUST be consumed by flowchart start resolution and then dropped, without `ELS3-ACTIVITY-REMOVED-INPUT-BOUND` and without `ELS3-ACTIVITY-GRAPH-START-TRIGGER-MISPLACED`.
- **FR-004**: The flag's handling on HttpEndpoint, Event, Cron and Timer MUST NOT change.
- **FR-005**: `ObjectDictionary` MUST count as a JSON type for JSON-only members.
- **FR-006**: Value-flow lowering MUST judge a property's type against the value normalization stored for the Elsa 4 member, so a value that normalization narrowed or carried into the Elsa 4 contract (the three cases of User Story 4) is not refused with `VF-IMP-008`.
- **FR-007**: A genuinely incompatible declared type MUST still be refused with `VF-IMP-008`.
- **FR-008**: The spec 236 member map, the Mapping README and the activity-map coverage test MUST be amended in the same unit for rows 1 and 2.
- **FR-009**: Each row MUST have a test in the spec 236 test suite (`Extensions/Elsa3/tests/Mapping/Tests`) that converts against the real Elsa 4 activity catalog.

### Key Entities

- **Member rule**: a spec 236 activity-map decision (R1–R9, W) for one Elsa 3 member.
- **Normalized value**: the typed value normalization stores under the Elsa 4 member key, after renaming, narrowing or carrying.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Each of the four rows converts in a dedicated test, and the existing spec 236–247 test suite stays green.
- **SC-002**: In a throwaway check against the JTest definitions (not committed), these 6 convert end to end: simple-write-hello-world, performance-baseline-dispatch-sub, performance-bigdata, performance-commit-after-every-activity, composition-bulk-dispatch-sub-workflow, storage-drivers-variables-sub-workflow.
- **SC-003**: No diagnostic code is added, removed or renamed.

## Assumptions

- Elsa 4 publish accepts a WriteLine `text` bound to the literal `""` (measured on 2026-10-19), and its WriteLine writes an empty line for it.
- The only Elsa 3 triggers in the built-in map are HttpEndpoint, Event, Cron and Timer; each declares a `CanStartWorkflow` rule. The coverage test pins this.
- Out of scope: the WriteHttpResponse structured body (#3050), JavaScript limits (#3026, #3012), class-b refusals and #1152 gaps.
