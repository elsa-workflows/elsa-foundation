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

## R-12 #3281 source audit and shape boundary

This follow-up's source baseline is tag `3.6.3` at `7eac7de6955718aea35870be125d54f080fc55dc`, checked against
`3.5.3` at `7646867ac5365af35f0507fde72b2df98790bec9`. Exact source files were read from the existing local
`elsa-core` Git objects; no historical build or runtime execution is claimed by this audit.

- `src/modules/Elsa.Workflows.Core/Serialization/Converters/InputJsonConverter.cs`: a supported empty wrapper with
  no resolvable expression returns a null Input; a Literalnull is an actual wrapped expression. Do not treat arbitrary
  malformed expression metadata as a supported empty wrapper.
- `Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs` in that module: a declared default replaces
  only a null wrapped Input. A present wrapped expression is evaluated, even when its result is null. The generic
  `ExpressionExecutionContextExtensions.Get`/`ObjectConverter.ConvertTo<T>` path then returns the type default.
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs`: blank strings remain strings for a String target;
  conversion to other types can return null, while enum parsing can fail before the blank-string fallback. Thus a
  literal blank string and a missing input are not interchangeable, and a blank status enum is not assumed to be zero.
- `src/modules/Elsa.Http/Activities/SendHttpRequestBase.cs`: Method additionally falls back to GET on null, while a
  non-null blank method reaches `HttpMethod`. Content is attached only when ContentType and Content are non-null;
  `SelectContentWriter` selects `JsonContentFactory` for a whitespace ContentType. An explicit blank string is not
  the no-body behavior of an absent/null ContentType.
- `src/modules/Elsa.Http/Activities/FlowSendHttpRequest.cs`: the input default provider supplies `[200]`; evaluated
  null/empty ExpectedStatusCodes instead produce no numeric outcome. Preserve explicit empty lists.
- `src/modules/Elsa.Scheduling/Activities/Delay.cs`/`Timer.cs`: declared defaults are one minute; a deliberate null
  reads as zero TimeSpan. Elsa 4's recurring interval calculator requires a positive interval, so zero must be refused
  instead of silently selecting one minute.
- `src/modules/Elsa.Http/Activities/WriteHttpResponse.cs`: status is read from the typed input and assigned to the
  actual HTTP response. Elsa 4's current WriteHttpResponse explicitly normalizes values below one to 200; blindly
  emitting zero would therefore not preserve the legacy invalid-status behavior.
- `src/modules/Elsa.Scheduling/Activities/Cron.cs`: 3.6.3 completes and registers no trigger for blank expressions.
  3.5.3 lacks that disabled-path handling. This difference is recorded rather than described as identical behavior;
  the follow-up keeps the already selected 3.6.3 baseline.
- `src/modules/Elsa.Expressions.JavaScript/Activities/RunJavaScript/RunJavaScript.cs` (3.6.3) is under
  `src/modules/Elsa.JavaScript/Activities/RunJavaScript/RunJavaScript.cs` in 3.5.3. Both treat no script as no work.

The earlier 267 research deliberately retained literal-null/default conflation and deferred it to #3281. The follow-up
overrides only those named built-in cases; the bare JSON-null table remains delivered. Fault.Message's accepted
silent difference remains unchanged. Current runtime/native/corpus acceptance is NOT RUN at this planning checkpoint.

## R-13 Current source recheck and initiative integration

Exact legacy sources were rechecked at 3.6.3 `7eac7de6955718aea35870be125d54f080fc55dc` and
3.5.3 `7646867ac5365af35f0507fde72b2df98790bec9`. Current-target comparisons use initiative
`18dd3aefb98bb0d3c8916ad6b71be69a9fc3699f`. These primary-source reads are not historical runtime execution.

### Loader and effective values

Both tagged InputJsonConverter implementations return a null Input for `{}`, `{"typeName":"…"}` and a typed wrapper
with `expression:{}`. A resolved Literal descriptor constructs an expression even when `value` is omitted; that
expression evaluates null. Null/primitive expression metadata is not the empty-object path. Unsupported metadata
must not be silently repaired. The input-evaluation extension supplies a declared default only for a null wrapped
Input, not for a present expression result. Generic Get/conversion then supplies the CLR zero value for a null
value-type result. String blanks remain strings for String targets. Enum parsing precedes the whitespace fallback,
so blank HTTP-status text is not assumed to become zero.

Sources: [input converter, 3.6.3](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Workflows.Core/Serialization/Converters/InputJsonConverter.cs),
[input converter, 3.5.3](https://github.com/elsa-workflows/elsa-core/blob/7646867ac5365af35f0507fde72b2df98790bec9/src/modules/Elsa.Workflows.Core/Serialization/Converters/InputJsonConverter.cs),
[input evaluation](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs),
[ObjectConverter](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs).

### HTTP corrections to the earlier proposal

Both tags attach request Content only when ContentType **and Content are non-null**. Empty String Content belongs in
that boundary. Blank ContentType selects the JSON writer/application-json fallback; absent/null ContentType does not.
Both writers send String content unchanged; 3.6.3 uses RawStringContent and 3.5.3 StringContent. Method also falls back
to GET for null, while non-null whitespace reaches HttpMethod without the target's normalization.

The implementation recheck follows the 3.6.3 JSON factory into
[RawStringContent](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Http/ContentWriters/RawStringContent.cs).
It preserves the media type with no charset parameter. The current target uses StringContent and appends
`charset=utf-8`. Therefore blank ContentType with potentially non-null Content is refused under FR-012's
faithful-or-refuse boundary, including nonempty text; emitting application/json alone would not preserve the header.
The target also omits empty String Content and its headers. A nonliteral/referenced String body has no established
nonempty guarantee, so that use is refused rather than accepting the same possible omission. Known-null/unbound
Content retains its no-body path. These are source-inspection findings, not historical runtime executions.

Both FlowSendHttpRequest implementations supply `[200]` for a null wrapped ExpectedStatusCodes Input. A present
null/empty result selects **Done for every response**, including 404/500; a nonempty list adds numeric/unmatched
alongside Done. The target's empty-list path instead selects Done for 2xx and Failed for non-2xx. The merged converter
maps legacy Done through ValueOutcomes, whose empty case is only Done; Failed represents connection failure.
**Decision:** the earlier proposed typed-null/empty-list emission is insufficient. Preserve the source distinction
and refuse these cleared-list uses under the existing faithful-or-refuse rule. Emit `[200]` only for defaulted
shapes. Add 200/404/500 controls and change no target outcome behavior.

Both legacy response activities assign the raw integer status. Literal null becomes enum zero; authored 0/negative
literals also stay raw. The target normalizes values below one to 200. **Decision:** refuse these values, invalid
blank enum text and unproven computed ranges; retain supported positive values. No specific historical HTTP-server
exception is claimed without a host witness.

Sources: [request execution](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Http/Activities/SendHttpRequestBase.cs#L253),
[FlowSend, 3.6.3](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Http/Activities/FlowSendHttpRequest.cs#L25),
[FlowSend, 3.5.3](https://github.com/elsa-workflows/elsa-core/blob/7646867ac5365af35f0507fde72b2df98790bec9/src/modules/Elsa.Http/Activities/FlowSendHttpRequest.cs),
[JSON writer](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Http/ContentWriters/JsonContentFactory.cs#L20),
[response status assignment](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Http/Activities/WriteHttpResponse.cs#L111).
Target checks: `Essentials/src/Activities/Http/Activities/SendHttpRequest.cs`, `WriteHttpResponse.cs`,
`Elsa3BuiltInActivityMap.Entries.cs` and `Conversion/Elsa3FlowchartGraphBuilder.cs`.

### Scheduling and scripts

Both tags initialize For.OuterBoundInclusive true, but only 3.6.3 declares true as an input default. Missing means
true in both; a null/empty wrapped Input means true in 3.6.3 and false in 3.5.3; deliberate literal-null means false
in both. The reference remains 3.6.3.

Delay's declared default is one minute; deliberate null/typed blank reads as zero duration. Timer null/blank likewise
reads zero; nonpositive values reach legacy scheduling without a positive-interval validation. Default non-strict
conversion can turn invalid TimeSpan text into zero; strict mode can reject it. Neither licenses substituting the
target's one-minute default. The 3.6.3 recurring task clamps nonpositive due delays to 1 ms; 3.5.3 differs, so the
proposal does not claim a common legacy fault.

Cron 3.6.3 completes/schedules nothing for blank text; 3.5.3 lacks those blank guards. Both RunJavaScript versions
return without evaluating blank Script. The current Elsa 4 runtime also has a blank-script path; the converted/catalog
input is required and the importer rejects it as unbound. Keep a truthful conversion-contract refusal rather than
claiming that the target runtime lacks no-work behavior.

Sources: [Delay](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Scheduling/Activities/Delay.cs),
[Timer](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Scheduling/Activities/Timer.cs),
[recurring task, 3.6.3](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Scheduling/ScheduledTasks/ScheduledRecurringTask.cs),
[recurring task, 3.5.3](https://github.com/elsa-workflows/elsa-core/blob/7646867ac5365af35f0507fde72b2df98790bec9/src/modules/Elsa.Scheduling/ScheduledTasks/ScheduledRecurringTask.cs),
[Cron](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Scheduling/Activities/Cron.cs),
[RunJavaScript, 3.6.3](https://github.com/elsa-workflows/elsa-core/blob/7eac7de6955718aea35870be125d54f080fc55dc/src/modules/Elsa.Expressions.JavaScript/Activities/RunJavaScript/RunJavaScript.cs),
[RunJavaScript, 3.5.3](https://github.com/elsa-workflows/elsa-core/blob/7646867ac5365af35f0507fde72b2df98790bec9/src/modules/Elsa.JavaScript/Activities/RunJavaScript/RunJavaScript.cs).

### S-2 boundary and evidence

The [active spec1232 proposal](https://github.com/elsa-workflows/elsa-foundation/pull/3726) was checked at
`15799ee0aea6d32ef5c81bc8f964c21643f5f6e3`. Its policies, requiredness, nullable-null rules and pins/hash remain
separate from named legacy effective values. Its supported type-default policy does not supply TimeSpan/Guid/date
defaults; zero Delay is explicit authored conversion data. This proposal changes no policy fields, variable
initialization, computed-name rule or S-2-owned null-intrinsic behavior.

At proposal preparation, the established local/Linux path and authorized read-only corpus were available but new
production/native/import/runtime/corpus checks were NOT RUN. The separate implementation's fresh receipt is now
recorded in spec.md; it does not relabel these historical source reads as runtime execution. S-2 policy integration
remains NOT RUN, and CI waits remain deferred with no disabled checks or inferred CI success.
