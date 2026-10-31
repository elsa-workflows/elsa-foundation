# BPMN hosting API

This package owns the HTTP boundary for [spec1017](../../../../../specs/1017-direct-bpmn-hosting/spec.md): publication, direct start, exact human completion, cancellation, service-incident inspection/retry and execution/command inspection. Authentication, tenant scope and authorization follow the existing Foundation API conventions. Semantic decisions remain in the BPMN implementation; durable coordination remains in the shared runtime.

Enable `ActivitiesBpmnHostingApi` together with the explicitly configured `ActivitiesBpmnHosting` feature. The generated NativeEndpoints map exposes:

| Method | Route | Permission |
|---|---|---|
| POST | `/bpmn/artifacts` | `bpmn:publish` |
| POST | `/bpmn/executions` | `bpmn:execute` |
| GET | `/bpmn/executions/{id}` | `bpmn:read` |
| GET | `/bpmn/executions/{id}/work` | `bpmn:read` |
| POST | `/bpmn/executions/{id}/work/{invocation}/complete` | `bpmn:complete-human` |
| POST | `/bpmn/executions/{id}/cancel` | `bpmn:cancel` |
| GET | `/bpmn/executions/{id}/incidents` | `bpmn:read` |
| POST | `/bpmn/executions/{id}/retry-service` | `bpmn:retry-service` |
| GET | `/bpmn/executions/{id}/commands/{request}` | `bpmn:read` |

The transport captures actor and tenant from the normalized principal. Resource checks precede execution/command disclosure; route identities cannot be replaced by body properties. Start and completion distinguish durable pending admission, consumed results, exact replay and conflicting request content. Obsolete/terminal completions retain a rejection receipt without changing execution state or its revision. The initial human binding defaults missing/empty outcomes to `Done` and refuses other outcomes.

Work and incident queries use bounded pages. Retry requires the exact incident, invocation and failed attempt; authorization precedes lookup and replay disclosure. A matching request replays the retained result without starting another worker. Cancellation can finish semantics while native cancellation or parent consumption remains owed; inspection preserves that pending-settlement distinction. HTTP202 proves durable admission, not semantic consumption or source acknowledgement.

New execution admission defaults off. Compose the built-in single-node mailbox and the qualified EF reader/checkpoint/material stores explicitly; replacing one participant does not inherit the original provider's qualification. [Declared model formats](../Core/README.md#declared-model-formats) cover human work, native services/ETL, held technical retry, duration boundaries and native signal catches. [Physical provider tests](../../../../../specs/1017-direct-bpmn-hosting/evidence/providers/model-storage/README.md) cover the common storage gate on four engines; actual host recovery and existing Studio compatibility are separately qualified on SQLite. [Final legacy/Studio evidence](../../../../../specs/1017-direct-bpmn-hosting/evidence/final-compatibility/README.md) does not complete #2907's direct-model Studio acceptance. Clustered model placement and broader bindings remain outside this profile. See [extension points](EXTENSION_POINTS.md) and the [process-host qualification](../../../../tests/Activities/Bpmn/Hosting/Tests/README.md).
