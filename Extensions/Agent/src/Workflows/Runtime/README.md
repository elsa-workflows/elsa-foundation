# Runtime-side workflow contribution to Weaver

This optional server module lets Weaver explain one exact, immutable workflow Executable. The Executable Inspector names an Executable by its artifact id and the Artifact Hash it shows; the server reads it through Workflows Runtime Core (`IWorkflowExecutableInspection`) under the host's `workflows/runtime:read`, refuses a read whose hash differs as stale, and `workflow_read_executable` returns its identity, Artifact Hash, compiled graph (input binding names, never values) and the Source References that point at it. Nothing is ever changed.

The package references Agent, Agent Core and Workflows Runtime Core only, so it composes in a Runtime-only host, where Design journeys are explicitly unavailable; the Design-side workflow contributions live in [`Elsa.Agent.Workflows`](../Elsa.Agent.Workflows/README.md). Select the shell feature `FoundationWorkflowsRuntimeAgent`. See [the module extension-point catalog](EXTENSION_POINTS.md).

## Cross-domain contributions

This package contributes to [Agent foundation](../../Elsa.Agent/EXTENSION_POINTS.md) through `Elsa.Agent.Core`: one `IAgentContextProvider` (`workflows.executable-context`), one `IAgentTool` with `IAgentToolAuthorization` (`workflow_read_executable`) and one `IAgentCapabilityProvider` (`workflow.executable-explain`). It consumes Workflows Runtime Core's [`IWorkflowExecutableInspection`](../../../../../Essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md) and registers no fallback for it. See [the module extension-point catalog](EXTENSION_POINTS.md) for each contribution's admission, bounds and refusals.

## Upgrade requirement

Studio's Executable Inspector sends its `workflows.executable` context whenever an Executable is shown. The inspector is only routable when the backend offers the Workflows Runtime API, but Studio cannot see which Weaver modules the backend composed: a host that composes the Workflows Runtime API and Weaver must also select `FoundationWorkflowsRuntimeAgent`, or every Weaver turn on that page is refused as `agent.context.kind_unsupported` ("No installed module admits the supplied context kind and owner."). The Workbench and the Docker compose shells select it.
