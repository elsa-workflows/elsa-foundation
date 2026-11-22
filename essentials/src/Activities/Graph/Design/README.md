# Activity graph Design provider

`GraphActivitiesDesignFeature` owns `elsa.activity-graph` provider schemas 1 and 2. Its existing
authoring, compilation and reference-rewriting contracts are listed in
[EXTENSION_POINTS.md](EXTENSION_POINTS.md); the canonical provider/runtime boundary is specified by
[spec 221](../../../../../specs/221-reusable-activity-definitions/contracts/provider-runtime-seams.md).

The same singleton `GraphActivityProvider` also projects an owned manifest and public contract through
`IActivityDataSelectionDocumentProjector`. This is metadata projection only: it captures no scoped
analyzer, creates no detached scope, reads no store and persists no synthetic workflow. Activity Design
and Publishing coordinators authorize their actual resource and invoke the one scoped shared analyzer.
Malformed owned manifests fail explicitly; an unrelated provider/schema is not claimed. The existing
body-reference and capture diagnostics remain in force.

See the [owning analysis catalog](../../../Workflows/Design/Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md)
for scope/cardinality and admission rules. New runtime member instructions and Studio chooser behavior
are outside this Foundation leaf.
