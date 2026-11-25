# Elsa3.Mapping

Converts Elsa 3 workflow definitions to Elsa 4 Design state. The single-definition import and the Elsa 3 collection
import both use one converter, `IElsa3DefinitionConverter`. Start-time reconciliation (spec 235,
[`Elsa3JsonReconciliation`](../Activities/Design/Import/Reconciliation/README.md)) runs the collection import on a
folder of Elsa 3 exports at engine start, so it converts exactly as the collection import does. The
decision record for every Elsa 3 member is
[`specs/236-elsa3-activity-map/member-map.md`](../../../../specs/236-elsa3-activity-map/member-map.md); flowchart
connections follow [`specs/239-elsa3-flowchart-connections`](../../../../specs/239-elsa3-flowchart-connections/spec.md).

## Portable null policies

The engineer option `Elsa3MappingOptions.ApplyElsa3NullPolicies` (also exposed by `Elsa3MappingFeature`) defaults to
true and records both `strategyOptions.nullHandling.defaultOnNull` and `undefinedAsNull`. Turning it off records
native Elsa 4 rules. Native authored workflows leave both policies off. The report includes `ELS3-NULL-POLICIES`.
See [workflow definition values](../../../../docs/reference/workflow-definition-values.md) for each policy's runtime
before/after examples, export/import behavior and diagnostics. These are configuration settings, not Studio controls.

A variable or optional workflow input of a date, time, duration or Guid type without a starting value is refused before
publication: declare a default or use String / Any. Elsa 3.6.3 exposes neither requiredness nor default on its workflow
input definitions, so converted inputs are optional and their nullability follows TYPE.

## Pipeline

A conversion clones the definition, so the caller's object is never changed, then runs four stages:

1. **Normalize** (`Elsa3ActivityNormalizer`), node by node:
   - resolves the node's Elsa 4 activity;
   - applies the member rules of its map entry: renames, defaults, removed and lost members, narrowed contracts and
     runtime-difference warnings;
   - runs the entry's rewrite strategy;
   - routes port children (`then`, `body`, `cases[].activity`, …) to the Elsa 4 child slots.

   Once every node is normalized, each flowchart's connections become Elsa 4 connections, innermost flowchart first
   (`Elsa3FlowchartGraphBuilder`, see "Flowcharts" below).
2. **Collect and lower** value flow: memory references, `getVariable`, `getInput` and loop values become Elsa 4
   bindings. This stage runs only when normalization reported no error.
3. **Map** to `WorkflowDefinitionState`, giving each container the native structure of its Elsa 4 activity (Sequence,
   Flowchart, If, loops, Parallel, Switch).
4. **Report** diagnostics, ordered by (JSON path, code).

Nothing throws for input or catalog content. Every cause is a diagnostic:
- `ELS3-ACTIVITY-*` errors make the item unconvertible;
- `ELS3-BEHAVIOUR-*` warnings mean it converts, but Elsa 4 behaves differently at run time;
- `VF-IMP-*` are value-flow errors;
- `ELS3-VARIABLE-TYPE-UNREGISTERED` (an info) names a variable's custom type that Elsa 4 does not register (#5666);
- `ELS3-BEHAVIOUR-MEMORY-STORAGE-PERSISTED` and `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED` (warnings) name a
  variable, input or output whose Elsa 3 Memory or custom storage driver became Elsa 4's default storage;
- `ELS3-UNTYPED-RESULT` (an error) is an untyped activity result bound to a typed input whose kind is not known to match
  (spec 1228 US6, #4898); `ELS3-SCRIPT-OUTCOME-*` are about a script's computed outcomes.

The codes are constants in `Elsa3.Models.Elsa3ActivityDiagnosticCodes` and `Elsa3ImportDiagnosticCodes`.

## Which Elsa 4 activity a node becomes

The resolver tries these in order, and the first match wins:

1. A plan replacement (a reusable reference in a collection).
2. The built-in map (`Elsa3BuiltInActivityMap`).
3. An Elsa 4 CLR activity marked `[ReplacesActivityType("Acme.SendInvoice")]`, read from the catalog. Two claimants →
   `ELS3-ACTIVITY-AMBIGUOUS-ALIAS`.
4. The Elsa 3 type used verbatim as a catalog key.
5. Otherwise `ELS3-ACTIVITY-UNMAPPED-TYPE`.

A catalog target takes its highest valid-SemVer version; the Elsa 3 integer version never selects it.

## Behaviour worth knowing

- **Variable expressions** (#3201). An Elsa 3 `Variable` expression reads the variable's declared id, using the
  nearest enclosing container's scope or workflow scope. Its embedded name and storage-driver metadata do not
  select another variable. Missing, ambiguous, malformed and out-of-scope references are refused during import;
  generated projection IDs do not satisfy a missing source declaration. The selected declaration's actual value type
  must fit the input. A dynamic variable read into a typed input needs compatible producer kinds and a preceding
  sequence capture. Future or conditional captures alone cannot establish compatibility; competing explicit or script
  writes invalidate that proof. Reads of a loop's captured current value
  retain the inside-the-loop restriction and refuse competing explicit or script writes.
- **Output capture.**
  - An Elsa 3 output captured into a **workflow** variable becomes an Elsa 4 `Variable` output binding, so readers of
    the variable see the value.
  - A capture into a container variable is `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`; Elsa 4 captures into workflow
    scope only.
  - A loop's current value captured into a variable is read as the loop's scoped variable (`index`, `currentItem`)
    inside the loop body. Outside the loop that read is not representable.
- **Variable starting values** (spec 264). Elsa 3 stored a variable's starting value as text and converted it to the
  variable's type when the workflow started. The converter applies that rule, through Elsa 4's object converter, and
  carries the result as a `Literal`:
  - no value → no starting value (the variable starts empty);
  - `Object` → the text, unparsed, even when it is JSON;
  - `String`, primitives and enums → the text converted to the type (`"5"` → `5`, `"true"` → `true`);
  - a collection (`List<T>`, `T[]` or an array flag) whose element type Elsa 4 knows → a JSON array. The variable is
    `T` with collection kind List or Array. Every item must be JSON of `T`, read as Elsa 3 read it (enums by name,
    property names case-insensitive) and written for Elsa 4 as plain JSON (an enum as its number). As in Elsa 3, a
    number written as a string (`["5"]` for `List<Int32>`) does not convert;
  - `ObjectDictionary` and `HttpHeaders` → `Object` holding a JSON object;
  - any other type Elsa 4 does not know (a nested collection such as `List<List<String>>`, `ObjectDictionary[]`, a
    custom class or an unregistered enum) → `Object`. A JSON starting value (`{…}` or `[…]`) is held as parsed JSON,
    exactly as written: Elsa 3 deserialized it case-insensitively and wrote camelCase names, but `Object` keeps the
    names as the author wrote them (`{"ID":1}` stays `ID`).

  A custom type Elsa 4 does not register (the type itself, or the innermost element of a collection of it, such as
  `Acme.Orders.OrderDto` or `List<Acme.Orders.OrderDto>`) gets the info `ELS3-VARIABLE-TYPE-UNREGISTERED`, naming the
  variable and the type: its Elsa 4 type is Any until an Elsa 4 feature registers the type and the workflow is converted
  again (#5666). The converter looks the Elsa 3 name up in Elsa 4's registry, as an alias or as the full name of a
  registered type, and otherwise loads it as an assembly-qualified CLR name. The info is raised even when the variable
  is also refused, so every custom type is reported. A nesting Elsa 4 cannot express (`List<List<String>>`,
  `ObjectDictionary[]`) gets none, and neither do Elsa 3's JSON types (`JsonElement`, `JsonNode`, `JsonObject`,
  `JsonArray`, `ExpandoObject`), which Any holds faithfully.

  Every Elsa 3 storage driver becomes Elsa 4's default storage: no storage driver, which a reusable workflow's contract
  publishes as `elsa.json`. This holds for workflow and container variables, inputs and outputs, reusable workflow or
  not. The converter never carries a driver over, even one an Elsa 4 host registers: Elsa 4 honours storage drivers only
  on a reusable activity's members, and proper support is #5698. The default `WorkflowInstanceStorageDriver` and the
  obsolete `WorkflowStorageDriver` kept the value with the instance too, so they convert silently. The Memory driver
  never persisted the value; it converts with the warning `ELS3-BEHAVIOUR-MEMORY-STORAGE-PERSISTED`. Any other driver
  kept the value outside the instance; it converts with `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED`. Both warnings
  carry `VariableName`, `InputName` or `OutputName` and `StorageDriverType` (the trimmed Elsa 3 driver type) as
  metadata, plus `ComponentKind` (`storage-driver`) and `ComponentName` (the same type) for the migration report's
  component inventory. What to do instead is the migrator's choice:
  [Elsa 3 storage drivers](../../../../docs/migration/elsa3-storage-drivers.md) walks through it.

  A resource type (`FormFile`, `IFormFile`, `Stream`, `byte[]` or `Byte` with the array flag, alone, as a collection
  element or as a CLR generic type argument) is refused with or without a starting value: it stands for a file, a stream or raw bytes, which Elsa 4 cannot hold yet (#3194).

  Text is never split on commas. `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE` refuses, with the exact fix, a starting
  value that:
  - is not a JSON array for a collection whose element type Elsa 4 knows (the guidance shows the array the author most likely meant: `a, b` →
    `["a","b"]`);
  - has an item that does not convert to the element type;
  - is not a JSON object for a dictionary;
  - does not convert to its scalar type (`"abc"` for an `Int32`, which Elsa 3 silently started as `0`);
  - is not a JSON object or array (`42` included), on a type Elsa 4 does not know: Elsa 3 converted such a value in a
    way the converter cannot know.

  Any other variable with no starting value converts.
- **HttpEndpoint headers and query string.** A captured `Headers` or `QueryStringData` gets a synthesized `set` node
  right after the endpoint. It rebuilds the Elsa 3 shape from Elsa 4's `Request` output (one value → string, several →
  array) into the captured variable, or into `http.{node}.headers` / `.query` for a bare memory slot. The
  endpoint must sit in a list of activities (a Sequence or a Flowchart) so that a node can follow it; in a Flowchart the
  node is connected between the endpoint and the endpoint's former successors.
- **Defaults and required inputs.** A WriteLine without text gets `text = ""`, because Elsa 3 writes an empty line. An
  R2 default counts as bound even when it is blank. Any other required Elsa 4 input with no Elsa 3 value is
  `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND`.
- **Null members (spec 267).** Elsa 3's exporter writes `null` for every member the author never set. A null member
  converts to what the Elsa 3 runtime ran the activity with, as each map rule records (`OnNull`):
  - Elsa 3 ran with null and the Elsa 4 input accepts null: a `Literal` holding null (RunJavaScript's
    `possibleOutcomes`, HttpEndpoint's `Policy`, …).
  - Elsa 3 ran with a value Elsa 4's default equals: no binding (If's `Condition`, …).
  - Elsa 3 ran with a value Elsa 4's default differs from: that value, as for an absent member (R2), plus
    FlowSendHttpRequest's `[200]` and an empty string for Correlate and SetName, which Elsa 4 reads as clearing them.
  - Elsa 4 cannot reproduce or represent what Elsa 3 did: `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`, naming the member and why
    (For's `step` ran as 0, ForEach's `items` and the header members faulted, a SendHttpRequest with content and a
    null content type sent no body, a required member rejected the activity, a null SetVariable or SetOutput value
    cannot be published: #3280).

  A null output is captured by nothing, and a null member of an activity resolved by name gets no binding. An input
  holding a literal null follows that table except for the named declared-default corrections below. Nothing above
  reports a warning, except one case (#3293). On an
  alias or a verbatim catalog key, Elsa 3 ran a null with its own activity's default, which the conversion cannot see.
  Where the Elsa 4 input declares a default, the member converts with `ELS3-BEHAVIOUR-NULL-TAKES-ELSA4-DEFAULT`, which
  names that default and says what to set to keep Elsa 3's value: the member in the Elsa 3 workflow, or the input on the
  converted activity.
- **`canStartWorkflow` on a non-trigger** (a WriteLine, SetVariable, …) only picks a flowchart's start node, as in Elsa 3
  when no trigger fired. It is dropped after that, without a diagnostic. HttpEndpoint and Event carry the flag; Cron and
  Timer require it.
- **Declared types.** Value-flow lowering compares a value with the Elsa 4 member in the shape normalization gave it:
  `HttpStatusCode` is an integer, `HttpHeaders` a dictionary of string arrays, `ObjectDictionary` a dictionary, and
  `List<T>` a list of `T`. A declared type that still does not fit is `VF-IMP-008`. A lowered expression declares the
  Elsa 3 type as its result type, except for a JSON member (`JsonElement`, `Object`, `Any`), for every activity: it
  accepts any value, so the expression declares the member's own type, which publication requires verbatim. A script
  bound to a JSON member must therefore be declared untyped in Elsa 3 (`Object`, or a list or dictionary of objects):
  Elsa 3 converted a typed declaration's result (`Int32` turns `"5"` into 5), and Elsa 4 would take the value as
  returned, so a typed declaration is `VF-IMP-008`.
- **Script results into typed inputs** (spec 1228 US6). A RunJavaScript result has no declared type. Elsa 3 coerced it
  into a boolean, string or number input whatever it was (`"true"` and `1` became `true`, a fraction was rounded);
  Elsa 4 converts only a JSON value of the matching kind. The import infers the kind from the script's completion value
  and its top-level returns (literals, comparisons, `!`, template strings, `Boolean(…)`, `String(…)`, `Number(…)`,
  `Math.round(…)`, …) and keeps the binding only when it matches the input (for an integral input, an integer; for a decimal input, an integer or
  a fraction literal, since Elsa 3 converted a computed fraction at 15 significant digits). Any
  other or unknown kind is `ELS3-UNTYPED-RESULT`, with guidance to state the kind in the script.
- **Other untyped results into typed inputs** (#4898). The same check covers every untyped activity result, read
  directly or through an untyped (`Object`) workflow variable the result is captured into: a ForEach's current item,
  an Event's payload, HTTP request or response data, any other `Object` output. Nothing in the definition shows their
  kind, so a binding to a boolean, string or number input is `ELS3-UNTYPED-RESULT`, with guidance to convert the value
  in a JavaScript expression (`String(…)`, `Boolean(…)`, …). A For loop's current value is its index, an `Int32` in
  Elsa 4: it is kept for an `Int32`, `Int64` or `Decimal` input and refused for any other (Elsa 3 converted it into any input).
  A typed output, a typed variable, or a variable only Set Variable writes is not an untyped result; a loop's value
  captured into a typed variable still is, since Elsa 4 reads the loop's own variable there. A loop value
  captured into a variable and read outside its loop keeps its own refusal. The same producer-kind checks apply to
  a `Variable` expression that reads the captured variable. JavaScript and Liquid expressions that read such a value
  are not yet checked.
- **Literal lists.** A literal bound to a collection input whose text is a JSON array (`["Approved","Rejected"]`,
  `[200,404]`) converts to that JSON array, as Elsa 3 converted the text. Elsa 4 reads literal text for a collection
  as a comma-separated list, so the brackets and quotes would otherwise become part of the items.
- **HTTP response body.** Elsa 4's `WriteHttpResponse.Body` is a JSON value: text is sent as is, anything else as JSON,
  and an unset content type is inferred as Elsa 3 did (`text/plain` for text, `application/json` otherwise). Elsa 3
  `Content` therefore converts when it is a literal or a `String` variable, or when the content type is unset, blank or JSON.
  Elsa 3 matched its JSON writer by exact media type and sent any other value's type name as text: another spelling of
  a JSON type (letter case, a parameter, `+json`) converts with `ELS3-BEHAVIOUR-HTTP-RESPONSE-JSON-VARIANT`, because
  Elsa 4 sends the JSON. An object with a non-JSON or computed content type and binary content are
  `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`; a script with a typed declaration is `VF-IMP-008` (above). A content type expression with
  a blank script is dropped.
- **JavaScript** converts when Elsa 4 runs it the same, and is refused otherwise. See
  [Migrating Elsa 3 JavaScript](#migrating-elsa-3-javascript).
- **Reusable workflows** (collection import, `IsReusable`):
  - The body becomes a schema-2 graph.
  - `SetOutput` of a declared output writes a synthesized variable (`output.{hash}`) that `outputMappings`
    expose.
  - `Complete` becomes the graph `Complete` activity, at any depth, with the outcome's reference key (`Done` →
    `done`, other names verbatim). `End` does the same with `done`, but only in the workflow's root flowchart: Elsa 3
    `End` ends only its enclosing flowchart, so elsewhere it is `ELS3-ACTIVITY-UNSUPPORTED-MODE`.
  - `Done` is always declared and mapped from the natural completion.
  - Several literal outcomes (a JSON list, literal or `Object`) become the graph `Complete`'s literal `outcomes` list of
    reference keys, so the caller follows every one of them (spec 1208 FR-029); one outcome stays `outcome`. An
    expression outcome, and several outcomes in a workflow that is not reusable (Elsa 4 `finish` names one), are
    `ELS3-ACTIVITY-UNSUPPORTED-MODE`. An undeclared outcome is declared with a warning; a declared outcome nothing
    reaches is dropped with a warning.
- **Flowcharts** (spec 239) become Elsa 4 Flowcharts with their connections:
  - A connection keeps its source, target and port. The port is mapped by the map's outcome rules (an Elsa 3 `If` or
    `Switch` completes with `Done`; Elsa 4's completes with `True`/`False` or a case match, so one connection becomes
    one per Elsa 4 outcome), or kept when the Elsa 4 activity emits it. Target ports and vertices have no behaviour and
    are dropped.
  - A RunJavaScript `Done` connection stays on `Done`, with or without possible outcomes (spec 1208, #3295).
  - A Switch or FlowSwitch in `MatchFirst` mode (the default) gets a synthesized `Value` that returns the first true
    case's label. In `MatchAny` mode (`MatchAny`, `matchAny` or `1`) it becomes the Elsa 4 Switch's `MatchAny` mode,
    each case keeping its own condition (read with JavaScript truthiness, as in `MatchFirst`), so every true case's
    connection runs, or `Default` when none is true (spec 1208 FR-028). S-12 also recursively converts structured
    MatchAny case activities, waiting for every selected body before completion. Empty selected cases suppress Default.
    A newly enabled body-containing MatchAny Switch refuses owning labels `Break`/`Done` and effective or unproven
    native Break escaping a body with localized `ELS3-ACTIVITY-UNSUPPORTED-MODE` guidance. Rename control labels or
    restructure the body. Safe unused declared ports and supported case-local consuming loops remain convertible;
    a script-selected Break can change internal container behavior even if an outer container consumes it. Expression
    modes and unknown modes remain refused. A newly enabled body also refuses invalid Done co-selections and
    unproven native-completion flow through helper aliases, eval/Function compilation, prototype constructors or
    unresolved computed prototype/callable access. Numeric literal indexes, named data properties, protected variable
    reads and proved ordinary scripts remain eligible. These scoped proof limits do not change standalone modes.
    Every case carries a stable identity.
    A SendHttpRequest with expected status codes has no `Done` in Elsa 4, so its `Done` connection becomes one per
    code and one for the unmatched status code: Elsa 3 fired `Done` with every response, but not on "Failed to
    connect" or "Timeout". `ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE` warns only that, without expected codes, a non-2xx
    response goes to `Failed`.
  - `startNodeId` is always set: a `Start` activity's target, else the first start-capable child, else the first child
    with no inbound connection, else the first child.
  - `FlowFork`, `FlowJoin` and `Start` leave no node: a fork becomes several connections on one port (fan-out), a join
    becomes the implicit join of what follows it, `Start` becomes the start node.
  - Joins: an implicit join and `WaitAllActive` convert. `WaitAny` converts when its branches cannot run at the same
    time; `WaitAll` when every branch provably runs. Otherwise `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`: Elsa 4 has no
    join that cancels the other branches, nor one that skips when a branch did not run.
  - A loop converts only back to the start node when nothing else leads into it; otherwise
    `ELS3-ACTIVITY-GRAPH-LOOP-UNSUPPORTED` (use a While, For or ForEach).
  - Other refusals: `ELS3-ACTIVITY-GRAPH-DUPLICATE-NODE-ID` (two activities of a flowchart share an id),
    `ELS3-ACTIVITY-GRAPH-CONNECTION-INVALID` (unknown node, or connections on a non-flowchart),
    `ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED` (a port the Elsa 4 activity does not emit), `ELS3-ACTIVITY-GRAPH-START-AMBIGUOUS`
    (a `Start` or fork at the start that leads to several nodes).
  - A connection Elsa 3 never follows (leaving `Complete`, `End` or `Break`, or a fork port outside its branches) is
    removed with `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`.
  - Several start triggers stay in one flowchart. Elsa 4 starts a run at the trigger that fired (spec 238). A start
    trigger that another activity leads into, and that is not the start node, is
    `ELS3-ACTIVITY-GRAPH-START-TRIGGER-MISPLACED`: Elsa 4 cannot begin a run there.
  - Conversion assumes the host's default `deadPathJoin` flowchart join option.
- **Imported reusable activities are published after the import commits** (spec 246). The commit writes each new
  activity definition with no head and the draft of its first selected version; the import's publish phase then
  publishes every selected version through the activity publisher under the plan's version id, dependencies first and
  the versions of one definition in ascending order. The apply, a replay and a status read report each version's
  publication state; a version the publisher refuses blocks only the versions that use it, and a later apply of the
  same export (a new idempotency key) publishes what is still missing. Publishing needs the
  `Elsa3ImportActivitiesPublishing` feature; without it, an apply that selects a reusable workflow is refused with
  `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE`.
  - Conversion runs the selected reusable versions in that publish order, so a caller routes on the outcomes the
    target's own conversion declares (its published contract).
  - A reusable body's natural completion maps from its root's own `Done` key to the boundary's `done`.
  - Refused until Elsa 4 graph activities support them: a reusable whose body starts from a trigger
    (`ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER`; 'Can start workflow' on a non-trigger only picks the start node and is fine), uses an engine intrinsic (SetVariable, SetOutput, Correlate, SetName, Finish, or a captured HTTP Endpoint header or query projection)
    (`ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`, #3023).
  - A reusable body that reads its inputs, its variables or another activity's result, or captures an output into
    one of its variables, converts as a workflow body does: an Elsa 4 graph activity is a closed scope whose request
    is its own inputs and whose root variables are its own (spec 265, #3027).
  - What to change in Elsa 3 before exporting: [Elsa 3 migration readiness](../../../../docs/reference/elsa3-migration-readiness.md).

## Migrating Elsa 3 JavaScript

Elsa 4 evaluates JavaScript in a sandbox. Each expression sees the parameters it declares (`args`) and the workflow
variables it reads by name (`variables`, spec 1222). It has the native `Date`, `Temporal`, `Intl` and `Math` (including
`Math.random`) on the host clock, in UTC with the invariant culture, and the built-in functions of spec 1223; its values
are JSON, so no live .NET object reaches a script. The converter therefore converts an Elsa 3 construct only when Elsa 4
gives the same result. Anything else refuses the whole definition with a stable code. The guidance in each refusal says
what to change in the Elsa 3 workflow so that it converts. Specs 236, 247 and 1223 hold the details (spec 247:
`specs/247-elsa3-js-host-functions/contracts/javascript-conversion.md`; spec 1223:
`specs/1223-expression-capabilities/contracts/conversion-and-diagnostics.md`).

**Date interpretation.** Elsa 3 evaluated script dates in its server's time zone and culture. The `Elsa3Mapping` feature's
`DateInterpretation` setting says how the converter reads Elsa 3 local time (spec 1223 FR-034): `Refuse` (the default)
refuses zone-dependent date usage with guidance; `AssumeUtc` converts it as UTC with the warning
`ELS3-BEHAVIOUR-DATE-ASSUMED-UTC` on each affected expression; `SourceTimeZone`, with `SourceTimeZone` set to the Elsa 3
server's IANA zone (for example `Europe/Amsterdam`), rewrites each zone-dependent call into explicit `Temporal` code in
that zone through one helper written into the converted expression. Every expression still evaluates in UTC. A
workflow whose scripts the setting decided gets the info `ELS3-DATE-INTERPRETATION`.

### What converts

| Elsa 3 | Elsa 4 |
|---|---|
| `getVariable("x")`, `variables.x`, `variables["x"]` in an input expression or a RunJavaScript body | `variables.x`, spelled as the variable is declared; publish pins it to the nearest declaring scope. A loop's current value becomes the loop's own `variables.currentItem` or `variables.index` |
| `getVariable(name)`, `variables[name]` (a computed name) in an input expression or a RunJavaScript body | Kept as written; the Elsa 4 run-time lookup resolves the name nearest scope first and records the read. Inside a loop, or where a name is declared in more than one visible scope, the conversion warns (`VF-IMP-011`) |
| `getInput("x")`, and a body's other reads (inputs, activity results, metadata) | A declared parameter, read as `args.x`. A body's variable reads are not arguments: it reads them over its variable session (spec 1222 FR-026a, FR-033) |
| `{{ Variables.x }}` in Liquid | `{{ variables.x }}` |
| `get{Name}()` generated for a variable (`get` + the name with its first letter upper-cased) or a workflow input (`get` + the name as written) | The same read as `getVariable`/`getInput`. Elsa 3's precedence applies: an activity output accessor `get{Output}From{Activity}()` shadows both and is refused; an input shadows a variable with the identical accessor name. In a reusable workflow, Elsa 3 declared inputs as variables, so only the variable-style name exists |
| `getCorrelationId()`, `getWorkflowInstanceId()` | A `WorkflowInstance` parameter that reads the running instance's correlation ID (null when it has none) or instance ID |
| `isNullOrEmpty(x)`, `isNullOrWhiteSpace(x)` | An inline function with .NET's semantics, including .NET's whitespace characters |
| `newGuid()`, `newGuidString()`, `getGuidString()`, `newShortGuid()`, `getShortGuid()`, `parseGuid(s)` | The Elsa 4 built-ins of the same name, which return Elsa 3's text as a string: the canonical 36-character form, or Elsa 3's short form (Base64 of the GUID's bytes without `/`, `+`, `=`). A GUID used as text (`'id-' + newGuid()`, `newGuid().toString()`) gives the same text |
| `stringToBase64(s)`, `stringFromBase64(s)` | The Elsa 4 built-ins of the same name: UTF-8 text and standard Base64, as in Elsa 3 |
| A custom function `getSettings(…)` that a registered host function declares it replaces (`new JavaScriptHostFunctionDeclaration("acme.getSettings", …) { ReplacesGlobalFunctions = ["getSettings"] }`) | A call to that host function, `acme.getSettings(…)`, with its arguments converted as written. Elsa 4 host functions carry a namespace prefix (ADR 0045 §5 D-5); the migration plan records which prefix each function gets. Two functions claiming one name, a variable or input whose generated accessor has the same name, or a script that declares the namespace root are refused as ambiguous (`VF-IMP-002`) |
| `toJson(x)` | `JSON.stringify(x)`, with the warning `ELS3-BEHAVIOUR-TOJSON-STRINGIFY`: Elsa 3 escaped `<`, `>`, `&`, `'`, `+`, `` ` `` and characters outside the Basic Multilingual Plane, trimmed a `Date`'s fractional seconds, wrote `undefined` as `null` and failed on `NaN`. Elsa 4 has no `toJson` |
| `Math.random()` | Native `Math.random()` |
| `Date.now()`, `new Date()` as an instant, `getTime()`, epoch arithmetic, `new Date(number)`, `Date.UTC(…)`, `toISOString()`, the `getUTC*`/`setUTC*` methods, parsing text with `Z` or an offset, parsing a date-only text | Native `Date` on the host clock, under every date interpretation |
| A read of a `DateTime` or `DateTimeOffset` variable or input | A `Date` of the same instant (`v == null ? v : new Date(v)`), because Elsa 3 handed scripts a `Date` and Elsa 4 hands over ISO 8601 text |
| Local date and time: `getHours()` and the other local getters and setters, `new Date(y, m, …)`, parsing text without an offset, parsing a value that is not a literal | Under `AssumeUtc`: as written, in UTC, with a warning. Under `SourceTimeZone`: rewritten into `Temporal` code in the server's zone, with `Date`'s roll-over of out-of-range fields and its handling of skipped and repeated hours. Refused under `Refuse` (below) |
| `toLocaleString(locale)`, `localeCompare(other, locale)`, an `Intl` constructor with a locale | As written. On a date, the text also needs an explicit `timeZone` (below) |
| A top-level `return` | The body runs as a function. A trailing expression gets a `return`; a bare `return;` produces no value, normalized only by the pinned `UndefinedAsNull` policy |
| A statement-form expression | A function that returns its last expression |
| A RunJavaScript body | Reads go through the `arguments` input, and the completion value becomes a `return`. A body whose last line is a `//` comment ends with a newline, so the comment cannot break Elsa 4's script wrapper (#3171) |
| An expression whose value is `undefined` (a missing property, a function that returns nothing) | The pinned `UndefinedAsNull` policy gives null when the converter option is enabled; otherwise the native boundary refuses consumed undefined. Expression source is not rewritten with `?? null`. A bound RunJavaScript result uses the same explicit boundary. |
| `setVariable('x', v)`, a generated `setX(v)` in a RunJavaScript body of a regular workflow | `variables.x = v` (`variables["x"] = v` when the name is not an identifier), a whole assignment written with the step (spec 1231 FR-032, Q6). Where the call's value could be read (the script's last statement, or inside an expression) it becomes `void (variables.x = v)`, since Elsa 3's setters returned nothing |
| `setVariable(name, v)` with a computed name in a RunJavaScript body of a regular workflow | `variables[name] = v`: the name resolves when the script runs, nearest scope first; an undeclared name faults the step, where Elsa 3 created the variable (spec 1231 FR-033 as amended, #4899). A Run JavaScript body gets no import check of which names it can reach, as for a computed read in a body |
| `getVariable('x').m = v`, `getX().m++`, `variables.x.m = v` in a RunJavaScript body of a regular workflow | The same member change through `variables.x`: written as the stored value with that change (ADR 0090 C2, C3) |
| `setOutcome(…)`, `setOutcomes(…)` in a RunJavaScript body | The body returns `complete(<value>, <pending selection>)` (spec 1208). Each call replaces the pending selection, and a body that sets nothing completes with `Done`. Literal names join the possible outcomes. A computed name, when the activity lists possible outcomes, selects any name outside them as a converter-declared outcome "Unlisted outcome" with no connection, so that path ends as in Elsa 3 (info `ELS3-SCRIPT-OUTCOME-CATCHALL`) |

### What is refused, and what to change in Elsa 3

| Elsa 3 construct | Code | Change in the Elsa 3 workflow |
|---|---|---|
| `Guid`, `Random`, `crypto.randomUUID()` | `VF-IMP-005` | Use `newGuid()` for a GUID, or `Math.random()` for a random number |
| `DateTime`, `DateTimeOffset`, `TimeSpan` | `VF-IMP-005` | Use JavaScript's `Date` or `Temporal` |
| Local date and time (see above) under the date interpretation `Refuse` | `VF-IMP-005` | Set the converter's date interpretation to `SourceTimeZone` with the Elsa 3 server's IANA zone, or to `AssumeUtc`; or use UTC methods or an explicit offset |
| Local text on a date: `toString()`, `toDateString()`, `toTimeString()`, `Date()`, `toLocaleString(locale)` or `Intl.DateTimeFormat(locale)` without a `timeZone`, under `Refuse` or `SourceTimeZone` | `VF-IMP-005` | Use `toISOString()`, or pass an explicit `timeZone` and locale. Elsa 3's local text depended on the server's operating system. Under `AssumeUtc` it converts with a warning |
| `toLocaleString()`, `toLocaleDateString()`, `toLocaleTimeString()`, `localeCompare` or an `Intl` constructor without an explicit locale | `VF-IMP-005` | Pass an explicit locale (for example `'nl-NL'`). Elsa 3 used the server's culture; Elsa 4 uses the invariant culture |
| A member of a value that was a live .NET object in Elsa 3: the result of `newGuid()` or `parseGuid()` (other than `toString()`), a variable or input of a wrapped .NET type (`TimeSpan`, `Uri`, …), a .NET member of a list or dictionary (`Add`), or such a value passed to `toJson`; a .NET type (`new Uri(s)`, `Enumerable.Range(…)`); a PascalCase method on a value the script did not define (`items.Where(f)`) | `VF-IMP-012` | Rewrite it with JavaScript (the string and array methods, `Date`, `Intl`, `JSON`) or a built-in function. Elsa 4 values are JSON. The same check runs at publish (`Scripts/DotNetMemberUsage`) |
| `setOutcome(…)`, `setOutcomes(…)` in an input expression | `VF-IMP-005` | Move the call into a RunJavaScript, or route with a Decision or Switch activity. Only a RunJavaScript script selects outcomes in Elsa 4 |
| `setOutcome(…)`, `setOutcomes(…)` with a computed name in a RunJavaScript whose possible outcomes are empty | `VF-IMP-005` | List every outcome the script can set in the activity's possible outcomes |
| A RunJavaScript body that calls `setOutcome` and declares its own `complete` | `VF-IMP-005` | Rename the script's `complete`. Elsa 4 reserves it for the returned completion form |
| Byte and stream helpers (`bytesToString`, `bytesToBase64`, `streamToBytes`, …) | `VF-IMP-005` | Move the conversion into an activity. Binary data is not a JSON value |
| A custom function a host function replaces (`ReplacesGlobalFunctions`), used other than in a direct call: `const f = getSettings`, `items.map(getSettings)`, `o.fn = getSettings` | `ELS3-REPLACED-FUNCTION-REFERENCE` | Call it directly, for example `items.map(item => getSettings(item))`. The converter rewrites only direct calls |
| `getConfig(…)`, `getConfiguration(…)`, definition and instance-name readers, `getLastResult()` | `VF-IMP-005` | Pass the value in as a workflow input, or capture the producing output into a variable. A host that rebuilt one of them as a prefixed host function can declare it replaces the name (`ReplacesGlobalFunctions`) instead |
| `setVariable`, `set{Name}(…)`, mutating a value read from the workflow, in an input expression | `VF-IMP-005` | Move the assignment into a Set Variable activity, or into a RunJavaScript, whose script can set variables. Elsa 4 input expressions only read |
| In a RunJavaScript body of a reusable workflow, `setVariable('<input>', v)` or `set{Input}(v)` for one of the reusable workflow's inputs (Elsa 3 held them as local variables) | `VF-IMP-005` | Declare a variable, initialize it from the input, and write that variable. A reusable workflow's script writes its own variables as a workflow's script does (spec 1241); its inputs are read-only |
| `setVariable('x', v)` for a variable the workflow does not declare (Elsa 3 created it on its first write) | `ELS3-SETTER-UNDECLARED-VARIABLE` | Declare the variable in the Elsa 3 workflow |
| `setVariable` or a setter for a loop's current value, a whole assignment `variables.x = v` or `getVariable('x')++` in a RunJavaScript body | `VF-IMP-005` | Use `setVariable('x', v)`; copy a loop's current value into another variable and set that |
| `setCorrelationId`, `setWorkflowInstanceName` outside supported inline bodies | `VF-IMP-005` | Move the call into a supported Run JavaScript body or use an intrinsic activity |
| `get{Output}From{Activity}()`, `getOutputFrom`, a computed input or output name | `VF-IMP-005` / `VF-IMP-006` | Capture the output into a variable and read the variable; use literal input and output names |
| A literal variable read that an enclosing container's or loop's declaration of the same name would capture in Elsa 4, or an outer loop's value read inside a nested loop of the same kind | `VF-IMP-010` | Rename one of the two, or copy the outer value into another variable first |
| A computed variable read in a workflow where an Elsa 3 variable holds a loop's current value (Elsa 4 never writes it) | `VF-IMP-010` | Read the loop's current value by its literal name |
| A script that declares its own `args` or `variables` around a converted read, a Liquid loop variable named `variables`, or a Liquid input or output named `variables` | `VF-IMP-002` | Rename the script's own name, or the input or output (#3296) |
| A variable or input whose accessor is a built-in name (`CorrelationId`) | `VF-IMP-002` | Rename it |
| lodash `_`, `moment`, CLR globals, Elsa 3 `args` | `VF-IMP-005` | Rewrite with plain JavaScript and declared reads |
| Any other undeclared function | `VF-IMP-005` | If the host rebuilt it for Elsa 4, register it as a prefixed host function that declares the name it replaces (`ReplacesGlobalFunctions`); otherwise replace it with plain JavaScript, or move it into an activity |
| Syntax that only parses in sloppy mode: `with`, a legacy octal literal such as `010`, duplicate parameter names, `delete` of a variable, a reserved word such as `let`, `static` or `yield` as a name | `VF-IMP-007` | Rewrite it without that syntax (write `0o10` or `8` for an octal literal). Elsa 4 runs JavaScript in strict mode |
| An assignment to a name the script never declares (`total = 1`) | `VF-IMP-007` | Declare it with `let` first. Elsa 3 created a global; Elsa 4's strict mode fails |
| `this` or `arguments` outside a function | `VF-IMP-007` | Read the value through `getVariable`, `getInput` or a local variable |
| Changing `arguments[i]`, or a parameter, in a function that reads `arguments` | `VF-IMP-007` | Copy the value into a local `let` variable and change that. Elsa 3 kept them linked; Elsa 4 does not |

Known differences that convert without a diagnostic, because they depend on how the script is called or on the data:
- `this` in a function called without a receiver was Elsa 3's global object; in Elsa 4 it is `undefined`.
- With the converter's default `ApplyElsa3NullPolicies=true`, undefined object members retain their keys as null,
  and null bound to a supported non-nullable scalar uses its type default (`0`, `false`, enum zero).
  With that option off, undefined object members are omitted (#3170) and null bound to a non-nullable input
  is refused with `VF-ACT-004` (#3172). See [workflow value policies](../../../../docs/reference/workflow-definition-values.md).
- Elsa 3 handed scripts mutable .NET values; Elsa 4 hands them frozen JSON values.

## Missing, blank and deliberately null members (spec 267 follow-up)

The built-in map distinguishes missing/defaulted Input wrappers (`{}`, typeName-only and typed `expression:{}`)
from a resolved typed Literal with a null or omitted value. Malformed expression metadata and memory-only input
wrappers are refused. A valid expression takes precedence over serialized memory-reference metadata, which the
legacy loader ignores; valid Variable expressions retain declaration-ID lowering. Explicit
collections and the String text `"null"` cannot acquire a declared default by being classified as unbound.

| Member | Conversion |
|---|---|
| Both HTTP request ContentTypes | Missing/defaulted or null with potentially non-null Content refuses the legacy no-body mismatch. Blank ContentType with potentially non-null Content refuses the selected 3.6 JSON writer's header difference: Elsa 4 appends a charset parameter. Empty or unproven computed/referenced String Content refuses the target's body/header omission. Known-null Content retains no body. |
| FlowSendHttpRequest.ExpectedStatusCodes | Missing/defaulted and bare JSON null emit `[200]`. Literal null, blank, `[]` and JSON-list text `[]` refuse: legacy Done on 404/500 differs from target Failed. Supported nonempty literal lists keep the existing outcome mapping. |
| For.OuterBoundInclusive / Delay.TimeSpan | Missing/defaulted and bare JSON null retain true/one minute. Literal null/omitted Literal value and blank literals emit false/explicit zero duration. |
| Timer.Interval | Missing/defaulted and bare JSON null retain one minute; authored intervals must be positive literals. Null, blank, zero, negative and invalid intervals refuse. |
| Both HTTP Methods / WriteHttpResponse.StatusCode | Missing/defaulted retain GET/200. Method literal null retains GET; authored whitespace refuses. Response status literal null, nonpositive/invalid literals and unproven computed ranges refuse; positive literal statuses remain raw. |
| Cron.CronExpression / RunJavaScript.Script | Missing/defaulted and blank uses refuse with guidance about disabled/no-work source behavior and the converted authoring contract. Fault.Message retains its accepted silent text difference. |

Refusals name the original member and an edit in Elsa 3 terms. Null refusals use `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`;
other source shapes use the existing value-contract family, without a duplicate required-input refusal. Custom maps
without these optional corrections keep their existing contracts. Runtime defaults, outcome selection and S-2's
portable null/missing/undefined policies are unchanged.

## Nesting budget

A converted workflow is stored as JSON, and System.Text.Json reads at most 64 levels by default. A node needs up to 6
levels for its own bindings, so an activity node may sit at most at **JSON level 58**; the root node sits at level 2.

Each container adds the levels its native structure puts between it and its children:

| Container | Levels per container | Deepest uniform nesting |
|---|---|---|
| Sequence, Flowchart (`structure > payload > activities > [i]`) | 4 | 14 containers |
| If, While, For, ForEach (`structure > payload > then`/`body`) | 3 | 18 containers |
| Parallel, Switch (`structure > payload > branches`/`cases > [i] > activity`) | 5 | 11 containers |

Mixed nesting adds up: an activity is convertible when 2 plus the levels of all its ancestor containers is at most 58.
For example, 4 Parallels (20) inside 8 Sequences (32) put a node at level 54, which converts. A deeper node is
`ELS3-ACTIVITY-NESTING-TOO-DEEP`, and the message names the level reached. A binding value that is itself deeply
nested JSON can still exceed the limit at a convertible depth; the import then fails before anything is committed.

## Converter revision

An imported Elsa 3 version n becomes Elsa 4 version `{n}.{R}.0`, where R is `Elsa3ConverterRevision.CurrentRevision`
(spec 235 R-8, #3921). At revision 0 the Elsa 4 version id is the Elsa 3 version id and a reusable's activity version
id derives from it alone, as every import did before revisions existed. From revision 1 on, both ids and the draft ids
derive from the Elsa 3 version id and R, so the same export imported after a converter upgrade adds `{n}.{R}.0`
beside `{n}.{R-1}.0` instead of colliding with it (`ELS3-REUSABLE-IDENTITY-COLLISION`); nothing the earlier revision
wrote is changed or unpublished. Bump the revision in the change that makes the converter emit different content for
the same input, never otherwise, and only once a released converter may have been used for durable imports. Before
GA no host keeps durable imports, so the revision stays 0 and rebuilding the store is the upgrade path (spec 235
R-10). An older Elsa 3 version of a reusable whose `{m}.{R}.0` would be below a newer version that is already
published is refused by the activity version bump rule and skipped with its dependents; the versions imported at the
earlier revision keep running.

## Registration

`Elsa3MappingFeature` registers the following:
- the built-in map as a singleton;
- the resolver, the replaced-type index, the normalizer and the converter, all scoped;
- one `IElsa3NodeRewrite` per rewrite strategy;
- the mappers and value-flow services;
- the importer and the reusable-activity materializer;
- the converter revision (`IElsa3ConverterRevision`), with `TryAdd`.

The replaceable contracts are listed in its [extension-point catalog](EXTENSION_POINTS.md).

### Workflow state in supported script bodies (spec 1234)

Direct `setOutput(name, value)`, `setCorrelationId(value)` and `setWorkflowInstanceName(value)` calls lower to the native
`workflowState` body capability. Same-body correlation/name getters read pending assignments. Arguments retain source
order and run once; setters still return undefined. Output assignments use Any JSON and preserve destination storage
and protection minima. Effects share the step's checkpoint with variables/results; failed evaluations save none.

Literal empty/whitespace identities refuse import with `ELS3-STATE-IDENTITY-NORMALIZATION`. Computed identities use the
once-evaluated generic `requireExactIdentity` guard and runtime `VF-STATE-005`; native identity setters retain their normal
whitespace-to-null contract. Use explicit null or a nonblank string. Input-expression writes, helper aliases, root
collisions, spread/wrong arity, `setInput` and `setResult` remain refused. Closed reusable graph writes remain outside this
capability ([#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023)); this does not deliver reusable
caller-output mapping. A reusable body's script does set its own variables (spec 1241).
