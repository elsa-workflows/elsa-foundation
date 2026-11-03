# Specialist inspection API extension points

Owner project: `Elsa.Agent.Specialists.Inspection.Api`
Domain: `Elsa.Agent`

One NativeEndpoints endpoint with capability `elsa.api.agent-specialist-inspection` v1. Feature
`SpecialistsInspectionApi` depends on `SpecialistsRuntime`, `WorkflowsPublishingApi` and `ApiCapabilities`; it is a
separate project so the authoring API stays composable without the runtime.

| Route | Permission | Answer |
|---|---|---|
| `GET agent/specialists/test-runs/{testRunId}/evidence` | `workflows/publishing:manage` | The generic draft Test Run's exact candidate and status, and the evidence of the specialist it placed as its root |

The endpoint adds no execution path. Starting, polling and cancelling a playground run stay on the generic
`publishing/activity-drafts/{draftId}/test-runs` routes. Here, `IActivityDraftTestRunService.GetAsync` authorizes the
run first, and the evidence comes from that run's own recorded root invocation through
[`ISpecialistInvocationInspector`](../../Core/EXTENSION_POINTS.md#invocation-evidence). Nothing in the request names
an invocation. `invocation` is null until the run has a root invocation (a refused run never does), and also when the
draft is not a specialist. An unknown or invisible run is 404 with `errorCode` `activity.test-run.not-found`, a foreign
tenant reference is 403, and other generic refusals are 422 with their code. Anything else is an opaque 500.

## Cross-domain contributions

The feature declares its API capability with `AddApiCapability` and keys its fault renderer by the endpoint group
owner. It contributes no permission: it reuses the Workflows Publishing permission that guards the generic Test Run reads.
