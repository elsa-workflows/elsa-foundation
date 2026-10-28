# BPMN hosting contracts

This package is the thin consumer boundary for [direct BPMN hosting](../../../../../specs/1017-direct-bpmn-hosting/spec.md). Shared durable execution ports belong to `Elsa.Workflows.Runtime.Core`; BPMN command and inspection contracts belong here. The package does not depend on an interpreter, provider or API host.

[`IBpmnModelRuntime`](IBpmnModelRuntime.cs) is the scoped replacement port for immutable publication, direct start, exact human completion and execution/work/retained-command inspection. Its model material, caller and result DTOs are consumed by the BPMN implementation and authenticated API. [Extension points](EXTENSION_POINTS.md) identify the actual registration and safety requirements. Spec1017 remains in progress; these contracts do not claim its full scenario matrix or upgrade/Studio acceptance.
