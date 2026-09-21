# Workflow Design API

`Elsa.Workflows.Design.Api` is the supported management-client slice for authored workflows. It owns workflow definitions, first-class drafts, immutable Design versions, scoped-variable analysis, and context-sensitive activity input options. Publishing and executable inspection belong to their respective domain APIs.

The ownership and lifecycle rules are specified in [`specs/092-domain-owned-apis`](../../../../../specs/092-domain-owned-apis/spec.md). Terms such as *feature*, *API capability*, *draft*, and *workflow executable* are defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).

## Composition

Add `WorkflowsDesignApiFeature` to the active shell and compose a Workflow Design persistence provider. The feature registers its Minimal API mapper, mediator handlers, scoped-variable authoring services, and contextual input-option resolver. Activity input options also read the Activity Design definition/version stores, so a host exposing that operation must compose Activity Design persistence.

This package does not reference or depend on `Elsa.Workbench`; the server application is only one possible reference composition.

## Supported routes

All routes are relative to the active shell route base.

| Area | Routes |
|---|---|
| Definitions | `GET/POST /design/workflows/definitions`, `POST /design/workflows/definitions/submit`, `GET/PATCH/DELETE /design/workflows/definitions/{definitionId}`, `POST .../{definitionId}/restore`, `DELETE .../{definitionId}/permanent` |
| Drafts | `GET/PUT/DELETE /design/workflows/drafts/{draftId}`, `POST .../{draftId}/promote` |
| Versions | `GET /design/workflows/definitions/{definitionId}/versions`, `GET /design/workflows/versions/{versionId}`, privileged `POST /design/workflows/versions/ingest` |
| Authoring | `POST /design/workflows/scoped-variables/analyze`, `POST /design/workflows/activities/{activityVersionId}/inputs/{inputName}/options` |
| Authoring schema | `GET /design/workflows/definitions/submit/schema`, `GET /design/workflows/structures` |

Definition reads and authoring analysis require `workflow-design.read`; mutations and direct ingestion require `workflow-design.manage`. The shared wildcard permission remains supported. Authentication and RFC 7807 error handling come from the shared identity and ASP.NET Core endpoint conventions. Context-sensitive option responses are non-cacheable.

The authoring-schema endpoints make the design API a complete headless authoring surface (issue #1164). `GET .../definitions/submit/schema` returns a versioned, fingerprinted JSON Schema for the submit body, generated at request time from the actual wire types so it cannot drift from the contract. `GET .../structures` enumerates the composite-activity structure kinds registered in the active shell (`IActivityStructureHandler` set) with each kind's `schemaVersion`, scoped-variable support, and a generated JSON Schema of its authored payload. Polymorphic argument values and activity-owned structure payloads intentionally render as unconstrained schemas in the submit-body document; per-kind payload shapes come from the structures endpoint instead.

## Extension points

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for persistence ports, structure handlers, and input-option providers. API clients should use the published HTTP contracts rather than implementation services.
