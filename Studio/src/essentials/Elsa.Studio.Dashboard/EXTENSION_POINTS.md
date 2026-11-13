# Studio Dashboard contributions

`Client/src/module.tsx` registers this module through the public `ElsaStudioModuleApi`. The Home widget contribution contract remains the [Studio SDK contract](../Elsa.Studio.Hosting/Client/src/sdk/index.ts); the [module guide](README.md) describes its existing widget IDs. This module introduces no replaceable host service or global dataset registry.

## Native routes

`api.navigation.add` registers `dashboards` and `questions`. `api.routes.add` registers the list/create/detail `saved-dashboard:*` and `saved-question:*` routes, gated by `elsa.api.studio-dashboard/1`. Detail routes supply the resource workspace key. Native API transport, validation, results and resource writes remain current-viewer operations; no model is required.

## Cross-domain Weaver contributions

`native/weaverAuthoring.tsx` consumes the public Hosting SDK's `api.weaver` contributor slots. All contributions belong to `Elsa.Studio.Dashboard`, require `elsa.api.studio-dashboard-authoring/1` and use `/questions` or `/dashboards` surfaces as declared in source.

| SDK slot | Stable contribution IDs | Role |
| --- | --- | --- |
| `contextProviders.add` | `dashboards.authoring` | Required, internal-sensitivity declarative draft context; content type `dashboards.authoring/1`, `origin: clientSnapshot`, bound to client/generation, exact target and working revision. No native query results are attached. The backend owner must reauthorize and admit the snapshot before model disclosure. |
| `capabilities.add` | `dashboards.authoring` | Read-only answer capability requiring `studio/questions:read`; the backend must independently advertise/admit the same capability. Client registration grants no authority. |
| `promptStarters.add` | `dashboards.ask` | Starts supported-question authoring from `/questions`; consequential interpretations require clarification. |
| `resultRenderers.add` | `dashboards.question-preview`, `dashboards.clarification`, `dashboards.dataset-catalog` | Version1 typed result renderers. Proposal Preview uses the native API; Use is bound to the originating editor and revision; Save is separate. Current reader data remains outside the model transcript. |

`createDashboardAuthoringController` has one module-registration lifetime. Editors publish immutable snapshots and remove them on unmount/change; a stale target cannot receive a proposal. Backend session disposal or capability/provider withdrawal refuses further dispatch/application. Pending dispatch keeps its exact target through collection rather than retargeting on navigation. Correlated prompt results retire the pending request. Module-owned controller/registry references are retired with the host's backend module registration. React subscriptions remove their provider/draft/window listeners on unmount. No persisted authoring controller or cross-actor result cache is introduced.

Weaver retains conversation, provider, context admission, policy, tool invocation and cancellation ownership; the module contributes through those slots only. Its [backend authoring contract](../../../../specs/1217-ai-dashboards/contracts/weaver.md) defines independent disclosure checks. No new events are defined here; backend capability refresh uses the existing SDK event with the current session key.
