# Workflow Design API

`Elsa.Workflows.Design.Api` is the supported management-client slice for authored workflows. It owns workflow definitions, first-class drafts, immutable Design versions, scoped-variable analysis, and context-sensitive activity input options. Publishing and executable inspection belong to their respective domain APIs.

The ownership and lifecycle rules are specified in [`specs/091-domain-owned-apis`](../../../../../specs/091-domain-owned-apis/spec.md). Terms such as *feature*, *API capability*, *draft*, and *workflow executable* are defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).

## Composition

Add `WorkflowsDesignApiFeature` to the active shell and compose a Workflow Design persistence provider. The feature registers its FastEndpoints slice, mediator handlers, scoped-variable authoring services, and contextual input-option resolver. Activity input options also read the Activity Design definition/version stores, so a host exposing that operation must compose Activity Design persistence.

This package does not reference or depend on `Elsa.Server`; the server application is only one possible reference composition.

## Supported routes

All routes are relative to the active shell route base.

| Area | Routes |
|---|---|
| Definitions | `GET/POST /design/workflows/definitions`, `GET/PATCH/DELETE /design/workflows/definitions/{definitionId}`, `POST .../{definitionId}/restore`, `DELETE .../{definitionId}/permanent` |
| Drafts | `GET/PUT/DELETE /design/workflows/drafts/{draftId}`, `POST .../{draftId}/promote` |
| Versions | `GET /design/workflows/definitions/{definitionId}/versions`, `GET /design/workflows/versions/{versionId}`, privileged `POST /design/workflows/versions/ingest` |
| Authoring | `POST /design/workflows/scoped-variables/analyze`, `POST /design/workflows/activities/{activityVersionId}/inputs/{inputName}/options` |

Definition reads and authoring analysis require `workflow-design.read`; mutations and direct ingestion require `workflow-design.manage`. The shared wildcard permission remains supported. Authentication and RFC 7807 error handling come from the common FastEndpoints API infrastructure. Context-sensitive option responses are non-cacheable.

## Extension points

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for persistence ports, structure handlers, and input-option providers. API clients should use the published HTTP contracts rather than implementation services.
