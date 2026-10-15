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
