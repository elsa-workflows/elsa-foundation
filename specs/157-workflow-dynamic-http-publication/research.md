# Research: Workflow-authored Dynamic HTTP Publication

## Decision: Retain the custom workflow route-table model

ADR 0068 explicitly treats workflow-authored routes as runtime publication rather than static module mappings. The existing `HttpEndpointMiddleware` and `IRouteTable` therefore remain the execution boundary.

## Decision: Add metadata below the endpoint adapter

The existing static endpoint ownership/security records live in `Elsa.Api.AspNetCore`, while `Elsa.Http.Core` is the lower contract layer used by activities and runtime HTTP. A small HTTP-core projection avoids reversing the dependency direction or introducing an Elsa endpoint DSL.

## Decision: Optional snapshot lease seam

Changing `IRouteTable` would break test doubles and external implementations. `IRouteTableSnapshotProvider` is additive; middleware uses it when available and falls back to the existing enumerable behavior otherwise.

## Decision: Existing authorization remains authoritative

The resolver can describe the effective public/policy disposition for inventory, but provider authentication, normalized claims, and policy evaluation continue through `IHttpEndpointAuthorizationHandler`.
