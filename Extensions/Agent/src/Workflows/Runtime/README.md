# Runtime-side workflow contribution to Weaver

This optional server module lets Weaver explain one exact, immutable workflow Executable. The Executable Inspector names an Executable by its artifact id and the Artifact Hash it shows; the server reads it through Workflows Runtime Core (`IWorkflowExecutableInspection`) under the host's `workflows/runtime:read`, refuses a read whose hash differs as stale, and `workflow_read_executable` returns its identity, Artifact Hash, compiled graph (input binding names, never values) and the Source References that point at it. Nothing is ever changed.

The package references Agent, Agent Core and Workflows Runtime Core only, so it composes in a Runtime-only host, where Design journeys are explicitly unavailable; the Design-side workflow contributions live in [`Elsa.Agent.Workflows`](../Elsa.Agent.Workflows/README.md). Select the shell feature `FoundationWorkflowsRuntimeAgent`. See [the module extension-point catalog](EXTENSION_POINTS.md).
