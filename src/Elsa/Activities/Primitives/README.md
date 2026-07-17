# Elsa.Activities.Primitives

Primitive hand-written activities and the transient CLR activation implementation. This runtime feature
references no Design project.

`ActivitiesPrimitivesFeature` registers `ClrActivityActivator` as `IActivityActivator`. For each invocation
attempt the activator resolves the canonical type alias from `ClrActivityDescriptor`, creates an owned DI
scope, uses `ActivatorUtilities` so activity authors can use constructor injection, constructs a fresh CLR
instance, and hydrates its plain `[ActivityInput]` properties from the committed input snapshot. The
activation lease disposes both the activity and its scope.

`WriteLine` is the minimal shipped example: a plain annotated `string Text` property and one atomic
`ActivityUnit` result. It contains no argument wrapper or activity-owned value address.

Coverage lives in `tests/Elsa/Activities/Runtime/Tests`, especially the CLR activator, input hydrator,
pinned-input, and completion contract fixtures.
