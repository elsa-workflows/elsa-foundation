# Member decisions: Elsa 3 → Elsa 4 activity map

Decision record for [spec.md](spec.md) FR-002. Every Elsa 3 activity member is classified here by rule; the map and its
coverage test must implement exactly this table. Agreed with the maintainer on 2026-10-13.

**Sources.** Elsa 3: elsa-core `5b0c2d73` (the parity report's baseline; no Input/Output/Port declaration differs
from `release/3.6.3`). Elsa 4: elsa-foundation `6768753c7`, `Essentials/src/Activities/**` (inputs are `[ActivityInput]`
properties, the `Key` argument wins; outputs are `[Output]` properties on the activity's result type; slots are
`[ActivityChildSlot]`). Usage counts are from the 36 Elsa 3 workflow definitions of the JTest suite
(`JTEST-nexxbiz/workflow-definitions`). The parity evidence `elsa3-surface.json` lists spurious `Result` outputs on
non-generic activities and misses plain properties and `SetOutput`; this table corrects it.

## Rules

| Rule | Meaning | Outcome |
|---|---|---|
| **R1 Carry** | Same member, or a rename, or an Elsa 3 port carried into an Elsa 4 child slot | Converted |
| **R2 Default flip** | Elsa 4's default differs from Elsa 3's effective value | Converted; the Elsa 3 effective value is emitted explicitly when the Elsa 3 node leaves the member unset |
| **R3 Removed input** | Input or serialized property with no Elsa 4 counterpart | Removed only when absent or a literal equal to the Elsa 3 default (or, where stated, the value Elsa 4 behaves as); otherwise **unconvertible** |
| **R4 Lost output** | Output with no Elsa 4 counterpart | Removed when nothing captures it (no memory reference, no expression reads it); captured → **unconvertible**, unless an exact projection is listed |
| **R5 Narrowed contract** | Elsa 4 accepts a narrower value (literal only, string only, JSON only, int instead of enum, secret reference only) | Converted when the bound value provably fits, judged by its literal value or its declared Elsa 3 `typeName`; otherwise **unconvertible** |
| **R6 No counterpart** | Activity, or a use of it, that Elsa 4 cannot express | **Unconvertible** |
| **R7 Structure** | Structure Elsa 4 needs but Elsa 3 does not carry | Converted; generated deterministically |
| **R8 Graph-level** | Outcomes, connections, joins, forks, start node | The map carries the data; graph conversion applies it ([spec 239](../239-elsa3-flowchart-connections/spec.md), #2705). Flowcharts convert with their connections; FlowFork, FlowJoin and Start become fan-out connections, implicit joins and the start node |
| **R9 Display only** | Member with no behaviour | Removed without a diagnostic. Only the members listed under R9 below |
| **W Runtime difference** | Converted faithfully by contract, but Elsa 4 behaves differently at run time in a way the JSON cannot reveal | Converted with a **Warning** diagnostic naming the difference (one per node per difference) |

"Unconvertible" means an Error diagnostic with a stable code; the item is not converted.

## All activities: members of the Elsa 3 base `Activity`

| Elsa 3 (JSON) | Elsa 4 | Rule |
|---|---|---|
| `id` | `ActivityNode.NodeId` | R1 |
| `nodeId` (hierarchical path) | derived; not stored | R9 |
| `name` | — | R9 |
| `metadata` (designer position, size, display text) | layout sibling (out of scope) | R9 |
| `type`, `version` | `ActivityVersionId` via resolution (spec FR-007, FR-008) | R1 |
| `customProperties.canStartWorkflow` | `CanStartWorkflow` input on HttpEndpoint and Event; Cron and Timer are start-only | R1 where the input exists; on Cron/Timer `true` is required (else R6, mid-flow wait). Elsewhere (a non-trigger) R8: Elsa 3 only uses the flag to pick a flowchart's start when no trigger fired (`Flowchart.GetStartActivity`, 3.5.3), so graph conversion reads it for `startNodeId` and it is then dropped (spec 248; was R3, amended 2026-10-19 with the maintainer) |
| `customProperties.runAsynchronously` | — | R3 (default false) |
| `customProperties.commitStrategy` | — | R3 (default absent) |
| output `converter {id, settings}` | `ArgumentState.Conversion` (Auto/None/Json/Xml/Profile) | R5: Auto/None/Json/Xml carried; any other converter id → unconvertible |

## Control flow

| Activity.member | Kind | Elsa 4 | Rule | JTest |
|---|---|---|---|---|
| If.Condition | input | `Condition` | R1 | 1 |
| If.Then / If.Else | port | slots `If.Then` / `If.Else` | R1 | 1 / 1 |
| If.Result | output | — | R4 | 1 (not captured) |
| If outcome `Done` | outcome | `True`, `False` (no `Done`) | R8: a `Done` connection becomes connections from both `True` and `False` | — |
| While.Condition | input | `Condition` | R1 + **W** (re-read only from persisted state) | 1 |
| While.Body | port | slot `While.Body` | R1 | 1 |
| For.Start / End / Step | input | same | R1 (+ **W**: Step 0 now faults) | 3 |
| For.OuterBoundInclusive | input | `EndInclusive`, default false | R1 rename + R2 (emit `true` when unset) | 3 (true, "True", "False") |
| For.Body | port | slot `For.Body` | R1 | 3 |
| For.CurrentValue | output | — (scoped `index`) | R4; references to it in the body lower to `index` (spec FR-004) | 3 captured → lowered |
| ForEach.Items | input | `Collection` | R1 rename | 1 |
| ForEach.Body | port | slot `ForEach.Body` | R1 | 1 |
| ForEach.CurrentValue | output | — (scoped `currentItem`, `currentIndex`) | R4; references lower to `currentItem` / `currentIndex` | 1 captured → lowered |
| ParallelForEach | activity | — | R6 | 1 |
| Sequence.Activities | port | slot `Sequence.Activities` | R1 | — |
| Sequence.Variables | prop | structure `variables` (one storage driver) | R7; a variable whose storage driver Elsa 4 lacks → unconvertible | — |
| Parallel.Activities | port | `branches[{name, activity}]` | R1 + R7 (branch names generated) | — |
| Parallel.Variables | prop | — | R3 (default empty) | — |
| Parallel (Break inside a branch) | — | Break does not cross Parallel | **W** | — |
| Fork.Branches | port | Parallel `branches` | R1 + R7 | 0 |
| Fork.JoinMode | input (default WaitAll, verified in elsa-core `5b0c2d73`; enum WaitAll = 0, WaitAny = 1) | Parallel `threshold` (absent or WaitAll → null, WaitAny → 1) | R1 + **W** for WaitAny (losing-branch cancellation unverified) | 0 |
| Switch.Cases[].Label | prop | `cases[].match` | R1 | 1 |
| Switch.Cases[].Condition | prop | — (one `Value`, compared to `match`) | R7: synthesize `Value` returning the label of the first true case (MatchFirst) | 1 |
| Switch.Cases[].Activity | port | slot `Switch.Case[{match}]` | R1 | 1 (2 cases) |
| Switch.Default | port | slot `Switch.Default` | R1 | 1 |
| Switch.Mode | input | — (first match only) | R3: literal `MatchFirst` or absent is removed; `MatchAny` or an expression → unconvertible | 1 (expression → unconvertible) |
| Switch.Output | output | — (never assigned in Elsa 3) | R4 | 1 |
| Switch outcome `Done` | outcome | `<match>`, `Default` | R8 | — |
| Flowchart.Activities | port | slot `Flowchart.Activities` | R1 | 49 |
| Flowchart.Variables | prop | structure `variables` | R7 (as Sequence) | 49 |
| Flowchart.Connections | prop | `connections[{source, target}]` | R8 (spec 239); `vertices` and target ports R9 | 34 files |
| Flowchart.Start / start resolution | port | `startNodeId` | R8 (spec 239: Start target, first start-capable child, first child with no inbound connection, first child) | — |
| Flowchart execution mode (`Flowchart:ExecutionMode`) | custom prop | — (global join policy) | R3 (default absent) | — |
| Break | activity | `Break` (outcome) | R1 + **W** (does not cross Parallel) | 0 |

## Flow-model activities

| Activity.member | Elsa 4 | Rule | JTest |
|---|---|---|---|
| FlowDecision → If | `If` with no slots | R1 | 46 |
| FlowDecision.Condition | `If.Condition` | R1 | 46 |
| FlowDecision outcomes True/False | If `True`/`False` | R1 | 92 connections |
| FlowSwitch → Switch | `Switch` | R1 | 1 |
| FlowSwitch.Cases `[{label, condition}]` | `cases[{match}]` + synthesized `Value` | R7 (as Switch) | 1 |
| FlowSwitch.Mode | — | R3 (as Switch.Mode) | 1 (MatchFirst) |
| FlowSwitch outcomes labels, `Default` | match values, `Default` | R1 | 3 connections |
| FlowFork | fan-out connections | R8 (spec 239); branches by expression → R6 (`UNSUPPORTED-MODE`) | 4 |
| FlowJoin.Mode (WaitAny default, WaitAll, WaitAllActive) | implicit join of what follows | R8 (spec 239): WaitAllActive converts; WaitAny only with mutually exclusive branches; WaitAll only when every branch provably runs; else `GRAPH-JOIN-UNSUPPORTED` | 7 |

## Primitives, terminals and intrinsics

| Activity.member | Elsa 4 | Rule | JTest |
|---|---|---|---|
| WriteLine.Text | `text` | R1; unset or empty → R2 `""` (Elsa 3 writes an empty line; spec 248, amended 2026-10-19) | 39 |
| ReadLine.Result | `Result` | R1 | 0 |
| Fault.Code / Category / FaultType / Message | `code` / `category` / `faultType` / `message` | R1 + R2 (emit `"0"`, `"General"`, `"System"` when unset; Message has no Elsa 3 default) | 0 |
| SetVariable.Variable | `elsa.intrinsic.set@1` `variable` (`VariableReference`) | R7 (declaring scope computed) | 20 |
| SetVariable.Value | `value` | R1; unbound or null → unconvertible (required) | 20 |
| SetOutput.OutputName | see below | R5 (literal only) | 10 |
| SetOutput.OutputValue | see below | R1 | 10 |
| SetOutput in a plain workflow, or naming an output the enclosing reusable workflow does not declare | `elsa.intrinsic.set-output@1` (`name`, `value`); writes the running workflow's output, as Elsa 3's fallback does | R1 | — |
| SetOutput in a reusable workflow naming one of its declared outputs | `elsa.intrinsic.set@1` targeting a synthesized graph variable for that output; the graph manifest's `outputMappings` maps the output to that variable | R7 (see "SetOutput in reusable workflows") | 1 file (`composition-workflow-as-activity-sub`) |
| Correlate.CorrelationId | `elsa.intrinsic.set-correlation-id@1` `value` | R1 | 19 |
| SetName.Value | `elsa.intrinsic.set-instance-name@1` `value` | R1; empty or unbound → unconvertible (required) | 1 |
| Finish | `elsa.intrinsic.finish@1` | R1 + R2 (`outcome = "Done"`) | 0 |
| Complete.Outcomes in a plain workflow | finish `outcome` | Only when the node is in the workflow's root flowchart and `Outcomes` is absent or one literal string; otherwise R6 | 1 |
| Complete.Outcomes in a reusable workflow | `Elsa.Activities.Graph.Runtime.Activities.Complete` `outcome` (spec 237); manifest schema 2 | Never finish (that would end the calling workflow). At any depth, absent/empty or one literal string → one `Complete` node with that outcome's reference key (`Done` → `done`, otherwise verbatim); the outcome is declared as an emitted contract outcome. Several outcomes or an expression → R6 (`UNSUPPORTED-MODE`). An outcome the definition does not declare → **W** (`OUTCOME-UNDECLARED`). See "Complete and End in reusable workflows" | 1 (`fail`/`succeed`) |
| End in a plain workflow | finish | Only when the node is in the workflow's root flowchart; otherwise R6 | 0 |
| End in a reusable workflow | `Complete` with `outcome = "done"` | Only in the workflow's root flowchart; otherwise R6 (amended by spec 239 FR-011: Elsa 3 End ends only its enclosing flowchart) | 0 |
| Reusable workflow's declared `Outcomes` | contract outcomes | `Done` is always declared (natural completion). A declared outcome that no `Complete` reaches is dropped → **W** (`OUTCOME-UNREACHED`) | — |
| Start | Flowchart `startNodeId` | R8 (spec 239); a Start leading to several nodes → `GRAPH-START-AMBIGUOUS` | 0 |
| Inline, DynamicActivity, NotFoundActivity, StateMachine | — | R6 | 0 |
| Workflow, CompositeWithResult as a node | — | R6 (a workflow-as-activity reference is a reusable reference, handled by the collection plan) | 0 |

## HTTP

| Activity.member | Elsa 4 | Rule | JTest |
|---|---|---|---|
| HttpEndpoint.Path | `Path` (required, fixed at publish) | R1 + R5 (literal) | 30 |
| HttpEndpoint.SupportedMethods, Authorize, Policy, RequestTimeout, RequestSizeLimit | same | R1 (+ **W** RequestSizeLimit has no outcome) | 30 |
| HttpEndpoint.FileSizeLimit, AllowedFileExtensions, BlockedFileExtensions, AllowedMimeTypes | — | R3 (default null/empty) | 30 |
| HttpEndpoint.Expose…Outcome (4) | — | R3 (default false) | 30 |
| HttpEndpoint outcomes Request too large, File too large, Invalid file extension, Invalid file MIME type | — | R6 if a connection uses one (spec 239) | 0 |
| HttpEndpoint.Result | `Request` (`HttpRequestModel`) | R4 → projection `Request` only when its consumers read members `HttpRequestModel` has; otherwise unconvertible | 30 |
| HttpEndpoint.ParsedContent | `ParsedContent` (`JsonElement?`) | R1 + **W** (form posts not parsed; text becomes a string) | 30 (22 captured) |
| HttpEndpoint.RouteData | `RouteData` | R1 | 30 (1 captured) |
| HttpEndpoint.QueryStringData / Headers | synthesized workflow variable `elsa3.http.{node}.query` / `.headers`, set right after the endpoint from `Request.Query` / `Request.Headers` in the Elsa 3 shape (one value → string, several → array); consumers bind to the variable | R7 (plan D-11 to D-13). Elsa 3 values are a string or a string array; Elsa 4 values are always lists, and a binding cannot project a sub-path. Endpoint as root or in a single-child slot → R4 unconvertible | 30 (2 / 12 captured) |
| HttpEndpoint.Files / File | — | R4 | 30 / 13 (1 captured → unconvertible) |
| (Elsa 4) HttpEndpoint.ResponseMode | default `Async` (immediate 202) | R2: emit `Sync` | 30 |
| SendHttpRequest / FlowSendHttpRequest → `SendHttpRequest` | | R1 | 0 |
| .Url, .Method, .StatusCode, .ResponseHeaders, .ExpectedStatusCodes (flow) | same | R1 (Method `PATCH`/`OPTIONS` → R6) | 0 |
| .Content | `Content` (string) | R5 (string `typeName` or literal string) | 0 |
| .ContentType | default `text/plain` | **Maintainer decision (2026-10-13), a deliberate behaviour change:** when `Content` is bound and `ContentType` is unset, emit `application/json`. Elsa 3 sent no body in that case (`SendHttpRequestBase.cs:271-278` attaches content only when a content type is set); the maintainer judged that the intended behaviour. No diagnostic. When neither is bound, ContentType stays unbound | 0 |
| .Authorization | credential (secret reference only) | R5: any literal or expression → unconvertible, guidance "move to a secret" | 0 |
| .DisableAuthorizationHeaderValidation | — (Elsa 4 behaves as `true`) | R3 against `true`: absent or `false` → **W**; `true` removed | 0 |
| .RequestHeaders | `IDictionary<string,string>` | R5 (one value per header) | 0 |
| .EnableResiliency | — | R3 (default false) | 0 |
| .ParsedContent | `ResponseBody` (raw string) | Captured → unconvertible ("member semantics changed") | 0 |
| .Result (HttpResponseMessage) | — | R4 | 0 |
| outcomes `Failed to connect`, `Done` co-firing with status outcomes | `Failed`; single outcome | R8 + **W** | 0 |
| non-flow .ExpectedStatusCodes[].Activity, UnmatchedStatusCode / FailedToConnect / Timeout ports | — (leaf activity) | R6 when any port holds an activity | 0 |
| WriteHttpResponse.StatusCode | `StatusCode` (int) | R5 (enum name literal → int) | 71 |
| WriteHttpResponse.Content | `Body` (JSON value) | R1 rename + R5: a literal, or a variable declared `String`, converts with any content type; a script with a typed declaration (`String`, `Int32`, …) → unconvertible, `VF-IMP-008` (Elsa 3 converted its result to that type; Elsa 4 takes it as returned; spec 256 FR-013, all activities). Any other value converts when `ContentType` is unset, blank, or exactly `application/json` or `text/json` (Elsa 3 sent JSON then, inferring `application/json`; Elsa 4 does the same); another JSON type (letter case, a parameter, `+json`) converts with **W** `ELS3-BEHAVIOUR-HTTP-RESPONSE-JSON-VARIANT` (Elsa 3 sent the type name, Elsa 4 the JSON). A non-JSON literal content type (Elsa 3 sent the value's type name), a computed content type, or `Byte[]`/`Stream` content → unconvertible (spec 256, #3050; was R5 string only, amended 2026-10-20 with the maintainer) | 71 |
| WriteHttpResponse.ContentType | same | R1; an expression with a blank script is dropped (Elsa 3 evaluates it to null and infers the type; Elsa 4 infers it when unset) (spec 256) | 71 |
| WriteHttpResponse.ResponseHeaders | `Headers` | R1 rename | 71 |
| DownloadHttpFile, WriteFileHttpResponse | — | R6 | 0 |

## Scheduling

| Activity.member | Elsa 4 | Rule | JTest |
|---|---|---|---|
| Delay.TimeSpan | `Duration`, default 0 | R1 rename + R2 (emit 00:01:00 when unset) | 0 |
| Timer.Interval | `Interval` string, literal, required | R5 (literal TimeSpan → string) + R2 (00:01:00 when unset); mid-flow (not `canStartWorkflow`) → R6 | 0 |
| Cron.CronExpression | `Expression`, literal | R1 rename + R5; mid-flow → R6 | 0 |
| StartAt | — | R6 | 0 |

## Runtime

| Activity.member | Elsa 4 | Rule | JTest |
|---|---|---|---|
| Event.EventName | `EventName` | R1 | 1 |
| Event.Result (payload) | `Payload` output (`Result` is the event name) | R1: carried to `Payload` (spec 258, #3107; was R4, amended 2026-10-20 with the maintainer) | 1 (captured → converts) |
| Event `canStartWorkflow` | `CanStartWorkflow`, default true | R2: emit the Elsa 3 value (default false) | 1 |
| PublishEvent.EventName, CorrelationId, IsLocalEvent | same | R1 | 1 |
| PublishEvent.Payload | `JsonElement?` | R5 (JSON) | 1 |
| DispatchWorkflow.WorkflowDefinitionId | required, pinned at publication | R5 (literal only) | 2 |
| DispatchWorkflow.CorrelationId, WaitForCompletion | same | R1 | 2 |
| DispatchWorkflow.Input | `Inputs` (JSON, child-declared keys) | R1 rename + R5 (JSON) | 2 |
| DispatchWorkflow.StartNewTrace, ChannelName | — | R3 (false / null) | 2 |
| DispatchWorkflow.Result | `Result` (`DispatchWorkflowResult`) | R4: captured → unconvertible (shape differs) | 2 (1 captured) |
| (Elsa 4) DispatchWorkflow.CancelChildOnParentCancellation | default true | R2: emit `false` | 2 |
| DispatchWorkflow outcome `Done` | Dispatched / Completed / Faulted / Cancelled / DispatchFailed | R8 | 2 |
| ExecuteWorkflow → DispatchWorkflow | `WaitForCompletion = true` | R1 + **W** (durable dispatch instead of an in-process call); members as DispatchWorkflow | 1 (Result captured → unconvertible; target is an expression → unconvertible) |
| BulkDispatchWorkflows, RunTask | — | R6 | 2 / 0 |

## JavaScript

| Activity.member | Elsa 4 | Rule | JTest |
|---|---|---|---|
| RunJavaScript.Script | `script` (sandbox sees `args` only) + `arguments` | R1 + R7 (plan D-16): the literal body is rewritten in script mode. `getVariable`/`getInput`/`variables.x` become `arguments` entries read as `args.<name>`, and an Elsa 3 completion value becomes `return`. `setOutcome(s)` and other host calls → VF-IMP-005 (follow-up #2717) | 17 |
| RunJavaScript.PossibleOutcomes | `possibleOutcomes` | R1 + **W** (routing by return value; `setOutcomes` with several outcomes is refused by the rewriter) | 17 |
| RunJavaScript outcome `Done` | `Unmatched` | R8 (spec 239): Elsa 3 completes with `Done` unless the script calls `setOutcome` (refused); Elsa 4 completes with `Unmatched` when the return value names no possible outcome | 16 connections |
| RunJavaScript.Result | `result` | R1 | 17 (9 captured) |

## Elsa 4 required members with no Elsa 3 source

A converted node must not leave a required Elsa 4 input unbound. These become unconvertible when the Elsa 3 node does
not supply them: SendHttpRequest.Url, HttpEndpoint.Path, Event.EventName, PublishEvent.EventName,
Cron.Expression, DispatchWorkflow.WorkflowDefinitionId, RunJavaScript.script (an empty string counts as unbound), and
every intrinsic input (`variable`+`value`, `name`+`value`, `value`, `outcome`). Timer.Interval and Finish.outcome are
covered by R2, and so is WriteLine.text (`""`, spec 248): a blank R2 default counts as bound.

## Null members (spec 267)

Added by [spec 267](../267-elsa3-null-members/spec.md) (#3200). A JSON `null` member converts to what the Elsa 3
runtime ran the activity with: a null input property runs with the input's declared default, otherwise with its type's
default (`false`, `0`, `null`). Every R1 and R5 input records its conversion (`OnNull`); an R2 default flip, and an R5
rule with an Elsa 3 default, emit that default for a null as for an absent member, except where the rule records its own
(SendHttpRequest.ContentType, whose R2 default applies only when Content is bound). The coverage test fails on a gap.
R7 structure members, ports and custom properties keep their own handling, which reads a null as unset.

| Conversion | Members |
|---|---|
| Typed null (`Literal` holding null) | RunJavaScript.PossibleOutcomes; Fault.Message; HttpEndpoint.SupportedMethods, Policy, RequestTimeout, RequestSizeLimit; SendHttpRequest / FlowSendHttpRequest.Content; WriteHttpResponse.Content, ContentType; PublishEvent.CorrelationId, Payload; DispatchWorkflow / ExecuteWorkflow.CorrelationId, Input |
| No binding (Elsa 4's default is Elsa 3's value) | If / While / FlowDecision.Condition; For.Start, End; HttpEndpoint.Authorize; SendHttpRequest / FlowSendHttpRequest.Method; WriteHttpResponse.StatusCode; PublishEvent.IsLocalEvent; DispatchWorkflow.WaitForCompletion |
| Elsa 3's value | R2: For.OuterBoundInclusive `true` (3.6.3), Delay `00:01:00`, WriteLine `""`, Fault code / category / faultType; R5: Timer `00:01:00`; recorded: FlowSendHttpRequest.ExpectedStatusCodes `[200]`, Correlate.CorrelationId and SetName.Value `""` |
| `ELS3-ACTIVITY-NULL-UNCONVERTIBLE` | For.Step (ran as 0); ForEach.Items, SendHttpRequest / FlowSendHttpRequest.RequestHeaders, WriteHttpResponse.ResponseHeaders (faulted); SendHttpRequest / FlowSendHttpRequest.ContentType with Content bound (no body sent; a typed null otherwise); Cron.CronExpression (a disabled trigger); RunJavaScript.Script (ran nothing); HttpEndpoint.Path, SendHttpRequest / FlowSendHttpRequest.Url, Event / PublishEvent.EventName, DispatchWorkflow / ExecuteWorkflow.WorkflowDefinitionId, SetOutput.OutputName (required); SetVariable.Value, SetOutput.OutputValue (Elsa 4 cannot publish a null value, #3280) |
| Unchanged (spec 236's mapping) | ExecuteWorkflow.WaitForCompletion: Elsa 3 ran a null as `false`, but spec 236 maps ExecuteWorkflow to a Dispatch that waits (`true`), with `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` |
| No binding, by rule | A null output (nothing captures it); a null member of an activity resolved by name; R3 removed inputs and R6 restrictions, which already treat null as absent |

## SetOutput in reusable workflows

Elsa 3's `SetOutput` writes to the nearest ancestor activity that declares an output of that name, else to the
workflow output. Inside one definition only the definition itself declares outputs, so the target is always that
definition: the workflow output for a plain workflow, and the workflow-as-activity's output for a reusable one.

Elsa 4's `set-output` intrinsic always writes the running workflow's output (`WorkflowIntrinsicExecutor`,
`NewWorkflowOutputChange`) and does not see graph boundaries. A graph activity's outputs are captured only from the
manifest's `outputMappings`, evaluated when the graph completes (`GraphActivityScope.CaptureOutputsAsync`). The
materializer writes `outputMappings: []` today, so a converted reusable workflow's outputs are always empty, and its
`SetOutput` nodes would write the calling workflow's output.

The conversion, for each declared output `O` of a reusable workflow:

1. A graph variable is synthesized for `O`, with `O`'s type and storage driver and a reference key derived
   deterministically from `O`'s reference key that cannot collide with an authored variable.
2. Every `SetOutput` node naming `O` becomes `elsa.intrinsic.set@1` targeting that variable, with its value binding.
3. The manifest's `outputMappings` maps `O` to that variable.

Last write wins and the value is read at completion, which matches Elsa 3. An output nothing sets maps to an unset
variable, which reads as null, as in Elsa 3.

The direct-start wrapper (the reusable workflow's original identity) must carry the graph activity's outputs into its
own workflow outputs, instead of the null outputs it sets today.

Both changes alter the content of the materializer's `BuildGraphManifest` and `BuildWrapperState` without restructuring
them (spec FR-019).

## Complete and End in reusable workflows

In Elsa 3, `Complete` inside a workflow-as-activity completes the composite with its outcomes, from any depth, and the
caller follows the matching ports. `End` completes it with `Done`. Elsa 4's `Complete` activity (spec 237) has the
same reach: it ends the nearest reusable activity from any depth, through loops and `Parallel`, and cancels the rest of
the body. It takes exactly one literal outcome, named by the boundary outcome's **reference key**.

The conversion, for a reusable workflow (plan D-1 to D-7; data-model §7a):

1. Each `Complete` with no outcome, or one literal outcome, and each `End` in the root flowchart (spec 239 FR-011),
   becomes one `Complete` node. The node's `outcome` is the key from the outcome key rule: `Done` → `done`, anything else verbatim.
2. The manifest is schema 2. Its `outcomeMappings` hold exactly `done → done`, so a body that ends without reaching a
   `Complete` still completes with `Done`, as in Elsa 3.
3. The contract outcomes are `Done` plus every outcome a `Complete` names. Contract names are the Elsa 3 strings
   verbatim, so caller ports keep their Elsa 3 names.
4. Unconvertible (`ELS3-ACTIVITY-UNSUPPORTED-MODE`):
   - several outcomes, because Elsa 4 completes with one;
   - an outcome set by an expression;
   - `"done"` used alongside `"Done"`.
5. Warnings:
   - `ELS3-BEHAVIOUR-OUTCOME-UNDECLARED`: a `Complete` names an outcome the definition did not declare. It is
     declared anyway, because Elsa 3 emitted it.
   - `ELS3-BEHAVIOUR-OUTCOME-UNREACHED`: a declared outcome that no `Complete` reaches is dropped from the contract,
     so it gets no caller port.

`Break` is unaffected: it still maps to `Break` (row above), and no `Complete`/`End` is lowered onto it.

## Nesting budget

Converted containers get native Elsa 4 structures, which nest children at different JSON depths, so the old flat
limit of 14 containers no longer holds. An activity node may sit at JSON level 58 at most (System.Text.Json's limit
is 64, and a node needs 6 levels for its bindings); the root node sits at level 2. Each Sequence or Flowchart adds 4
levels, each If, While, For or ForEach adds 3, and each Parallel or Switch adds 5. That allows 14 nested Sequences, 18
nested Ifs or loops, or 11 nested Parallels or Switches; mixed nesting adds up. A deeper node is
`ELS3-ACTIVITY-NESTING-TOO-DEEP`, and its message names the level reached. User-facing explanation:
`Extensions/Elsa3/src/Mapping/README.md`, "Nesting budget".
