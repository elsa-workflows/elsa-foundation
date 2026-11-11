# Workflow Runtime Weaver extension points

Owner project: `Elsa.Weaver.Workflows.Runtime`
Domain: `Elsa.Weaver.Workflows.Runtime`

The Runtime-side Weaver workflow contributions (spec 1193 D2, stories 8 and 9). The package references `Elsa.Weaver`,
`Elsa.Weaver.Core` and `Elsa.Workflows.Runtime.Core` only, never a Workflows Design assembly, so it composes in a
Runtime-only host; Design-side workflow contributions (drafts, versions, the library and templates) stay in
[`Elsa.Weaver.Workflows`](../Elsa.Weaver.Workflows/EXTENSION_POINTS.md). The shell feature is `WeaverWorkflowsRuntime`
(`WeaverWorkflowsRuntimeFeature`, which inherits `WeaverFeature`). Every contribution is
`internal sealed` and registers no fallback for the Runtime owner.

## Cross-domain contributions

These contribute to [Weaver foundation](../../Elsa.Weaver/EXTENSION_POINTS.md) through `Elsa.Weaver.Core`, under the Studio module
identity `Elsa.Studio.Workflows`, whose Executable Inspector discloses the context.

- `IWeaverContextProvider`: `WorkflowExecutableContextProvider`, source `workflows.executable-context`, kinds
  `workflows.executable` and `workflows.executable-guidance`, resource type `workflow-executable`. It admits the page's
  reference `workflows.executable` schema `1`, exactly `{ artifactId, artifactHash, sourceReferenceId? }`, targeted
  `workflow-executable` / artifact id / Artifact Hash (FR-005: an Executable's revision identity is its Artifact Hash). The
  server reads it itself: the host's `workflows/runtime:read` first (the permission the Runtime executable endpoints
  require), then `IWorkflowExecutableInspection.GetAsync(artifactId)`; a returned hash that differs from the bound one is
  stale. It admits `workflows.executable` (`AuthoritativeRead`) with the identity, Artifact Hash, root activity type and
  version, node count, live Source Reference count and the Source References (at most 32, `sourceReferencesTruncated`), plus
  installed guidance. Required refusals: `weaver.workflow.executable_context_invalid`, `weaver.workflow.permission_denied`
  ("Current workflow runtime read permission is required for the workflow Executable."),
  `weaver.workflow.executable_not_found` ("The workflow Executable is unavailable."), `weaver.workflow.executable_stale`
  ("The workflow Executable's Artifact Hash differs from the bound hash, so the context is stale."), and
  `workflow.executable.inspection_unavailable` (no or several inspections; owner-unavailable, 503). Without an
  Executable reference it contributes nothing. `AdmitDisclosureAsync` accepts only its own retained read, reads it again at
  the bound hash for the current turn, and withholds a retained context or `workflow_read_executable` result once the
  Executable is gone, changed or no longer readable.
- `IWeaverTool` with `IWeaverToolAuthorization`: `WorkflowReadExecutableTool`, `workflow_read_executable` contract `1`,
  `ReadOnly`, `ExecutionLocation.Server`, `workflows/runtime:read`, result `workflows.executable` schema `1`, no renderer,
  disclosed by `workflows.executable-context`. Arguments `{ artifactId? }`, strict; the Executable is the only admitted
  Executable context or the one `artifactId` names. It reads the Executable again at the bound hash and returns its
  identity, Artifact Hash, compiled graph (each node's executable and authored ids, activity type and version, structure
  kind, input bindings by name, source and sensitivity, never a value or source detail, child slots and connections) depth
  first and bounded: at most 300 nodes, each with at most 32 inputs and 64 connections, fewer of each when the result would
  exceed 24 KiB (well under the host's 32 KiB budget, #3710); a connection is kept only when both its ends are included
  (`connectionsOmitted` counts the rest), text values (types, versions, names and ports) are cut at 256 characters and
  counted in `truncatedFields` (identities never are), per-node `inputsTruncated` and `connectionsTruncated` and the overall
  `nodesIncluded` and `graphTruncated` say what was cut, and a read that does not fit even its smallest bound is refused as
  `weaver.workflow.executable_too_large` (413), never returned unbounded, the Source References (FR-028: those the Runtime read discloses under
  `workflows/runtime:read`) and the direct dependencies. Its authorization decides a concrete target by the Executable's
  current existence at the bound hash. Refusals: `weaver.workflow.executable_context_required` (409),
  `weaver.workflow.executable_stale` (409), `weaver.workflow.executable_not_found` (404),
  `weaver.workflow.executable_arguments_invalid` (400), `weaver.workflow.permission_denied` (403) and
  `workflow.executable.inspection_unavailable` (503).
- `IWeaverCapabilityProvider`: `WorkflowRuntimeWeaverCapabilityProvider` declares `workflow.executable-explain` (Answer,
  ReadOnly, `workflows/runtime:read`, kind `workflows.executable`, mode `explain`).

An Executable is immutable. The guidance tells the model to refuse any edit, run, delete or republish with zero effect and
to offer the source workflow's current draft instead (FR-015); this package offers no mutating tool.

## Runs (spec 1193 slice G)

- `IWeaverContextProvider`: `WorkflowRunContextProvider`, source `workflows.run-context`, kinds `workflows.run` and
  `workflows.run-guidance`, resource type `workflow-run`. It admits the run page's reference `workflows.run` schema `1`,
  exactly `{ workflowExecutionId, activityExecutionId? }`, targeted `workflow-run` / Workflow Instance id / revision (FR-005:
  the selected activity execution's id, or the instance id when none is selected; never "the latest run" of a definition).
  The server reads it itself: the host's `workflows/runtime:read` first (the permission the run endpoints require), then
  `IWorkflowRunInspection.GetAsync` without value resolution, which applies the run endpoints' per-run authority. It admits
  `workflows.run` (`AuthoritativeRead`): identity, Executable (artifact id and Artifact Hash), status, `inProgress`, counts,
  persistence mode and granularity, value-inspection permission and the selected execution's identity, type and status (or
  `unavailable`: `pruned`, `unsupported`), never an incident message or a value. A finished Run's projection is stable
  across admissions; an in-progress one carries `readAt`. Required refusals: `weaver.workflow.run_context_invalid` (also two
  selections of one Run, or an activity execution id equal to the instance id), `weaver.workflow.permission_denied` ("Current
  workflow runtime read permission is required for the workflow Run."), `weaver.workflow.run_not_found` ("The workflow Run is
  unavailable.", an unknown or hidden Run alike), `weaver.workflow.run_activity_not_found` ("The selected activity execution is
  not part of this workflow Run.") and `weaver.workflow.run_inspection_unavailable` (no or several inspections;
  owner-unavailable, 503). `AdmitDisclosureAsync` accepts only its own retained read and `workflow_read_run` results, reads
  the Run again for the current turn and withholds them once the Run (or its selection) is no longer readable, and withholds
  a retained result (`weaver.workflow.run_access_changed`) once it discloses more than the actor's current run access covers:
  incident details read under value inspection, or released values read under payload resolution, that the actor no longer
  holds; a result whose disclosure cannot be read is treated as exceeding.
- `IWeaverTool` with `IWeaverToolAuthorization`: `WorkflowReadRunTool`, `workflow_read_run` contract `1`, `ReadOnly`,
  `ExecutionLocation.Server`, `workflows/runtime:read`, result `workflows.run` schema `1`, no renderer, disclosed by
  `workflows.run-context`. Arguments `{ workflowExecutionId?, activityPageSize? (1–50), activityContinuationToken? }`, strict;
  no argument retargets the selection. It reads the bound Run twice: the first read resolves nothing and decides the bounds
  the result fits in (counting every value it may release at its full bound), the second resolves through the run endpoints'
  audited payload read exactly the values those bounds disclose, so the payload read audits only what the result shows. It
  returns `readAt`, `inProgress` and an `attribution` sentence, the instance,
  `canInspectValues` and `canResolveValues`, `incidents` (messages redacted without value inspection and withheld, as `sensitiveInputs`, when they could echo a
  sensitive input, an uninspected causal descendant, another Run's batches or a poisoned handler's text, `messageRedacted`; bounded with a truncation marker, `messageTruncated`), one activity page (`totalCount`,
  `nextContinuationToken`), the selected execution's summary, bookmarks, incidents, `values` (each with `disclosure`
  `resolved`, `resolvable`, `withheld` or `unavailable` and `unavailableReason` `notCaptured`, `pruned`, `unsupported` or
  `notPermitted`; a value only when `resolved`) and `stateView`, an `unavailable` list naming each withheld or unavailable item
  with its reason in product terms, and `runControls`, which points to the run page's own controls. It is hard-bounded at 24
  KiB, well under the host's 32 KiB check (#3710), by stepping down incidents, activities, values, bookmarks, characters per
  value or view and characters per incident message, to identities and counts only (`incidentsTruncated`, `itemsTruncated`,
  `valuesTruncated`, `bookmarksTruncated`, `valueTruncated`, `valueOmitted`, `viewOmitted`, `outcomesTruncated`); a read that
  does not fit even then is refused as `weaver.workflow.run_too_large` (413), never sent over budget. Refusals:
  `weaver.workflow.run_context_required` (409), `weaver.workflow.run_arguments_invalid` (400), `weaver.workflow.run_not_found`
  (404), `weaver.workflow.run_activity_not_found` (404), `weaver.workflow.permission_denied` (403) and
  `weaver.workflow.run_inspection_unavailable` (503). Its authorization decides a concrete target by the Run's (and the
  selection's) current visibility.
- `IWeaverCapabilityProvider`: `WorkflowRuntimeWeaverCapabilityProvider` also declares `workflow.run-explain` (Answer,
  ReadOnly, `workflows/runtime:read`, kind `workflows.run`, mode `explain`).

Weaver never changes a Run. The guidance tells the model to cite the Run's recorded incidents and activity executions, to
attribute an in-progress Run to its read time, never to guess a withheld or unavailable value, and to refuse any retry,
cancel, delete or dispatch with zero effect and a pointer to the run page's own controls (FR-029, story 9); this package
registers no mutating run tool.

## Owning contracts

[`IWorkflowExecutableInspection` and `IWorkflowRunInspection`](../../../../../Essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md)
(Workflows Runtime Core, D4) are the only Runtime reads. The Executable default reads through the same inspector as
`GET runtime/workflows/executables/{artifactId}` and returns a subset of that response; the Run default reads through the
run endpoints' own services (instance, activity execution, state view and value payload). See their catalog entries.

## Deployment shapes (SC-009)

- **Runtime-only:** this package alone serves the Executable and Run journeys. A draft, version or library reference is refused by
  the engine as `weaver.context.kind_unsupported`, and no Design tool is offered. `Elsa.Weaver.Workflows.Runtime.Tests`
  composes no Design assembly and asserts, AppDomain-wide, that no `Elsa.Workflows.Design*` assembly (nor
  `Elsa.Weaver.Workflows`) is loaded.
- **Design-only:** without this package an Executable or Run reference is `weaver.context.kind_unsupported` and
  neither `workflow_read_executable` nor `workflow_read_run` is offered (`Elsa.Weaver.Tests`, `WorkflowDeploymentShapeTests`).
- **Combined:** both packages compose side by side (`Elsa.Workbench` and the Docker compose shells select both features). A host that composes the Workflows Runtime API with Weaver must select `WeaverWorkflowsRuntime`: Studio's Executable Inspector and run page always send their context, and without this package every turn on those pages is refused as `weaver.context.kind_unsupported` (see the README's upgrade requirement).
- **Started hosts:** `WorkflowRuntimeOnlyHostTests` activates a real Runtime-only shell (Workflows Runtime API and this feature only) and reads a seeded Executable through the real `WorkflowExecutableInspection`; `WorkflowDesignOnlyHostTests` activates a real Design-only shell.

## SemVer

The package, its feature, context sources and kinds, tools and capabilities are new: **Minor** (slice F added the
Executable ones; slice G adds `workflows.run-context`, `workflows.run`, `workflows.run-guidance`, `workflow_read_run` and
`workflow.run-explain`, and the feature's description now names Runs). `IWorkflowExecutableInspection`,
`IWorkflowRunInspection` and their `WorkflowExecutableInspection*` and `WorkflowRun*` models in
`Elsa.Workflows.Runtime.Core` are additive public API (**Minor**, recorded in its `PublicAPI.Unshipped.txt`).
