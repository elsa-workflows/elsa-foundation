# Workflow Runtime Agent extension points

Owner project: `Elsa.Agent.Workflows.Runtime`
Domain: `Elsa.Agent.Workflows.Runtime`

The Runtime-side Weaver workflow contributions (spec 1193 D2, story 8). The package references `Elsa.Agent`,
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
  first and bounded: at most 300 nodes, each with at most 32 inputs and 64 connections, fewer of each when the result would
  exceed 24 KiB (well under the host's 32 KiB budget, #3710); a connection is kept only when both its ends are included
  (`connectionsOmitted` counts the rest), per-node `inputsTruncated` and `connectionsTruncated` and the overall
  `nodesIncluded` and `graphTruncated` say what was cut, and a read that does not fit even its smallest bound is refused as
  `agent.workflow.executable_too_large` (413), never returned unbounded, the Source References (FR-028: those the Runtime read discloses under
  `workflows/runtime:read`) and the direct dependencies. Its authorization decides a concrete target by the Executable's
  current existence at the bound hash. Refusals: `agent.workflow.executable_context_required` (409),
  `agent.workflow.executable_stale` (409), `agent.workflow.executable_not_found` (404),
  `agent.workflow.executable_arguments_invalid` (400), `agent.workflow.permission_denied` (403) and
  `agent.workflow.executable_inspection_unavailable` (503).
- `IAgentCapabilityProvider`: `WorkflowRuntimeAgentCapabilityProvider` declares `workflow.executable-explain` (Answer,
  ReadOnly, `workflows/runtime:read`, kind `workflows.executable`, mode `explain`).

An Executable is immutable. The guidance tells the model to refuse any edit, run, delete or republish with zero effect and
to offer the source workflow's current draft instead (FR-015); this package offers no mutating tool.

## Owning contract

[`IWorkflowExecutableInspection`](../../../../../Essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md) (Workflows Runtime Core,
D4) is the only Runtime read. Its default in the Workflows Runtime API reads through the same inspector as
`GET runtime/workflows/executables/{artifactId}` and returns a subset of that response (see its catalog entry).

## Deployment shapes (SC-009)

- **Runtime-only:** this package alone serves the Executable journey. A draft, version or library reference is refused by
  the engine as `agent.context.kind_unsupported`, and no Design tool is offered. `Elsa.Agent.Workflows.Runtime.Tests`
  composes no Design assembly and asserts, AppDomain-wide, that no `Elsa.Workflows.Design*` assembly (nor
  `Elsa.Agent.Workflows`) is loaded.
- **Design-only:** without this package an Executable reference is `agent.context.kind_unsupported` and
  `workflow_read_executable` is not offered (`Elsa.Agent.Tests`, `WorkflowDeploymentShapeTests`).
- **Combined:** both packages compose side by side (`Elsa.Workbench` and the Docker compose shells select both features). A host that composes the Workflows Runtime API with Weaver must select `FoundationWorkflowsRuntimeAgent`: Studio's Executable Inspector always sends its context, and without this package every turn on that page is refused as `agent.context.kind_unsupported` (see the README's upgrade requirement).
- **Started hosts:** `WorkflowRuntimeOnlyHostTests` activates a real Runtime-only shell (Workflows Runtime API and this feature only) and reads a seeded Executable through the real `WorkflowExecutableInspection`; `WorkflowDesignOnlyHostTests` activates a real Design-only shell.

## SemVer

The package, its feature, context source and kinds, tool and capability are new: **Minor**.
`IWorkflowExecutableInspection` and its `WorkflowExecutableInspection*` models in `Elsa.Workflows.Runtime.Core` are
additive public API (**Minor**, recorded in its `PublicAPI.Unshipped.txt`).
