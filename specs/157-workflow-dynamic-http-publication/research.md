# Research: Workflow-authored Dynamic HTTP Publication

## Decision: Retain the custom workflow route-table model

ADR 0068 explicitly treats workflow-authored routes as runtime publication rather than static module mappings. The existing `HttpEndpointMiddleware` and `IRouteTable` therefore remain the execution boundary.

## Decision: Add metadata below the endpoint adapter

The existing static endpoint ownership/security records live in `Elsa.Api.AspNetCore`, while `Elsa.Http.Core` is the lower contract layer used by activities and runtime HTTP. A small HTTP-core projection avoids reversing the dependency direction or introducing an Elsa endpoint DSL.

## Decision: Optional snapshot lease seam

Changing `IRouteTable` would break test doubles and external implementations. `IRouteTableSnapshotProvider` is additive; middleware uses it when available and falls back to the existing enumerable behavior otherwise.

## Decision: Existing authorization remains authoritative

The resolver can describe the effective public/policy disposition for inventory, but provider authentication, normalized claims, and policy evaluation continue through `IHttpEndpointAuthorizationHandler`.

The closed fourth disposition is `HostPolicy`: it carries actual named policies when selected and an empty value set
for the existing authenticated-principal/default-policy case. This describes runtime enforcement without inventing a
`default` named policy that the handler never evaluates.

## Decision: Validate external addresses while retaining relative matching

Workflow route data remains endpoint-relative because the middleware strips its configured dedicated base path.
Collision validation combines that relative route with a Core-owned publication-base-path option configured by the
Activities HTTP feature, so the dynamic and ASP.NET endpoint manifests use the same external coordinate system.

## Decision: Shell-owned route-table authority

The child shell service provider owns one singleton generation state and synchronization gate shared by its scoped
route-table facades. `IMemoryCache` remains accepted by the public constructor for source compatibility but is not an
authority: eviction cannot reset live routes, and no process-global shell-key dictionary can retain unloaded shells.
