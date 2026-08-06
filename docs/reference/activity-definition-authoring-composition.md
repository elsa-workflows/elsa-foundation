# Activity Definition Authoring Composition

Activity Definition authoring and reusable Activity Graph execution are separate host concerns.
Compose only the features needed by the host. The canonical provider identities, schemas, constraints,
and registered contributor implementations are documented in the
[Activity Graph Design extension catalog](../../src/Elsa/Activities/Graph/Design/EXTENSION_POINTS.md)
and [specification 092](../../specs/092-reusable-activity-definitions/spec.md).

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
