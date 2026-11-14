# Primitive release owner extension points

This optional adapter specializes the actual [primitive module](../EXTENSION_POINTS.md) through Releases.Core.
It references the concrete module only for its canonical activity alias and declared CLR member names.

| Strategy | Contract / layer | Signature and identity | Registration / callers / replacement |
|---|---|---|---|
| `PrimitiveReleaseOccurrenceInspector` | `IReleaseOccurrenceInspector` from Releases.Core | `ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)`; `Identity` is `elsa.primitive-release-profile/1`; `SupportedSites` claims the canonical WriteLine alias at `runtime.clr-activity/1` | Scoped `TryAddEnumerable` by `ActivitiesPrimitivesReleaseVerificationFeature`; the semantic builder/verifier dispatch it for exact captured sites. Replace the selected registration explicitly; duplicate claims fail registry composition. No default owner or event is introduced. |

Feature `ActivitiesPrimitivesReleaseVerification` depends on `WorkflowReleaseRuntime` and `ActivitiesPrimitives`.
It contributes the owning activity-module prerequisite and supplies no persistence, compiler or activation service.
