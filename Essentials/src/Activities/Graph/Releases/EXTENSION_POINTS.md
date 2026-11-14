# Graph release owner extension points

This optional compiler-free owner adapter bridges the Graph provider/runtime seam and Publishing Releases Core.
The [Runtime consumer](../Runtime/EXTENSION_POINTS.md) and
[provider/runtime seam](../../../../../specs/221-reusable-activity-definitions/contracts/provider-runtime-seams.md)
remain the domain owners. The adapter supplies no compiler, material store, authorization policy or activation.

| Strategy | Contract / layer | Signature and identity | Registration / callers / replacement |
|---|---|---|---|
| `GraphReleaseOccurrenceInspector` | `IReleaseOccurrenceInspector` from Releases.Core | `ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)`; `Identity` is `elsa.graph-release-profile/1`; `SupportedSites` is the exact-key claim inventory | Scoped `TryAddEnumerable` in `GraphActivitiesReleaseVerificationFeature`; the shared semantic verifier/builder dispatch captured owner sites. Replace the selected registration explicitly; duplicate claims fail registry composition. There is no default owner or named event. |

The exact version-1 site claims are consumer descriptor and overlay `elsa.graph-activity`, provider
`elsa.activity-graph`, metadata `graph.templateHash` and `runtime.activityExecutionScope`. Unknown consumer/schema
surfaces remain Unsupported. The inspector reads only qualified captured members through
`IReleaseRuntimeSiteReader`; Runtime placement and CLR activity inspectors remain independently mandatory.
Feature `ActivitiesGraphReleaseVerification` depends on `WorkflowReleaseRuntime` and `ActivitiesGraphRuntime`.
The adapter's package references only Core contracts; its feature dependency does not load a compiler.
