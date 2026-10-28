# BPMN hosting API

This package owns the HTTP boundary for [spec1017](../../../../../specs/1017-direct-bpmn-hosting/spec.md): publication, direct start, exact work completion and execution/command inspection. Authentication, tenant scope and authorization follow the existing Foundation API conventions. Semantic decisions remain in the BPMN implementation; durable coordination remains in the shared runtime.

Enable `ActivitiesBpmnHostingApi` together with the explicitly configured `ActivitiesBpmnHosting` feature. The generated NativeEndpoints map exposes:

| Method | Route | Permission |
|---|---|---|
| POST | `/bpmn/artifacts` | `bpmn:publish` |
| POST | `/bpmn/executions` | `bpmn:execute` |
| GET | `/bpmn/executions/{id}` | `bpmn:read` |
| GET | `/bpmn/executions/{id}/work` | `bpmn:read` |
| POST | `/bpmn/executions/{id}/work/{invocation}/complete` | `bpmn:complete-human` |
| GET | `/bpmn/executions/{id}/commands/{request}` | `bpmn:read` |

The transport captures actor and tenant from the normalized principal. Resource checks precede execution/command disclosure; route identities cannot be replaced by body properties. Start and completion distinguish durable pending admission, consumed results, exact replay and conflicting request content. The initial human binding defaults missing/empty outcomes to `Done` and refuses other outcomes.

The initial opt-in profile supports plain start/end events, parallel gateways and explicitly bound human tasks through the built-in single-node mailbox and qualified EF persistence. New execution admission defaults off. Clustered model placement, additional bindings, runtime-only/changed-host qualification and Studio acceptance remain open in spec1017. See [extension points](EXTENSION_POINTS.md) and the [process-host qualification](../../../../tests/Activities/Bpmn/Hosting/Tests/README.md).
