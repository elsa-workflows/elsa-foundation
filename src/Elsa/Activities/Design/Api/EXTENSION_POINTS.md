# Extension points — Activity Design API

`ActivitiesDesignApiFeature` is the host-independent composition root for the supported Activity Design management-client routes documented in [README.md](README.md). It does not depend on `Elsa.Workbench`.

## Endpoint composition

`ActivitiesDesignApi.MapActivitiesDesignApi(IEndpointRouteBuilder)` is the public, framework-neutral composition seam. It maps the complete owner surface as standard ASP.NET Core endpoints; hosts should compose the feature or call that mapper, not discover endpoint classes or introduce a parallel Elsa endpoint builder.

The feature registers owner-local source-generated JSON metadata and dynamic API Explorer refresh. API request/response contracts come from `Elsa.Activities.Design.Api.Core`; implementation handlers, providers, stores, authorization adapters, and compiler-generated delegate state remain outside endpoint/OpenAPI metadata so a removed feature generation can be collected.

The owner contributes `activity-design.read`, `activity-design.manage`, `activity-design.author-provider`, and `activity-design.read-provider-payload` to the Foundation Identity catalog. Route metadata uses only the read/manage action appropriate to the operation. The author-provider and provider-payload actions remain inner resource decisions through the authorization context seams below; permission implication and wildcard grants remain evaluator-owned.

## Replacement contracts

| Contract | Default | Purpose |
|---|---|---|
| `IActivityDefinitionStore` | Persistence-provider implementation | Definition reads used by catalog, definition, and diagnostics endpoints. |
| `IActivityDefinitionVersionStore` | Persistence-provider implementation | Version and normalized descriptor reads. |
| `IActivityAvailabilityEvaluator` | `DefaultActivityAvailabilityEvaluator` | Applies host include/exclude policy to catalog entries. |
| `IActivityAvailabilityDiagnosticsProjector` | `DefaultActivityAvailabilityDiagnosticsProjector` | Produces stable explanations for unavailable activities. |
| `IActivityAvailabilitySettingsStore` | `InMemoryActivityAvailabilitySettingsStore` | Stores API-managed availability settings; durable providers may replace it. |
| `IActivityFeatureAttributionResolver` | `ActivityFeatureAttributionResolver` | Best-effort resolution of the shell feature providing a CLR activity type key (the `featureId` in catalog/version `provenance`). The default resolves `activityTypeKey` → well-known type registry → assembly → runtime feature catalog, and returns `null` when either dependency is absent from the composition. |
| `IActivityAuthoringContextAsync` / `IActivityDependencyContextAsync` | `HttpContextActivityDesignAuthorizationContext` | Async request authorization seams for authoring, provider payload, management, and tenant-scoped dependency decisions. They are replacement contracts backed by Foundation Identity; a host's legacy synchronous context is adapted only when no async replacement is supplied, and provider-specific rules receive a stable resource object. |

These are single-owner seams. Replace them through DI; do not register competing implementations and rely on resolution order.

## Provider contributors

| Contract | Kind and registration | Consumer | Known implementation |
|---|---|---|---|
| `IActivityProvider` | Contributor keyed by stable provider identity. Provider features register implementations; `IActivityProviderRegistry` rejects duplicate keys and resolves exact provider/schema pairs. | Activity authoring, validation, migration, and capability projection services. | `GraphActivityProvider` *(cross-domain — Activities Graph Design)* |
| `IActivityProviderReferenceRewriter` | Enumerable, provider/schema-aware contributor. Opaque manifests are never rewritten by universal JSON guessing. | Publishing upgrade-plan persistence invokes the matching provider rewriter for exact occurrence replacements. | `GraphActivityProviderReferenceRewriter` *(cross-domain — Activities Graph Design)* |
| `IBuiltInAuthoringDescriptorProvider` | Enumerable. Contributes code-owned authoring-catalog descriptors that have no persisted CLR/JSON catalog row. The catalog handler always appends them and never gates them by availability policy. | `ListActivityAuthoringCatalog` merges the contributed descriptors into the available-activity list. | `IntrinsicAuthoringDescriptorProvider` — surfaces the Set Variable and Set Output engine intrinsics (ADR 0045) so the palette can author variable and output assignment. |

Provider packages own these implementations and their feature registrations. The Activity Graph contribution is
documented in its [contributing-feature catalog](../../Graph/Design/EXTENSION_POINTS.md).

## Sources and reconciliation

Activity definitions are populated by Activity Design reconciliation sources, not by `Elsa.Workbench`. Provider modules contribute installed activity metadata through the reconciliation contracts described in [`Reconciliation/EXTENSION_POINTS.md`](../Reconciliation/EXTENSION_POINTS.md). Groundwork persistence-specific seams are documented in their own catalog:

- [`Persistence/Groundwork/EXTENSION_POINTS.md`](../Persistence/Groundwork/EXTENSION_POINTS.md)

The catalog endpoint normalizes those stored definitions and applies availability evaluation. Context-sensitive input options deliberately belong to Workflow Design API because they require submitted workflow state and node context.

Canonical ownership is defined in the [domain-owned API spec](../../../../../specs/092-domain-owned-apis/spec.md); terminology is defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).
