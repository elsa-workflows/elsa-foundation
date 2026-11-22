# Dashboard authoring with Weaver extension points

`DashboardWeaverFeature` (`StudioDashboardWeaver`) composes one model-agnostic `IDashboardAuthoringService` over the existing Weaver context/session/streaming/tool services. It depends on Weaver API and native Dashboard API support. The [module guide](README.md) describes disclosure and composition; [Weaver's catalog](../Elsa.Weaver/EXTENSION_POINTS.md) owns generic contracts and event semantics.

## Owned replacement

| Contract / layer | Default / registration | Consumer and replacement boundary |
| --- | --- | --- |
| `IDashboardAuthoringService`, Dashboard Core | Internal `WeaverDashboardAuthoringService`, scoped `TryAddScoped`; exactly one enforced at shell initialization | Native authoring prepares current canonical-viewer draft/declarations and streams through the installed Weaver provider. A replacement owns the same current-access, exact target/editor/revision, bounded disclosure and explicit native preview boundary. No SDK/credential types enter Core. |

## Cross-domain contributions

| Contract / Core layer | Implementation / lifetime | Stable IDs and role |
| --- | --- | --- |
| `IWeaverContextProvider` | `DashboardAuthoringContextProvider`, scoped `TryAddEnumerable` | Source/content `dashboards.authoring/1`, installed `dashboards.guidance/1`. Re-admits drafts and retained authoritative evidence against current native authority, exact canonical user vs authentication subject, resource and editor revision; no source result facts. |
| `IWeaverCapabilityProvider` | `DashboardWeaverCapabilityProvider`, stateless singleton `TryAddEnumerable` | `dashboards.authoring`, answer/read-only, build mode, `/questions` and `/dashboards`, requiring `studio/questions:read` and draft context. Advertised to the actual bootstrap catalog so the frontend's capability can be admitted. Discovery is metadata, never a permission grant. |
| `IWeaverTool`, with `IWeaverToolAuthorization` companion | `DashboardDatasetTool`, `DashboardQuestionTool`, `DashboardClarificationTool`, scoped `TryAddEnumerable` | `dashboard_discover_datasets`, `dashboard_preview_question`, `dashboard_request_clarification`. Read-only declarations, validated definitions or bounded clarification; owning source `dashboards.authoring`, schema1 and typed result IDs `dashboards.dataset-catalog`, `dashboards.question-preview`, `dashboards.clarification`. Preparation and execution independently recheck current subject/tenant/session/target authority. |

The feature contributes to `WeaverContextCollecting` / `CollectWeaverContext`, `WeaverCapabilitiesCollecting` / `CollectWeaverCapabilities`, and `WeaverToolsCollecting` / `CollectWeaverTools`. Existing single aggregators and Sequential delivery remain unchanged; no new event, registry or model loop is defined.

Tools return declarations/definitions only. Native preview runs outside provider disclosure, under the current viewer. No tool saves, shares or publishes a resource. Backend provider selection/options/credential resolution remain the existing Copilot adapter's responsibility. Retained context cannot survive current source/resource denial merely because an earlier authoring turn succeeded.
