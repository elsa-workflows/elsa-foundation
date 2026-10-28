# BPMN hosting contracts

This package is the thin consumer boundary for [direct BPMN hosting](../../../../../specs/1017-direct-bpmn-hosting/spec.md). Shared durable execution ports belong to `Elsa.Workflows.Runtime.Core`; BPMN command and inspection contracts belong here. The package does not depend on an interpreter, provider or API host.

Spec1017 is in progress. This initial project declares no usable direct-host API. Public contracts are added with their production consumers and tracked in the API files. Existing embedded `BpmnProcess` authoring/state remains owned by `Elsa.Activities.Bpmn`.
