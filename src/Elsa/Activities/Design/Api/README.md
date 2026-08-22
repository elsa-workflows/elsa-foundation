# Activity Design API

`Elsa.Activities.Design.Api` owns the supported management-client view of installed activity definitions, versions, authoring descriptors, and availability policy. The normalized catalog is the client bootstrap surface for constructing authored activity state; workflow-context evaluation remains in Workflow Design API.

See the [domain-owned API specification](../../../../../specs/092-domain-owned-apis/spec.md) and [Elsa glossary](../../../../../docs/glossary/elsa.md) for canonical ownership and terminology.

## Composition

Add `ActivitiesDesignApiFeature` to the active shell together with an Activity Design persistence provider and the reconciliation features that populate its definitions. The feature supplies default availability evaluation, diagnostics projection, and an in-memory availability-settings store; durable hosts should replace that store as appropriate.

The package has no dependency on `Elsa.Workbench`. Custom hosts expose the same routes by composing the feature in their own shell. `ActivitiesDesignApiFeature` calls the public `ActivitiesDesignApi.MapActivitiesDesignApi(IEndpointRouteBuilder)` mapping entry point; all 38 routes are ordinary ASP.NET Core Minimal API endpoints with standard route, authorization, API Explorer, and OpenAPI metadata.

API-visible request and response types live in `Elsa.Activities.Design.Api.Core`. The implementation package forwards the former public type names, supplies owner-local source-generated JSON metadata, and keeps providers, stores, handlers, and transport adapters outside endpoint/OpenAPI contract metadata. This separation lets a retired feature generation release its endpoint, DI, serializer, and API-description state.

## Supported route areas

| Area | Routes |
|---|---|
| Authoring catalog | `GET /design/activities/catalog?availability=addable|all` |
| Availability | `GET/PUT /design/activities/availability/settings`, `GET /design/activities/availability/diagnostics` |
| Definitions | Definition list/get/create/update, picker, recommendation, drafts, versions, and fork preview under `/design/activities/definitions` |
| Drafts | Draft get/create/replace/presentation/conflict-copy/validate/migrate/discard/diff and contract proposals under `/design/activities/drafts` |
| Versions | Version get/diff/dependencies and retire/restore/revoke under `/design/activities/versions` |
| Forks | Candidate apply and idempotency status under `/design/activities/fork-candidates` and `/design/activities/forks` |
| Upgrade plans | Create/get/apply/receipt/refresh under `/design/activities/upgrade-plans` |

The exact method/template/operation inventory is the executable [38-route manifest](../../../../../specs/166-activities-design-api-migration/contracts/activities-design-route-manifest.md).

Catalog and read operations declare exactly `activity-design.read`; management operations declare exactly `activity-design.manage`. Foundation Identity owns normalized-principal trust, implication, wildcard compatibility, replaceable evaluation, tenant/resource handlers, and the standard `401`/`403` boundary. Wildcard is evaluator-level grant compatibility and is not endpoint-owned policy metadata. Transport errors preserve the reviewed RFC 7807 contract through the owner-local Minimal API mapping.

The default catalog returns addable activities. `availability=all` is the diagnostic/administrative view and includes unavailable entries with their reason; it does not change availability.

Catalog items and version details carry a `provenance` object (`sourceKind`, `sourceId`, `featureId`) identifying the reconciliation source that contributed the definition version and — for CLR-provided activities — the shell feature that provides the activity type (issue #1164). `featureId` is resolved best-effort at read time (`activityTypeKey` → well-known type registry → assembly → runtime feature catalog) and is `null` for non-CLR rows or when attribution is unavailable; a non-null `featureId` may name a feature that is not enabled in the current composition, which is the "enable feature X to use this activity" signal for headless clients. Built-in engine intrinsics have no provenance.

## Extension points

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) and the [reconciliation extension catalog](../Reconciliation/EXTENSION_POINTS.md).
