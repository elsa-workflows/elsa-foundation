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
to the placed activity, so two uses of one activity, or one use in a loop, never share values. JavaScript in a body
sees the activity's variables by name (`variables.x`, `getVariable('x')`).

Publishing the activity refuses a body reference the activity does not declare or contain, with one of the
`activity.graph.body-*` codes in the [graph scope contract](../../specs/265-graph-activity-scope-reads/contracts/graph-scope-references.md).
Versions compiled before graph compiler `3.0.0` keep their behaviour: their bodies still resolve against the
caller until the activity is published again.
