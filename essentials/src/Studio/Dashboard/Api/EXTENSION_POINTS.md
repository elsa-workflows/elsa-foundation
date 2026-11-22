# Dashboard native API

`StudioDashboardApi` declares the Foundation Identity abstractions dependency for normalized identity, authorization and shell authentication/authorization middleware, and specializes `DashboardManagementFeature` and maps authorized dataset discovery, native query validation/preview/drill and saved-question/dashboard resource operations. It publishes `elsa.api.studio-dashboard` contract1 with the relations listed in [the API guide](README.md).

## Replacements and dependencies

The feature supplies scoped `HttpDashboardActorContext` for `IDashboardActorContext` and scoped `DashboardAudienceDirectory` for `IDashboardAudienceDirectory`, preserving deliberate host replacements. `DashboardAudienceCursors` holds process-wide cursor signing state. One guard instance runs through both plain-host IHostedService and CShells IShellInitializer hooks. It checks descriptor cardinality for exactly one actor, query/resource service, store, definition validator and audience directory without constructing request-scoped replacements. A host must select a definition store and normal Identity authorization services; a request never supplies an author principal.

The actor resolver requires one current normalized principal, canonical user/tenant mapping and current effective operation permissions. Role audiences use current active user/membership records and tenant role lookup. See the [Core contract catalog](../Core/EXTENSION_POINTS.md) and [Identity catalog](../../../Foundation/Identity/Core/EXTENSION_POINTS.md).

## Contributions

`DashboardResourcePermissionContributor` registers through `AddPermissionContributor` and contributes the question/dashboard read/create/edit/share/delete permission declarations to Foundation Identity's single `CompositePermissionCatalog`. It grants no data access. Native endpoint authorization and resource ACLs are enforced separately from source-owned record/field/aggregate authorization.

No dataset, event aggregator or model provider is supplied by the API. Query handlers consume the named contribution events registered by Management. HTTP operations propagate RequestAborted, use ETags/keyed creation and native domain error contracts. The [API guide](README.md) owns endpoint and conditional-write details.
