# Specialist inspection API extension points

Owner project: `Elsa.Agent.Specialists.Inspection.Api`
Domain: `Elsa.Agent`

Two MinimalEndpoints endpoints with capability `elsa.api.agent-specialist-inspection` v1. Feature
`SpecialistsInspectionApi` depends on `SpecialistsRuntime`, `WorkflowsPublishingApi` and `ApiCapabilities`; it is a
separate project so the authoring API stays composable without the runtime.

| Route | Permission | Answer |
|---|---|---|
| `GET agent/specialists/test-runs/{testRunId}/evidence` | `workflows/publishing:manage` | The generic draft Test Run's exact candidate and status, and the evidence of the specialist it placed as its root |
| `GET agent/specialists/invocations/{workflowExecutionId}/{activityExecutionId}/evidence` | `workflows/runtime:read`, then `workflows/activity-executions:inspect` on that run | One runtime occurrence's specialist evidence, for any run the caller may inspect |

Result values are re-authorized per run, with the runtime's own inspection permissions, on both endpoints. A value is
returned only when the caller holds `workflows/activity-executions:inspect` and `workflows/activity-executions:inspect-values`
for that run. Otherwise `valuesPermitted` is false, and the output keeps its capture mode but drops its value. Seeing a
run, or holding the Test Run permission, does not imply its values. Like the runtime's own activity execution reads, the
occurrence read answers 404 for a run outside the caller's tenant, an unknown run, a run the caller may not inspect and an
occurrence that is not a specialist, so it never confirms that a run exists.

The endpoint adds no execution path. A playground run starts with the generic `POST publishing/activity-drafts/{draftId}/test-runs`
and is polled and cancelled through `publishing/activity-test-runs/{testRunId}` and its `cancel` route. Here, `IActivityDraftTestRunService.GetAsync` authorizes the
run first, and the evidence comes from that run's own recorded root invocation through
[`ISpecialistInvocationInspector`](../../Core/EXTENSION_POINTS.md#invocation-evidence). Nothing in the request names
an invocation. `invocation` is null until the run has a root invocation (a refused run never does), and also when the
draft is not a specialist. An unknown or invisible run is 404 with `errorCode` `activity.test-run.not-found`, a foreign
tenant reference is 403, and other generic refusals are 422 with their code. A schema write refusal keeps its 409 answer
and code. Anything else, including a journal failure, is an opaque 500.

The two permission strings this project checks are owned by the Workflows Runtime API; it mirrors them rather than
referencing that project.

The project references `Elsa.Workflows.Publishing.Api` because the draft Test Run service, its rejection and its
permission constant live there rather than in `Elsa.Workflows.Publishing.Core`. The Elsa 3 import publishing bridge
depends on the same project for the same reason; moving these contracts to Core is tracked in #3762.

## Cross-domain contributions

The feature declares its API capability with `AddApiCapability` and keys its fault renderer by the endpoint group
owner. It contributes no permission: it reuses the Workflows Publishing permission that guards the generic Test Run reads.
