# Extension points — Activity Design API

`ActivitiesDesignApiFeature` is the host-independent composition root for the supported Activity Design management-client routes documented in [README.md](README.md). It does not depend on `Elsa.Workbench`.

## Endpoint composition

`ActivitiesDesignApi.MapActivitiesDesignApi(IEndpointRouteBuilder)` is the public, framework-neutral composition seam. It maps the complete owner surface as standard ASP.NET Core endpoints; hosts should compose the feature or call that mapper, not discover endpoint classes or introduce a parallel Elsa endpoint builder.

The feature registers owner-local source-generated JSON metadata and dynamic API Explorer refresh. API request/response contracts come from `Elsa.Activities.Design.Api.Core`; implementation handlers, providers, stores, authorization adapters, and compiler-generated delegate state remain outside endpoint/OpenAPI metadata so a removed feature generation can be collected.

The owner contributes `activities/design:read`, `activities/design:manage`, `activities/design:author`, and `activities/design/provider-payload:read` to the Foundation Identity catalog. Route metadata uses only the read/manage action appropriate to the operation. The author-provider and provider-payload actions remain inner resource decisions through the authorization context seams below; permission implication and wildcard grants remain evaluator-owned.

## Replacement contracts

| Contract | Default | Purpose |
|---|---|---|
| `IActivityDefinitionStore` | Persistence-provider implementation | Definition reads used by catalog, definition, and diagnostics endpoints. |
| `IActivityDefinitionVersionStore` | Persistence-provider implementation | Version and normalized descriptor reads. |
| `IActivityAvailabilityEvaluator` | `DefaultActivityAvailabilityEvaluator` | Applies host include/exclude policy to catalog entries. |
| `IActivityAvailabilityDiagnosticsProjector` | `DefaultActivityAvailabilityDiagnosticsProjector` | Produces stable explanations for unavailable activities. |
| `IActivityAvailabilitySettingsStore` | `InMemoryActivityAvailabilitySettingsStore` | Stores API-managed availability settings; durable providers may replace it. |
| `IActivityFeatureAttributionResolver` | `ActivityFeatureAttributionResolver` | Best-effort resolution of the shell feature providing a CLR activity type key (the `featureId` in catalog/version `provenance`). The default resolves `activityTypeKey` → well-known type registry → assembly → runtime feature catalog, and returns `null` when either dependency is absent from the composition. |
| `IActivityAuthoringContextAsync` / `IActivityDependencyContextAsync` | `HttpContextActivityDesignAuthorizationContext` | Async request authorization seams for authoring, provider payload, management, and tenant-scoped dependency decisions. They are replacement contracts backed by Foundation Identity, and both resolve to the same per-request instance; provider-specific rules receive a stable resource object. |

These are single-owner seams. Replace them through DI; do not register competing implementations and rely on resolution order.

### `IActivityAuthoringCatalog` *(Contract — `Elsa.Activities.Design.Core`)*

| Contract | Default | Purpose |
|---|---|---|
| `IActivityAuthoringCatalog` | `HttpActivityAuthoringCatalog` (`internal sealed`) | The actor-gated authoring catalog for consumers that may couple only to `.Core` contracts, such as Weaver's workflow context (spec 1193, FR-003). |

- **Signature:** [`IActivityAuthoringCatalog`](../Core/Contracts/IActivityAuthoringCatalog.cs):
  `CanReadAsync(): ValueTask<bool>` and `ListAsync(): ValueTask<ActivityAuthoringCatalogResult>`, with the
  [Core models](../Core/Models/ActivityAuthoringCatalogEntry.cs) `ActivityAuthoringCatalogResult`,
  `ActivityAuthoringCatalogEntry` and `ActivityAuthoringCatalogInput`, and `ActivityAuthoringCatalogUnavailableException`.
- **When to override:** a host whose activity authority is not the request principal's `activities/design:read` grant, or
  whose designer catalog is not `IActivityAuthoringCatalogReader`, replaces it so `.Core` consumers see the same catalog
  and gate the designer applies. Replacing only the catalog reader is usually enough, because the default reads through it.

`ListAsync` and `CanReadAsync` accept no principal, permission string or tenant. The default applies the designer's own
gate: the normalized request principal must hold `activities/design:read` in its tenant, checked through
`IPermissionAuthorizationService` with the current request (`HttpContext`) as the resource, as the catalog route's
policy handler has: the same principal and tenant. During a Weaver turn the current request is the agent's own (for
example its messages request), so a host `IPermissionResourceHandler` keyed on route or path sees the agent request,
not the catalog route. A missing request, an unnormalized
principal or a refused permission is a denied result with no entries, and no catalog read. An allowed read is the
designer's own `IActivityAuthoringCatalogReader` read with `Availability = All`, projected into Core models: every entry,
addable or not, with `Available`, `AvailabilityReason`, activity type and exact version identity, display data and
declared inputs with `IsRequired`, `IsSensitive`, `IsCredential` and the optional `UiHint` (spec 1193 slice D; Minor,
additive), and the computed `IsMasked`: sensitive, a credential or hinted `password`, which is exactly what the designer
masks. `UiHint` is part of the record's value equality, so two inputs that differ only in their hint are not equal. No
default value or authored binding is carried. The
catalog is not narrowed per actor, because the designer offers no per-actor narrowing; tenant scoping is the stores'.
`CanReadAsync` is the gate alone and reads no entries.

Infrastructure failures, including a cancellation the caller did not request, become
`ActivityAuthoringCatalogUnavailableException` (Core), with a fixed message, the stable code
`activities.catalog.unavailable` and the original cause as `InnerException`. Only the caller's own cancellation propagates.

It is a replacement contract with one implementation per shell, declared in the contract's documentation, as
`IWorkflowDefinitionAuthorizer` is. `EnsureReplacementContract` is not used because its marker attribute lives in
Foundation Identity Core, which this `.Core` does not reference. Duplicates are detected in two places:

- `ActivitiesDesignApiFeature.ConfigureServices` throws when the collection already holds more than one registration,
  so this holds with or without the Tasks feature;
- `ActivityAuthoringCatalogStartupTask` fails startup when a registration added after the feature makes a second one. It
  runs only where the Tasks feature is composed (the feature does not depend on Tasks, because several composed hosts
  omit it). It owns only the configuring collection, so a copied child shell checks its own registrations, and repeated
  configuration keeps one validator. `IWorkflowDefinitionAuthorizer`'s startup validator has the same Tasks condition.

Consumers that resolve `IEnumerable<IActivityAuthoringCatalog>` must fail closed on zero or several implementations, as
the Agent workflow context does by reporting the catalog unavailable. This enumerable consumer of a replacement contract
is the accepted exception that `IWorkflowDefinitionAuthorizer` also uses, so a duplicate added after the Tasks-less
configuration still cannot select an implementation; the Agent-wide gap for existing contracts is #3633. Adding the
contract is **Minor**, additive.

## Provider contributors

| Contract | Kind and registration | Consumer | Known implementation |
|---|---|---|---|
| `IActivityProvider` | Contributor keyed by stable provider identity. Provider features register implementations; `IActivityProviderRegistry` rejects duplicate keys and resolves exact provider/schema pairs. | Activity authoring, validation, migration, and capability projection services. | `GraphActivityProvider` *(cross-domain — Activities Graph Design)*; `SpecialistActivityProvider` *(cross-domain — Specialist Design)* |
| `IActivityProviderReferenceRewriter` | Enumerable, provider/schema-aware contributor. Opaque manifests are never rewritten by universal JSON guessing. | Publishing upgrade-plan persistence invokes the matching provider rewriter for exact occurrence replacements. | `GraphActivityProviderReferenceRewriter` *(cross-domain — Activities Graph Design)* |
| `IBuiltInAuthoringDescriptorProvider` | Enumerable. Contributes code-owned authoring-catalog descriptors that have no persisted CLR/JSON catalog row. The catalog handler always appends them and never gates them by availability policy. | `ListActivityAuthoringCatalog` merges the contributed descriptors into the available-activity list. | `IntrinsicAuthoringDescriptorProvider` — surfaces the Set Variable and Set Output engine intrinsics (ADR 0045) so the palette can author variable and output assignment. |

Provider packages own these implementations and their feature registrations. The Activity Graph contribution is
documented in its [contributing-feature catalog](../../Graph/Design/EXTENSION_POINTS.md).

Each authorable manifest schema declares its supported migration sources. The legacy schema-version list applies only
to that same provider; cross-provider migrations must declare exact provider-key and schema-version pairs. Capability
projection reads these declarations without running a migration.

## Sources and reconciliation

Activity definitions are populated by Activity Design reconciliation sources, not by `Elsa.Workbench`. Provider modules contribute installed activity metadata through the reconciliation contracts described in [`Reconciliation/EXTENSION_POINTS.md`](../Reconciliation/Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md). Persistence-specific seams are documented in their own catalog:

- [`Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md)

The catalog endpoint normalizes those stored definitions and applies availability evaluation. Context-sensitive input options deliberately belong to Workflow Design API because they require submitted workflow state and node context.

Canonical ownership is defined in the [domain-owned API spec](../../../../../specs/220-domain-owned-apis/spec.md); terminology is defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).
