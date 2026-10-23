# Research: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

## R-1 What Elsa 3 ran a null member with

**Decision**: the per-member table in the spec, from elsa-core `release/3.6.3`, checked against the `3.5.3` tag.

**Evidence**:
- `JsonActivityConstructorContextHelper.CreateActivity` (`Elsa.Workflows.Core/Models/ActivityConstructorContext.cs`)
  deserializes the activity JSON into its class with System.Text.Json. A JSON `null` on an `Input<T>` property sets the
  property to null, replacing its initializer. `InputJsonConverter<T>` is not called for a null token.
- `EvaluateInputPropertyCoreAsync` (`Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs`, lines 87–106 in
  3.6.3, identical in 3.5.3): a null wrapped input with a declared default gets a `Literal(defaultValue)`; otherwise
  the value stays null, and `context.Get(input)` returns `default(T)`.
- The declared default comes from `PropertyDefaultValueResolver`: `[Input(DefaultValue)]` or its
  `DefaultValueProvider`, else null.
- Synthetic inputs (workflow-as-activity) skip a null member in `ReadSyntheticInputs`; synthetic outputs skip a null in
  `ReadSyntheticOutputs`.
- Per-activity reads confirmed by hand: `For.Step` (`context.Get(Step)` → 0; the initializer `new(1)` is replaced),
  `ExecuteWorkflow.WaitForCompletion` (no declared default → `false`), `GetHeaders`
  (`input.MemoryBlockReference()` on a null input throws).
- 3.5.3 differs only for `For.OuterBoundInclusive` (no declared default there). The maintainer chose 3.6.3.

## R-2 Where the rule applies

**Decision**: in the normaliser's member pass (`Elsa3ActivityNormalizer.ApplyRule` and `BindByName`), before the
member rule's own handling. The state mapper (`Elsa3ActivityToState.Map`) then never sees a bare JSON `null`.

**Rationale**: the normaliser is where member rules, Elsa 3 defaults (`ApplyDefaults`), the required-input check and
refusals live. `Map` only turns typed values into `ArgumentState`s, and has no access to the rule or the diagnostics.

**Alternatives considered**: deciding in `Map`. Rejected: refusals would have to be raised after conversion, and the
defaults already emitted by `ApplyDefaults` would race with it.

## R-3 Representing a typed null

**Decision**: the normaliser stores `{ "typeName": <Elsa 4 alias>, "expression": { "type": "Literal", "value": null } }`.
`Elsa3ActivityToState` maps a literal whose value is null to `new ArgumentValue(null, "Literal")`.

**Evidence**: publication's `CompileLiteralInput` → `ConvertLiteral(null)` returns null; `ValidateNull` accepts it for a
nullable input and the binding becomes `ValueEnvelope.Null` (`RuntimeInputBindingCompiler.cs`). The runtime accepts an
explicit null for a nullable input (`RuntimeActivityInputMaterializer.ValidatePresence`). The design-time required-input
validator does not apply, because every typed-null member is optional in Elsa 4 (checked by the coverage test, R-7).

**Today**: `Elsa3TypedValues.TryGetExpression` rejects an expression whose value is null, so `Map` cannot read the
shape. `Map` gets its own check for it.

## R-4 Refusals and the existing required-input check

**Decision**: one new error code, `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`, for every "refuse" row, with the row's reason in
the message.

**Rationale**: `CheckRequiredInputs` already reports `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND` when a required Elsa 4
input stays unbound, so several refuse rows (Path, Url, EventName, WorkflowDefinitionId, Script, Cron, the intrinsic
values) are refused today, with a generic message. The new refusal is raised in the member pass and marks the node
failed, so `CheckRequiredInputs` (which skips a failed node) does not report the same member again. The new message
says why Elsa 3's behaviour cannot be reproduced. `For.Step`, `ForEach.Items`, the header members and `ContentType`
are not required in Elsa 4 and were not refused before.

**Alternatives considered**: reusing `REQUIRED-INPUT-UNBOUND` for the required rows. Rejected: a code keeps one meaning
(`Elsa3ActivityDiagnosticCodes`), and "Elsa 3 hung, faulted, dropped data or Elsa 4 cannot represent it" is not
"unbound".

## R-5 A deliberate null (an input holding a literal null)

**Finding**: the spec's FR-005 assumed a deliberate null converts as an explicit null today. It does not:
`Elsa3TypedValues.IsBound` counts it as unbound, so the member pass treats it like a JSON `null`, `ApplyDefaults` emits
an R2 default over it, and `Map` drops it.

**Decision**: the member pass reads a deliberate null exactly as a JSON `null` (it rewrites it to JSON `null` first).
This keeps today's behaviour (unbound) and gives it the same table. Elsa 3 ran a deliberate null differently from a
JSON `null` only for members with a declared default (For.OuterBoundInclusive, Delay, Timer, SendHttpRequest.Method,
WriteHttpResponse.StatusCode, FlowSendHttpRequest.ExpectedStatusCodes): it ran with null or the type default, not the
declared default. That gap existed before this spec; no corpus definition has a deliberate null. It is added to #3281.

## R-6 Recording the null conversion per member

**Decision**: `Elsa3MemberRule` gains `OnNull`, an `Elsa3NullConversion` (`NoBinding`, `TypedNull`, `Emit(value)` or
`Refuse(reason)`, optionally only when another member is bound). Every input rule of kind R1 Carry or R5 Narrowed
contract records it. An R2 default flip, and an R5 rule with an Elsa 3 default (Timer), already record Elsa 3's value
and keep emitting it on null, unless they record `OnNull` themselves (SendHttpRequest.ContentType).

**Rationale**: the spec's FR-008 asks for a recorded conversion per member, with a test that fails on a gap. Deriving
it from the Elsa 4 contract would hide the Elsa 3 evidence the reviewer asked for.

## R-7 Coverage test against the real catalog

**Decision**: `Elsa3ActivityMapCoverageTests` gains two checks:
- every input rule of kind Carry or Narrowed contract has `OnNull`, or an Elsa 3 default it emits;
- against the CLR-reconciled real catalog: a `TypedNull` member's Elsa 4 input accepts null and is not required; a
  `NoBinding` member's Elsa 4 input is not required. This checks the table's Elsa 4 column mechanically.

## R-8 SendHttpRequest.ContentType

**Decision**: `OnNull = Refuse(…, whenBound: "Content")`. With content bound, Elsa 3 sent no body: refuse. Without
content, a typed null (Elsa 3 sent no body either way). The existing R2 rule (`application/json` when content is bound
and ContentType is absent) does not change: absent members are #3281.

## R-9 Correlate and SetName

**Decision**: `OnNull = Emit("")`. Elsa 4's intrinsic executor reads an empty string as null and clears the
correlation id or removes the instance name (`WorkflowIntrinsicExecutor.ReadOptionalString`, `ApplyInstanceName`).
`CheckRequiredInputs` counts a blank literal as unbound, so `IsEmittedDefault` also accepts the `OnNull` value, as it
does for WriteLine's R2 empty string.

## R-10 Null outputs and members resolved by name

**Decision**: a null output (R1 Carry output) gets no binding. On an alias, catalog key or workflow-as-activity
replacement (`BindByName`), a null member gets no binding, and the Elsa 4 contract's default applies. That is faithful
for a replacement, because Elsa 3 skipped a null synthetic member. For an alias or a catalog key, Elsa 3 ran its own CLR
activity with that activity's default, which the converter does not know; the result matches only when the Elsa 4
default is the same (review finding, recorded as a known limit in the spec's edge cases). Lost outputs (R4), removed
inputs (R3) and restrictions (R6) already handle null.

## R-11 Measurement

**Decision**: the same throwaway import-host test as the baseline (import each of the 36 definitions on its own,
publish each that converts, start each published one without an HTTP trigger), run on this branch and deleted.
