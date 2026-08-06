# Activity Design API

`Elsa.Activities.Design.Api` owns the supported management-client view of installed activity definitions, versions, authoring descriptors, and availability policy. The normalized catalog is the client bootstrap surface for constructing authored activity state; workflow-context evaluation remains in Workflow Design API.

See the [domain-owned API specification](../../../../../specs/092-domain-owned-apis/spec.md) and [Elsa glossary](../../../../../docs/glossary/elsa.md) for canonical ownership and terminology.

## Composition

Add `ActivitiesDesignApiFeature` to the active shell together with an Activity Design persistence provider and the reconciliation features that populate its definitions. The feature supplies default availability evaluation, diagnostics projection, and an in-memory availability-settings store; durable hosts should replace that store as appropriate.

The package has no dependency on `Elsa.Workbench`. Custom hosts expose the same routes by composing the feature in their own shell.

## Supported routes

| Area | Routes |
|---|---|
| Authoring catalog | `GET /design/activities/catalog?availability=addable|all` |
| Availability | `GET/PUT /design/activities/availability/settings`, `GET /design/activities/availability/diagnostics` |
| Definitions | `GET/POST /design/activities/definitions`, `GET /design/activities/definitions/{id}`, `GET /design/activities/definitions/{definitionId}/versions` |
| Versions | `GET/POST /design/activities/versions`, `GET /design/activities/versions/{versionId}` |

Catalog and read operations use `activity-design.read`; management operations use `activity-design.manage`. The shared wildcard permission remains supported. Authentication and RFC 7807 errors are provided by the common FastEndpoints API infrastructure.

The default catalog returns addable activities. `availability=all` is the diagnostic/administrative view and includes unavailable entries with their reason; it does not change availability.

## Extension points

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) and the [reconciliation extension catalog](../Reconciliation/EXTENSION_POINTS.md).
