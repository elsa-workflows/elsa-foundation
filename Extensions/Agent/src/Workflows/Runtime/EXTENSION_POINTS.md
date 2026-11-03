# Workflow Runtime Agent extension points

Owner project: `Elsa.Agent.Workflows.Runtime`
Domain: `Elsa.Agent.Workflows.Runtime`

The Runtime-side Weaver workflow contributions (spec 1193 D2, stories 8 and 9). The package references `Elsa.Agent`,
`Elsa.Agent.Core` and `Elsa.Workflows.Runtime.Core` only, never a Workflows Design assembly, so it composes in a
Runtime-only host; Design-side workflow contributions (drafts, versions, the library and templates) stay in
[`Elsa.Agent.Workflows`](../Elsa.Agent.Workflows/EXTENSION_POINTS.md). The shell feature is `FoundationWorkflowsRuntimeAgent`
(`FoundationWorkflowsRuntimeAgentFeature`, which inherits `FoundationAgentAbstractionsFeature`). Every contribution is
`internal sealed` and registers no fallback for the Runtime owner.

## Cross-domain contributions

These contribute to [Agent foundation](../../Elsa.Agent/EXTENSION_POINTS.md) through `Elsa.Agent.Core`, under the Studio module
identity `Elsa.Studio.Workflows`, whose Executable Inspector discloses the context.

- `IAgentContextProvider`: `WorkflowExecutableContextProvider`, source `workflows.executable-context`, kinds
  `workflows.executable` and `workflows.executable-guidance`, resource type `workflow-executable`. It admits the page's
  reference `workflows.executable` schema `1`, exactly `{ artifactId, artifactHash, sourceReferenceId? }`, targeted
  `workflow-executable` / artifact id / Artifact Hash (FR-005: an Executable's revision identity is its Artifact Hash). The
  server reads it itself: the host's `workflows/runtime:read` first (the permission the Runtime executable endpoints
  require), then `IWorkflowExecutableInspection.GetAsync(artifactId)`; a returned hash that differs from the bound one is
  stale. It admits `workflows.executable` (`AuthoritativeRead`) with the identity, Artifact Hash, root activity type and
  version, node count, live Source Reference count and the Source References (at most 32, `sourceReferencesTruncated`), plus
  installed guidance. Required refusals: `agent.workflow.executable_context_invalid`, `agent.workflow.permission_denied`
  ("Current workflow runtime read permission is required for the workflow Executable."),
  `agent.workflow.executable_not_found` ("The workflow Executable is unavailable."), `agent.workflow.executable_stale`
  ("The workflow Executable's Artifact Hash differs from the bound hash, so the context is stale."), and
  `agent.workflow.executable_inspection_unavailable` (no or several inspections; owner-unavailable, 503). Without an
  Executable reference it contributes nothing. `AdmitDisclosureAsync` accepts only its own retained read, reads it again at
  the bound hash for the current turn, and withholds a retained context or `workflow_read_executable` result once the
  Executable is gone, changed or no longer readable.
- `IAgentTool` with `IAgentToolAuthorization`: `WorkflowReadExecutableTool`, `workflow_read_executable` contract `1`,
  `ReadOnly`, `ExecutionLocation.Server`, `workflows/runtime:read`, result `workflows.executable` schema `1`, no renderer,
  disclosed by `workflows.executable-context`. Arguments `{ artifactId? }`, strict; the Executable is the only admitted
  Executable context or the one `artifactId` names. It reads the Executable again at the bound hash and returns its
  identity, Artifact Hash, compiled graph (each node's executable and authored ids, activity type and version, structure
  kind, input bindings by name, source and sensitivity, never a value or source detail, child slots and connections) depth
  first and bounded (at most 300 nodes, fewer when the result would exceed 28 KiB, under the host's 32 KiB budget, #3710;
  `nodesIncluded` and `graphTruncated` say so), the Source References (FR-028: those the Runtime read discloses under
  `workflows/runtime:read`) and the direct dependencies. Its authorization decides a concrete target by the Executable's
  current existence at the bound hash. Refusals: `agent.workflow.executable_context_required` (409),
  `agent.workflow.executable_stale` (409), `agent.workflow.executable_not_found` (404),
  `agent.workflow.executable_arguments_invalid` (400), `agent.workflow.permission_denied` (403) and
  `agent.workflow.executable_inspection_unavailable` (503).
- `IAgentCapabilityProvider`: `WorkflowRuntimeAgentCapabilityProvider` declares `workflow.executable-explain` (Answer,
  ReadOnly, `workflows/runtime:read`, kind `workflows.executable`, mode `explain`).

An Executable is immutable. The guidance tells the model to refuse any edit, run, delete or republish with zero effect and
to offer the source workflow's current draft instead (FR-015); this package offers no mutating tool.

## Runs (spec 1193 slice G)

- `IAgentContextProvider`: `WorkflowRunContextProvider`, source `workflows.run-context`, kinds `workflows.run` and
  `workflows.run-guidance`, resource type `workflow-run`. It admits the run page's reference `workflows.run` schema `1`,
  exactly `{ workflowExecutionId, activityExecutionId? }`, targeted `workflow-run` / Workflow Instance id / revision (FR-005:
  the selected activity execution's id, or the instance id when none is selected; never "the latest run" of a definition).
  The server reads it itself: the host's `workflows/runtime:read` first (the permission the run endpoints require), then
  `IWorkflowRunInspection.GetAsync` without value resolution, which applies the run endpoints' per-run authority. It admits
  `workflows.run` (`AuthoritativeRead`): identity, Executable (artifact id and Artifact Hash), status, `inProgress`, counts,
  persistence mode and granularity, value-inspection permission and the selected execution's identity, type and status (or
  `unavailable`: `pruned`, `unsupported`), never an incident message or a value. A finished Run's projection is stable
  across admissions; an in-progress one carries `readAt`. Required refusals: `agent.workflow.run_context_invalid` (also two
  selections of one Run, or an activity execution id equal to the instance id), `agent.workflow.permission_denied` ("Current
  workflow runtime read permission is required for the workflow Run."), `agent.workflow.run_not_found` ("The workflow Run is
  unavailable.", an unknown or hidden Run alike), `agent.workflow.run_activity_not_found` ("The selected activity execution is
  not part of this workflow Run.") and `agent.workflow.run_inspection_unavailable` (no or several inspections;
  owner-unavailable, 503). `AdmitDisclosureAsync` accepts only its own retained read and `workflow_read_run` results, reads
  the Run again for the current turn and withholds them once the Run (or its selection) is no longer readable.
- `IAgentTool` with `IAgentToolAuthorization`: `WorkflowReadRunTool`, `workflow_read_run` contract `1`, `ReadOnly`,
  `ExecutionLocation.Server`, `workflows/runtime:read`, result `workflows.run` schema `1`, no renderer, disclosed by
  `workflows.run-context`. Arguments `{ workflowExecutionId?, activityPageSize? (1–50), activityContinuationToken? }`, strict;
  no argument retargets the selection. It reads the bound Run again, with the selected execution's values resolved through
  the run endpoints' audited payload read, and returns `readAt`, `inProgress` and an `attribution` sentence, the instance,
  `incidents` (messages redacted without value inspection, `messageRedacted`), one activity page (`totalCount`,
  `nextContinuationToken`), the selected execution's summary, bookmarks, incidents, `values` (each with `disclosure`
  `resolved`, `resolvable`, `withheld` or `unavailable` and `unavailableReason` `notCaptured`, `pruned`, `unsupported` or
  `notPermitted`; a value only when `resolved`) and `stateView`, an `unavailable` list naming each withheld or unavailable item
  with its reason in product terms, and `runControls`, which points to the run page's own controls. It is bounded under 28
  KiB (the host's 32 KiB budget, #3710) by stepping down incidents, activities, values and characters per value
  (`incidentsTruncated`, `itemsTruncated`, `valuesTruncated`, `valueOmitted`, `viewOmitted`). Refusals:
  `agent.workflow.run_context_required` (409), `agent.workflow.run_arguments_invalid` (400), `agent.workflow.run_not_found`
  (404), `agent.workflow.run_activity_not_found` (404), `agent.workflow.permission_denied` (403) and
  `agent.workflow.run_inspection_unavailable` (503). Its authorization decides a concrete target by the Run's (and the
  selection's) current visibility.
- `IAgentCapabilityProvider`: `WorkflowRuntimeAgentCapabilityProvider` also declares `workflow.run-explain` (Answer,
  ReadOnly, `workflows/runtime:read`, kind `workflows.run`, mode `explain`).

Weaver never changes a Run. The guidance tells the model to cite the Run's recorded incidents and activity executions, to
attribute an in-progress Run to its read time, never to guess a withheld or unavailable value, and to refuse any retry,
cancel, delete or dispatch with zero effect and a pointer to the run page's own controls (FR-029, story 9); this package
registers no mutating run tool.

## Owning contracts

[`IWorkflowExecutableInspection` and `IWorkflowRunInspection`](../../../../../Essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md)
(Workflows Runtime Core, D4) are the only Runtime reads. Their defaults in the Workflows Runtime API read exactly what
`GET runtime/workflows/executables/{artifactId}` and the run endpoints (instance, activity execution, state view and value
payload) return.

## Deployment shapes (SC-009)

- **Runtime-only:** this package alone serves the Executable and Run journeys. A draft, version or library reference is refused by
  the engine as `agent.context.kind_unsupported`, and no Design tool is offered. `Elsa.Agent.Workflows.Runtime.Tests`
  composes no Design assembly and asserts, AppDomain-wide, that no `Elsa.Workflows.Design*` assembly (nor
  `Elsa.Agent.Workflows`) is loaded.
- **Design-only:** without this package an Executable or Run reference is `agent.context.kind_unsupported` and
  neither `workflow_read_executable` nor `workflow_read_run` is offered (`Elsa.Agent.Tests`, `WorkflowDeploymentShapeTests`).
- **Combined:** both packages compose side by side (`Elsa.Workbench` selects both features).

## SemVer

The package, its feature, context sources and kinds, tools and capabilities are new: **Minor** (slice F added the
Executable ones; slice G adds `workflows.run-context`, `workflows.run`, `workflows.run-guidance`, `workflow_read_run` and
`workflow.run-explain`, and the feature's description now names Runs). `IWorkflowExecutableInspection`,
`IWorkflowRunInspection` and their `WorkflowExecutableInspection*` and `WorkflowRun*` models in
`Elsa.Workflows.Runtime.Core` are additive public API (**Minor**, recorded in its `PublicAPI.Unshipped.txt`).
