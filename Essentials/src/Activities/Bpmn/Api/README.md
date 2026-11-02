# BPMN hosting API

This package owns the HTTP boundary for [spec1017](../../../../../specs/1017-direct-bpmn-hosting/spec.md): publication, direct start, exact human completion, cancellation, service-incident inspection/retry and execution/command inspection. Authentication, tenant scope and authorization follow the existing Foundation API conventions. Semantic decisions remain in the BPMN implementation; durable coordination remains in the shared runtime.

Enable `ActivitiesBpmnHostingApi` together with the explicitly configured `ActivitiesBpmnHosting` feature. The generated NativeEndpoints map exposes:

| Method | Route | Permission |
|---|---|---|
| POST | `/bpmn/artifacts` | `bpmn:publish` |
| POST | `/bpmn/executions` | `bpmn:execute` |
| GET | `/bpmn/publications` | `bpmn:read` |
| GET | `/bpmn/executions` | `bpmn:read` |
| GET | `/bpmn/admission` | `bpmn:read` |
| GET | `/bpmn/executions/{id}` | `bpmn:read` |
| GET | `/bpmn/executions/{id}/inspection` | `bpmn:read` |
| GET | `/bpmn/executions/{id}/work` | `bpmn:read` |
| POST | `/bpmn/executions/{id}/work/{invocation}/complete` | `bpmn:complete-human` |
| POST | `/bpmn/executions/{id}/work/{invocation}/claim` | `bpmn:complete-human` |
| POST | `/bpmn/executions/{id}/work/{invocation}/complete-claimed` | `bpmn:complete-human` |
| POST | `/bpmn/executions/{id}/cancel` | `bpmn:cancel` |
| GET | `/bpmn/executions/{id}/incidents` | `bpmn:read` |
| POST | `/bpmn/executions/{id}/retry-service` | `bpmn:retry-service` |
| GET | `/bpmn/executions/{id}/commands/{request}` | `bpmn:read` |

The transport captures actor and tenant from the normalized principal. Resource checks precede execution/command disclosure; route identities cannot be replaced by body properties. Start and completion distinguish durable pending admission, consumed results, exact replay and conflicting request content. Obsolete/terminal completions retain a rejection receipt without changing execution state or its revision. The initial human binding defaults missing/empty outcomes to `Done` and refuses other outcomes.

Publication/run catalogs accept `limit` (default50/max500), optional `artifactId` and an opaque `continuationToken`. Each request reads one provider page and resource-authorizes disclosed items. A page may contain no authorized items and still offer continuation; callers choose whether to load another page. Continuations bind purpose, normalized subject, tenant, persistence scope, filter and page size. Source paging retains its initial provider live-clock value while rechecking current liveness and permission. Catalogs make no total or cross-request snapshot promise. The `elsa.api.bpmn-catalog` declaration advertises these two relations separately from optional authored publication or new-execution admission. Catalogs use Runtime readers, not Design storage. [Actual discovery evidence](../../../../../specs/1184-direct-bpmn-studio/evidence/catalog/README.md) does not establish direct Studio browser acceptance.

Work and incident queries use bounded pages. Retry requires the exact incident, invocation and failed attempt; authorization precedes lookup and replay disclosure. A matching request replays the retained result without starting another worker. Cancellation can finish semantics while native cancellation or parent consumption remains owed; inspection preserves that pending-settlement distinction. HTTP202 proves durable admission, not semantic consumption or source acknowledgement.

New execution admission defaults off. Compose the built-in single-node mailbox and the qualified EF reader/checkpoint/material stores explicitly; replacing one participant does not inherit the original provider's qualification. [Declared model formats](../Core/README.md#declared-model-formats) cover human work, native services/ETL, held technical retry, duration boundaries and native signal catches. [Physical provider tests](../../../../../specs/1017-direct-bpmn-hosting/evidence/providers/model-storage/README.md) cover the common storage gate on four engines; actual host recovery and existing Studio compatibility are separately qualified on SQLite. [Final legacy/Studio evidence](../../../../../specs/1017-direct-bpmn-hosting/evidence/final-compatibility/README.md) does not complete #2907's direct-model Studio acceptance. Clustered model placement and broader bindings remain outside this profile. See [extension points](EXTENSION_POINTS.md) and the [process-host qualification](../../../../tests/Activities/Bpmn/Hosting/Tests/README.md).

`elsa.api.bpmn-inspection` advertises the explicit persisted inspection relation. Its response contains the execution header/authority, frozen process/source/presentation, current work, retained native attempts/incidents and cancellation/parent-consumption proof. It contains no private adapter payload, authored input values, native completion values or invented human node history. The transport authorizes before loading the projection and reauthorizes its exact returned revision before disclosure. Requested native work is not labelled Running.

## Human work claims

Claim-required work (definition format 6) has exactly one current owner. `claim` names the exact invocation and attempt; the claimant is the normalized authenticated subject, never request content, and the existing mailbox and revision CAS commit the first accepted claim. There is no lease, expiry, team, reassignment or separate claim table, and the execution Authority is unchanged. `complete-claimed` additionally names the claim id and is accepted only for the current owner's exact claim and attempt. Refusals keep a rejection receipt without changing state: `already-claimed`, `claim-required` (unclaimed completion of claim-required work), `not-claimed`, `claim-not-current`, `foreign-claim`, `claim-not-required`, plus `obsolete-human-work` and `execution-terminal`. A timer, cancellation or terminal outcome retires the work; its claim stays as history and cannot complete or revive it. Exact replay recovers the retained outcome; the same request id with other content, including another subject, conflicts. Unclaimed bindings, including unclaimed work inside format 6, keep the original completion route, payload and fingerprints.

Work listings and inspection add `claimRequired` and the current `claim` (`claimId`, `claimantId`, `claimedAt`) only for claim-required work, so earlier profiles keep their JSON. `elsa.api.bpmn-claims` advertises the two claim relations separately from `elsa.api.bpmn-runtime`, so clients never offer claims to hosts that accept only unclaimed completion. Accepted claim work stays inspectable, claimable and completable with new admission off. [Claims evidence](../../../../../specs/1184-direct-bpmn-studio/evidence/claims/README.md) and the [actual Studio journey](../../../../../specs/1184-direct-bpmn-studio/evidence/operations/README.md) record the qualified behaviour and limits.

## Operational boundaries

Supported: the declared model formats, exact human completion and claims, published native service/ETL work with held technical retry, duration boundaries, native signal catches, cancellation with owed native settlement, and persisted inspection. XML import/export retains BPMNDI and owned direct-binding metadata; unknown meaning is diagnosed rather than reinterpreted. External effects are at least once: a native worker or ETL run that wrote output before failing keeps that output, and an accepted retry starts one replacement attempt that may repeat effects. Retained retry, replay and settlement never create further attempts, but destinations must tolerate a repeated record. The actual ETL recovery retains both the original physical output and the replacement records. Deferred and not offered: a task inbox or operator console, assignment teams or timed leases, arbitrary token or history alteration, live migration of running executions, clustered/Orleans placement and the task-runner host.

`elsa.api.bpmn-runtime` advertises the supported command/read relations; `GET bpmn/admission` reports current new-execution admission separately. Admission-off does not remove accepted-work inspection/completion. An ordinary composition without a selected model reader resolves the API but explicitly refuses model execution discovery; it never returns an empty success. [Actual composed and Runtime-only proof](../../../../../specs/1184-direct-bpmn-studio/evidence/inspection/README.md) preserves the exact-source whole stores and the remaining Studio gates.
