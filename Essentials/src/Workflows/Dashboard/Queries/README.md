# Workflow questions

`WorkflowDashboardQueries` contributes the dataset `elsa.workflows.runs`, schema `runs-v1`, through the native Dashboard query contracts. It depends on the Studio Dashboard and workflow Runtime API composition. Design organization is optional; current-folder fields appear only when the owning Design read contracts are available. Runtime gains no Design dependency.

## Permissions and source scope

Users need the source-owned `workflows/dashboard-queries:read` permission, independently of saved-question/dashboard permissions and Runtime inspection. Every contributing run must also pass the existing Runtime structure-inspection decision under the current tenant and request subject. A query selecting folder fields additionally requires the definition's owning organization to be currently readable through `IWorkflowDefinitionAuthorizer`.

The source evaluates its aggregate permission through Foundation's canonical permission service with the typed query as the resource. Host resource policies can refuse specific query/field/metric combinations. Neither a shared definition nor a drill proof transfers the author's permissions. Captured workflow values, arbitrary variables, correlation payloads and system metadata are not exposed as fields or business dimensions by this module.

## Meaning and bounds

Aggregate windows select actual `StartedAt` in `[from,to)`. Normal history pages are traversed without their date filters because those filters use a different effective update timestamp. Traversal remains finite and shares the native query's source-row, group, deadline and cancellation bounds; exhausting a bound refuses the aggregate instead of returning a sample.

Failure rate is currently faulted runs divided by eligible started runs, including incomplete runs in the denominator. No denominator is null; a nonempty cohort without faults has observed zero. Comparisons use exact fractions for predicates and ordering, and delta is percentage points. Numeric transport rounds to twelve decimal places. Grouping a folder calculates one rate over its eligible runs, never an average of workflow percentages.

Elapsed duration uses valid start/completion pairs of terminal runs. Missing or inverted pairs are excluded; zero elapsed time is valid. Exposed duration fields support count, sum, average, minimum and maximum. Count counts eligible values; other duration aggregates are null when no values are eligible. The separately selectable eligible-duration count helps interpret an average.

Excluding `testRun` leaves legacy `unknown` and `backgroundWeaverRun` unless explicitly filtered. It does not mean published production only. Current folder path/identity reflects current authorized Design organization. An unfiled definition is distinct from unreadable organization; the latter cannot contribute to a folder question. There is no historical folder snapshot.

Collection/retention coverage remains unknown: deleted, uncaptured or previously unavailable runs are not reconstructed. A successful result describes its authorized retained cohort, not a claim that all historical work was observed. Time-bucket comparisons are currently refused; single-period hour/day grouping and comparisons by supported non-time dimensions remain available.

## Drill behavior

Drill binds the exact question fingerprint, caller scope, resolved periods and selected group. It recomputes current authorized aggregate/HAVING membership before returning source records. Either bound comparison period may be selected; cursors also bind that choice. Permission, folder or source changes may require a fresh preview rather than replaying a prior group.

Drill integrity proofs use a private process-local signing key. A process restart or a different instance can invalidate an old proof, producing actionable refresh recovery; saved questions remain usable and produce fresh proofs. No restart-stable or multi-instance proof guarantee is claimed. No server result cache or export implementation is provided by this module.

## Cross-domain contributions

The module supplies `IDashboardDatasetExtension`, `IDashboardQueryValidationExtension`, `IDashboardQueryExecutionExtension` and `IDashboardQueryDrillExtension`. Dashboard dispatches these through its named Sequential events and one aggregator per operation. Dataset computation and authorization stay here; the generic query service and Weaver require no workflow-specific branch.

`WorkflowDashboardQueryPermissionExtension` contributes `workflows/dashboard-queries:read` through Foundation Identity's `IPermissionContributor`; see the [owning permission catalog](../../../Foundation/Identity/Core/EXTENSION_POINTS.md#ipermissioncontributor).
