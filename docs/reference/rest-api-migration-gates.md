# REST API migration gates

Issue [#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346) established reusable evidence for the First-party REST API Consolidation program. The gates protect externally visible behavior while endpoint authoring moves from FastEndpoints to ASP.NET Core Minimal APIs.

## Endpoint metadata

Every enabled first-party endpoint must publish standard ASP.NET Core metadata for:

- one stable host, module, or dynamic-shell owner (including shell and exact generation for dynamic routes);
- one authoring model;
- exactly one primary security disposition: Foundation permission, intentional public access with category/reason and anonymous metadata, host credential, or an owned host-policy disposition with authorization metadata. A host-policy disposition names selected policies when present; an empty policy set is reserved for authenticated-principal/default-policy compatibility.

Use the conventions in `Elsa.Api.AspNetCore`. Permission endpoints continue to use Foundation Identity's canonical policy codec and catalog; do not introduce path middleware or an endpoint-specific permission evaluator.

## Compatibility evidence

Use `Elsa.Api.Compatibility.Testing` to capture canonical HTTP observations and a supplied OpenAPI JSON document before and after an authoring change. The comparer covers route/method identity, binding, JSON, status, media types, headers, ProblemDetails, paging/filtering, bounded streaming and terminal state, plus consumed OpenAPI parameters, bodies, responses, media types, and schemas.

Intentional changes require an exact record in `rest-compatibility-approved-differences.json`. Each approval names one endpoint, method, case, facet, old value, new value, owner, reason, and follow-up. Unused approvals fail. There is no automatic baseline-update switch.

## Authoring and permission gates

`endpoint-manifest.json` is the reviewed runtime inventory for the representative host. It is built from `EndpointDataSource`, not source-route inference. Its permission dispositions must resolve to one active feature-owned `IPermissionContributor`; the administrative `*` grant is never a catalog permission.

`fastendpoints-transition-exceptions.json` freezes every discovered first-party FastEndpoints registration. Literals, cross-document constants, interpolated constants, and known route-helper compositions are resolved to exact routes and methods. Only genuinely runtime-computed routes use an exact normalized aggregate fingerprint of their owning repository source set; generated `bin` and `obj` files are excluded so build order cannot change the evidence. Changing any owning source invalidates the exception. New, expanded, stale, ambiguous, owner-mismatched, or dynamically unloadable registrations fail.

## Collectibility evidence

The collectible harness compiles an isolated endpoint type with Roslyn, loads it into a collectible `AssemblyLoadContext`, and returns only weak references across a non-inlined lifecycle boundary. Clean cycles must collect repeatedly. Deliberate route-delegate, DI-provider, serializer-options, and harness retention probes classify which seam still owns the collectible type.

## Verification

```bash
dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Review baseline diffs deliberately. A migration removes its matching transition exceptions; it does not regenerate the registry to accept a broader legacy surface.
