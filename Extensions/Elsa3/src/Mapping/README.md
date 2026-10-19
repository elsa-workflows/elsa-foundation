# Elsa3.Mapping

Converts Elsa 3 workflow definitions to Elsa 4 Design state. The single-definition import and the Elsa 3 collection
import both use one converter, `IElsa3DefinitionConverter`. Start-time reconciliation (spec 235) builds on it. The
decision record for every Elsa 3 member is
[`specs/236-elsa3-activity-map/member-map.md`](../../../../specs/236-elsa3-activity-map/member-map.md); flowchart
connections follow [`specs/239-elsa3-flowchart-connections`](../../../../specs/239-elsa3-flowchart-connections/spec.md).

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
- `VF-IMP-*` are value-flow errors.

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

- **Output capture.**
  - An Elsa 3 output captured into a **workflow** variable becomes an Elsa 4 `Variable` output binding, so readers of
    the variable see the value.
  - A capture into a container variable is `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`; Elsa 4 captures into workflow
    scope only.
  - A loop's current value captured into a variable is read as the loop's scoped variable (`index`, `currentItem`)
    inside the loop body. Outside the loop that read is not representable.
- **HttpEndpoint headers and query string.** A captured `Headers` or `QueryStringData` gets a synthesized `set` node
  right after the endpoint. It rebuilds the Elsa 3 shape from Elsa 4's `Request` output (one value → string, several →
  array) into the captured variable, or into `elsa3.http.{node}.headers` / `.query` for a bare memory slot. The
  endpoint must sit in a list of activities (a Sequence or a Flowchart) so that a node can follow it; in a Flowchart the
  node is connected between the endpoint and the endpoint's former successors.
- **Defaults and required inputs.** A WriteLine without text gets `text = ""`, because Elsa 3 writes an empty line. An
  R2 default counts as bound even when it is blank. Any other required Elsa 4 input with no Elsa 3 value is
  `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND`.
- **`canStartWorkflow` on a non-trigger** (a WriteLine, SetVariable, …) only picks a flowchart's start node, as in Elsa 3
  when no trigger fired. It is dropped after that, without a diagnostic. HttpEndpoint and Event carry the flag; Cron and
  Timer require it.
- **Declared types.** Value-flow lowering compares a value with the Elsa 4 member in the shape normalization gave it:
  `HttpStatusCode` is an integer, `HttpHeaders` a dictionary of string arrays, `ObjectDictionary` a dictionary, and
  `List<T>` a list of `T`. A declared type that still does not fit is `VF-IMP-008`.
- **JavaScript** converts when Elsa 4 runs it the same, and is refused otherwise. See
  [Migrating Elsa 3 JavaScript](#migrating-elsa-3-javascript).
- **Reusable workflows** (collection import, `IsReusable`):
  - The body becomes a schema-2 graph.
  - `SetOutput` of a declared output writes a synthesized variable (`elsa3.output.{hash}`) that `outputMappings`
    expose.
  - `Complete` becomes the graph `Complete` activity, at any depth, with the outcome's reference key (`Done` →
    `done`, other names verbatim). `End` does the same with `done`, but only in the workflow's root flowchart: Elsa 3
    `End` ends only its enclosing flowchart, so elsewhere it is `ELS3-ACTIVITY-UNSUPPORTED-MODE`.
  - `Done` is always declared and mapped from the natural completion.
  - Several outcomes or an expression outcome → `ELS3-ACTIVITY-UNSUPPORTED-MODE`. An undeclared outcome is declared
    with a warning; a declared outcome nothing reaches is dropped with a warning.
- **Flowcharts** (spec 239) become Elsa 4 Flowcharts with their connections:
  - A connection keeps its source, target and port. The port is mapped by the map's outcome rules (an Elsa 3 `If` or
    `Switch` completes with `Done`; Elsa 4's completes with `True`/`False` or a case match, so one connection becomes
    one per Elsa 4 outcome), or kept when the Elsa 4 activity emits it. Target ports and vertices have no behaviour and
    are dropped.
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
    (`ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER`), uses an engine intrinsic such as SetVariable or SetOutput
    (`ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`, #3023), or reads one of its inputs, a variable or another
    activity's result (`ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED`, #3027).

## Migrating Elsa 3 JavaScript

Elsa 4 evaluates JavaScript in a sandbox. Each expression sees only the parameters it declares (`args`), and the
sandbox has no clock, no randomness and no host functions. The converter therefore converts an Elsa 3 construct only
when Elsa 4 gives the same result. Anything else refuses the whole definition with a stable code. The guidance in each
refusal says what to change in the Elsa 3 workflow so that it converts. Specs 236 and 247 hold the details (spec 247:
`specs/247-elsa3-js-host-functions/contracts/javascript-conversion.md`).

### What converts

| Elsa 3 | Elsa 4 |
|---|---|
| `getVariable("x")`, `getInput("x")`, `variables.x` | A declared parameter, read as `args.x` |
| `get{Name}()` generated for a variable (`get` + the name with its first letter upper-cased) or a workflow input (`get` + the name as written) | The same parameter as `getVariable`/`getInput`. Elsa 3's precedence applies: an activity output accessor `get{Output}From{Activity}()` shadows both and is refused; an input shadows a variable with the identical accessor name. In a reusable workflow, Elsa 3 declared inputs as variables, so only the variable-style name exists |
| `getCorrelationId()`, `getWorkflowInstanceId()` | A `WorkflowInstance` parameter that reads the running instance's correlation ID (null when it has none) or instance ID |
| `isNullOrEmpty(x)`, `isNullOrWhiteSpace(x)` | An inline function with .NET's semantics, including .NET's whitespace characters |
| A top-level `return` | The body runs as a function. A trailing expression gets a `return`; a bare `return;` returns `null` |
| A statement-form expression | A function that returns its last expression |
| A RunJavaScript body | Reads go through the `arguments` input, and the completion value becomes a `return` |

### What is refused, and what to change in Elsa 3

| Elsa 3 construct | Code | Change in the Elsa 3 workflow |
|---|---|---|
| `newGuid()`, `newGuidString()`, `newShortGuid()`, `Guid`, `Math.random()` | `VF-IMP-005` | Use `getWorkflowInstanceId()` when one unique value per run is enough; otherwise pass the value in as a workflow input |
| `new Date()`, `Date.now()`, `DateTime`, any use of `Date` | `VF-IMP-005` | Replace a wait loop with a Delay activity. Pass the current time or date values in as workflow inputs |
| `setOutcome(…)`, `setOutcomes(…)` | `VF-IMP-005` | Route with a Decision or Switch activity whose conditions carry the script's logic |
| `toJson(x)` | `VF-IMP-005` | Use `JSON.stringify(x)`, or `JSON.parse(JSON.stringify(x))` to copy a value. Elsa 3's `toJson` escaped HTML characters and kept .NET casing |
| `parseGuid(s)` | `VF-IMP-005` | Keep the GUID as a string |
| Byte, stream and Base64 helpers | `VF-IMP-005` | Move the conversion into an activity |
| `getConfig(…)`, definition and instance-name readers, `getLastResult()` | `VF-IMP-005` | Pass the value in as a workflow input, or capture the producing output into a variable |
| `setVariable`, `set{Name}(…)`, mutating a value read from the workflow | `VF-IMP-005` | Use a Set Variable activity |
| `setCorrelationId`, `setWorkflowInstanceName` | `VF-IMP-005` | Use a Correlate or Set Name activity |
| `get{Output}From{Activity}()`, `getOutputFrom`, a computed name | `VF-IMP-005` / `VF-IMP-006` | Capture the output into a variable and read the variable |
| A variable or input whose accessor is a built-in name (`CorrelationId`) | `VF-IMP-002` | Rename it |
| lodash `_`, `moment`, CLR globals, Elsa 3 `args` | `VF-IMP-005` | Rewrite with plain JavaScript and declared reads |
| Any other undeclared function | `VF-IMP-005` | Replace it with plain JavaScript, or move it into an activity |

A workflow that genuinely needs the current time or a fresh unique value cannot migrate until Elsa 4 has an explicit
capability for them (#3026). Elsa 4 deliberately strips them from expressions to keep them deterministic.

Known differences that convert without a diagnostic today: Elsa 4 evaluates in strict mode, and it rejects an
`undefined` result where Elsa 3 returned `null` (#3012). Elsa 3 handed scripts mutable .NET values; Elsa 4 hands them
frozen JSON values.

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

## Registration

`Elsa3MappingFeature` registers the following:
- the built-in map as a singleton;
- the resolver, the replaced-type index, the normalizer and the converter, all scoped;
- one `IElsa3NodeRewrite` per rewrite strategy;
- the mappers and value-flow services;
- the importer and the reusable-activity materializer.

The replaceable contracts are listed in its [extension-point catalog](EXTENSION_POINTS.md).
