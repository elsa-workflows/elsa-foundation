# Contract: OpenAPI Lifetime Boundary

## Authoring contract

Every first-party endpoint promised to be dynamically unloadable MUST opt into the shared lifetime convention after applying all request, response, authorization, ownership, and OpenAPI metadata.

Conceptual usage:

```csharp
builder
    .WithOwner(owner)
    .WithMetadata(stableDescriptionMethod, requestAndResponseMetadata)
    .RequireStableOpenApi();
```

`RequireStableOpenApi` is an endpoint convention, not an endpoint framework. It returns the original standard convention builder and does not affect request binding, handler invocation, serialization, authorization, routing, or result execution.

## Acceptance contract

An endpoint is accepted when:

1. it has exactly one valid ownership record;
2. every API Explorer-facing request and response contract belongs to a non-collectible shared/host lifetime;
3. every metadata object's runtime type belongs to a non-collectible shared/host lifetime;
4. every metadata member, method, delegate, transformer, and target belongs to a non-collectible shared/host lifetime;
5. no live `JsonSerializerContext`, `JsonTypeInfo`, or `JsonSerializerOptions` object is exposed as endpoint metadata; runtime serialization stays behind the implementation boundary and API Explorer sees only stable contract `Type` metadata; and
6. the validator can describe the accepted classification without retaining an implementation-generation artifact.

The implementation request delegate may remain generation-owned because routing, not host documentation, owns it and releases it during drain. Any method metadata supplied for API description must be stable.

The host composition root for a changing `EndpointDataSource` registers
`AddDynamicEndpointApiExplorerRefresh()`. The adapter forwards the endpoint source's change token
through `IActionDescriptorChangeProvider`, which invalidates API Explorer's immutable description
collection. It must be registered before the host resolves API Explorer/OpenAPI services.

## Rejection contract

Validation runs as a final endpoint-build convention. A violation throws one domain-scoped exception containing:

- endpoint owner;
- shell and generation when available;
- route/display identity;
- violation category;
- offending artifact identity; and
- collectible load-context identity.

The diagnostic is deterministic and does not include object hash codes or nondeterministic enumeration order. Candidate construction fails before endpoint visibility. CShells candidate rollback preserves the prior accepted generation.

## Compatibility contract

- No route, method, operation ID, tag, security declaration, schema, content type, header, or status changes merely because the lifetime convention is applied.
- OpenAPI-visible types moved into `*.Api.Core` retain their public namespace and JSON contract.
- Public types formerly emitted from an implementation assembly use type forwarding where needed to preserve binary consumers.
- No contract may be replaced with `object`, hidden from documentation, or approved as a difference solely to satisfy unloadability.

## Evidence contract

The reusable gate is not complete until one test cycle performs all of the following in the same collectible generation:

1. map an endpoint through the production convention;
2. execute a representative request;
3. exercise source-generated request and response serialization;
4. enumerate real API descriptions;
5. generate a real OpenAPI document;
6. remove/replace endpoints and dispose the generation provider;
7. unload the implementation context; and
8. prove load context, assembly, handler/mapping types, delegates, serializer context, endpoint metadata, and provider become unreachable.

Repeat the cycle three times. A separate unsafe-type case must prove rejection before publication and prior-generation preservation.

## Non-contracts

- Garbage-collector timing is not a production API.
- Private ASP.NET Core cache structure is diagnostic evidence, not an integration seam.
- A serialized OpenAPI fragment registry is not part of this contract.
- Third-party modules that cannot share stable wire-contract lifetime require a separate approved boundary; they must not claim unloadability through this convention.
