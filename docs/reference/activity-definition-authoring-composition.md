# Activity Definition Authoring Composition

Activity Definition authoring and reusable Activity Graph execution are separate host concerns.
Compose only the features needed by the host. The canonical provider identities, schemas, constraints,
and registered contributor implementations are documented in the
[Activity Graph Design extension catalog](../../Essentials/src/Activities/Graph/Design/EXTENSION_POINTS.md)
and [specification 220](../../specs/221-reusable-activity-definitions/spec.md).

| Host role | Feature assembly in the catalog | Enabled shell feature |
|---|---|---|
| Provider-neutral Activity Definition authoring | `Elsa.Activities.Design.Api` | `ActivitiesDesignApi` |
| Activity Graph authoring, validation, and compilation | `Elsa.Activities.Graph.Design` | `ActivitiesGraphDesign` |
| Published Activity Graph execution | `Elsa.Activities.Graph.Runtime` | `ActivitiesGraphRuntime` |

`ActivitiesGraphDesign` depends on `ActivitiesDesignApi` and `WorkflowsPublishing` — the endpoint-free
publish engine, not the `WorkflowsPublishingApi` transport, so an authoring host composes the graph
provider without mounting the publish HTTP endpoints. When active, its provider contribution appears in
`GET /design/activities/authoring-capabilities`.

The stock `Elsa.Workbench` catalogs both graph assemblies and enables both graph features because its
default shell supports authoring and execution. A custom authoring host opts in by adding
`GraphActivitiesDesignFeature`'s assembly to its CShells catalog and enabling
`ActivitiesGraphDesign` in that shell. Enable the feature once; do not also register
`GraphActivityProvider` manually.

A runtime-only host can catalog and enable `ActivitiesGraphRuntime` without referencing or enabling
the Design project. Conversely, an authoring host that intentionally does not support Activity Graph
authoring omits `ActivitiesGraphDesign`; the authoring-capabilities response then does not advertise
that provider.

## Ending a reusable activity from any depth

A graph body ends its reusable activity early with the `Complete` catalog activity
(`Elsa.Activities.Graph.Runtime.Activities.Complete`, input `outcome`), which ships in `Elsa.Activities.Graph.Runtime`
and needs no extra feature. Its `outcome` is one literal boundary outcome reference key of a schema-2 graph. The
nearest enclosing reusable activity then completes with that outcome, whatever composites sit in between, and
durably cancels the rest of its body. `ActivitiesGraphDesign` validates these nodes when a graph is published and
rejects them in plain workflow definitions. Emitters such as importers and Studio write the node shape, rules and
diagnostic codes in the [Complete activity contract](../../specs/237-graph-boundary-completion/contracts/complete-activity.md)
(spec 237).

## Values inside a graph body

A graph activity is a closed value scope (spec 265). A node in its body reads the activity's own values, never those
of the workflow that calls it:

| A body node reads or writes | Authored as | Resolves to |
|---|---|---|
| One of the activity's inputs | a request read (`WorkflowRequest {memberKey}`), or Studio's `Input {referenceKey}` pick | the value the caller passed, else the input's default, else `null` |
| One of the activity's variables | a `Variable` reference with scope `workflow` | the variable in this invocation |
| A variable of a container inside the body | a `Variable` reference with the container's node id as scope | that container's variable |
| Another body node's result | an `ActivityResult` reference to the body node id | that node's result in the same invocation |
| An output captured into a variable | a node output bound to a `workflow`-scope `Variable` | writes the activity's own variable |

Inside a body, the `workflow` scope means the activity, because the body is authored like a workflow whose root is
the activity. When a workflow that uses the activity is published, placement rewrites these template-local references
to the placed activity, so two uses of one activity never share values, and each run of the activity reads its own
inputs and variables. JavaScript in a body
sees the activity's variables by name (`variables.x`, `getVariable('x')`, `get<X>()`); it reads inputs through
request parameters, as in a workflow. A graph published before graph compiler `3.0.0` and used inside a newer one
reads the newer graph as its caller.

Publishing the activity refuses a body reference the activity does not declare or contain:

| Code | Refused when |
|---|---|
| `activity.graph.body-input-unknown` | A request read, or Studio's `Input` pick, names an input the activity does not declare |
| `activity.graph.body-variable-unknown` | A `workflow`-scope variable read names a variable the activity does not declare |
| `activity.graph.body-variable-scope-invisible` | A container-scoped read names a scope that is not a container around the node |
| `activity.graph.body-producer-unknown` | A result read names a node that is not in the body, or a result the node does not produce |
| `activity.graph.body-capture-target-unknown` | An output capture targets anything but one of the activity's declared variables |
| `activity.graph.body-capture-type-incompatible` | An output capture's type does not fit the variable's type, and no conversion is authored |

A capture writes the activity's own variable row. That row is the variable's only store: the variables the body
reads are projected from it, not kept as a separate frame. Several nodes may capture into one variable one after
another, and later reads see the last captured value. ADR 0048 §5 requires publication to refuse captures that can
run concurrently into one variable, unless a merge or reduction governs them. That check is not built yet, for
workflow variables or graph variables ([#3198](https://github.com/elsa-workflows/elsa-foundation/issues/3198)).

The last two in the table that need the body nodes' resolved contracts (the result check and the capture type check)
run when the activity compiles; the others also run when a draft is validated. Ambient JavaScript reads and the key
of a container-scoped variable are not checked statically; they fail at run time, as in a workflow.
Versions compiled before graph compiler `3.0.0` keep their behaviour: their bodies still resolve against the
caller until the activity is published again.

A body that cannot be read is refused instead of being left unchecked, and so is a node the graph cannot run yet.
These checks run both when a draft is validated and when the activity compiles:

| Code | Refused when |
|---|---|
| `activity.graph.argument-malformed` | A node's `inputs` or `outputs` is not a list of arguments, or an argument has no `referenceKey` or a `value` that is not an expression object. An argument without a value is unbound, not malformed |
| `activity.graph.root-activity-malformed` | The root activity cannot be read as an activity node for any other reason |
| `activity.graph.intrinsic-unsupported` | A node is an engine intrinsic (Set variable, Set output, Correlate, Set name). Graph activities cannot run intrinsics yet ([#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023)) |
