# Research: Unload-Safe OpenAPI Boundary

## Decision 1: Use stable API contract assemblies for first-party modules

**Decision**: API-visible request and response models for a dynamically replaceable first-party module live in a stable `*.Api.Core` package. The implementation package keeps mappers, binders, handlers, provider adapters, and source-generated runtime serialization. Existing public namespaces remain stable; moved public types use forwarding from the former implementation assembly where binary compatibility requires it.

**Rationale**:

- Real retention probes isolated API Explorer processing of collectible request/response `Type` metadata as the causal boundary. Replacing only those types with stable types made the implementation generation collectible.
- Structured Logs is the positive production control: its description method and visible response contracts belong to framework/shared Core assemblies, and its combined query/SSE/serialization/OpenAPI lifecycle collects repeatedly.
- ASP.NET Core's OpenAPI service stores operation contexts and schema identifiers for the service-provider lifetime. A type whose public contract lifetime matches that host/shared lifetime is safe; an implementation-generation type is not.
- The split preserves native API Explorer/OpenAPI behavior and exact schemas. It requires no schema reimplementation, custom document endpoint, or private cache manipulation.
- It is the ordinary three-layer contract pattern already sanctioned by the constitutions and matches Nuplane Strategy B.

**Alternatives considered**:

- Keep DTOs in collectible implementation assemblies and clear framework caches. Rejected: the relevant caches are private, process behavior is undocumented, and cleanup would be timing-dependent.
- Replace collectible types with `object` or omit documentation. Rejected: it weakens the public contract and violates the migration evidence gate.
- Move workflow API read models into `Elsa.Workflows.Design.Core` or `Runtime.Core`. Rejected: Elsa §E2.9 places projections in the API/query layer, not authored state or runtime artifacts. An API sub-domain Core preserves both lifetime and ownership.

## Decision 2: Validate completed endpoint metadata before publication

**Decision**: Add a shared endpoint convention that runs as a final endpoint-build convention. It requires ownership metadata, inspects the completed API Explorer-facing metadata, and rejects any artifact owned by a collectible load context. The accepted endpoint receives immutable lifetime-boundary metadata for inventory and diagnostics.

The validator covers at minimum:

- `IAcceptsMetadata.RequestType`;
- `IProducesResponseTypeMetadata.Type`;
- metadata-object runtime types;
- `Type`, `MemberInfo`, `MethodInfo`, and delegate values;
- endpoint-specific OpenAPI transformers and their targets;
- serializer metadata or contexts exposed through endpoint metadata.

The request delegate itself remains generation-owned by routing and is not a documentation artifact. Its API-description `MethodInfo` metadata must be host/shared (the established `RequestDelegate.Invoke` pattern) rather than the module handler method.

**Rationale**: Endpoint-builder final conventions execute while a CShells candidate generation is still being materialized. Throwing there rejects the candidate before the dynamic endpoint data source publishes it; the transactional CShells lifecycle preserves the previous generation.

**Alternatives considered**:

- A post-publication background scanner. Rejected: unsafe metadata would already be visible and cached.
- Only an architecture test. Rejected as the sole defense because external or runtime-loaded modules need a deterministic operational diagnostic.
- Infer safety from assembly-name suffix alone. Rejected: actual load-context lifetime is authoritative at runtime; naming remains an architecture/package gate.

## Decision 3: Preserve native ASP.NET Core document generation

**Decision**: Continue to use ordinary API Explorer, `AddOpenApi`, `IOpenApiDocumentProvider`, and `MapOpenApi` for accepted first-party endpoints.

**Rationale**: Official ASP.NET Core documentation defines request/response endpoint metadata and operation transformers as the native document inputs, and .NET 10 regenerates documents from the current endpoint descriptions. Once those inputs carry only host/shared artifacts, the framework pipeline remains both compatible and unload-safe for the implementation generation.

Dynamic endpoint sources require one additional standard adapter. Endpoint API Explorer caches its
description groups by MVC action-descriptor version and does not subscribe to
`EndpointDataSource.GetChangeToken()` itself. Adapting that token through
`IActionDescriptorChangeProvider` makes the existing provider regenerate a complete immutable group
after each endpoint-generation swap; no replacement document provider is introduced.

**Primary references**:

- [ASP.NET Core: include OpenAPI metadata](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)
- [ASP.NET Core: generate OpenAPI documents](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0)
- Installed `Microsoft.AspNetCore.OpenApi` 10.0.10 implementation, specifically its internal document operation-context and schema-ID caches

**Alternatives considered**:

- Replace `IApiDescriptionGroupCollectionProvider` or the keyed internal document service. Rejected: the mapped document endpoint resolves the internal service directly, and replacement would be brittle.
- Exclude dynamic endpoints and add host-owned projection endpoints. Rejected for first-party modules because it duplicates route and metadata manifests.

## Decision 4: Defer serialized OpenAPI snapshots to a separate third-party boundary

**Decision**: Do not implement a serialized OpenAPI contribution registry in #1392. Record it as the fallback if a future third-party plugin must unload its contract assembly independently of the host.

**Rationale**: A canonical value-only snapshot is the most general lifetime boundary, but it introduces an Elsa-owned schema/operation snapshot contract, validation and merge engine, generation store, custom document endpoint, cache policy, and compatibility surface. The first-party program already controls package composition and can use stable API contracts. Building the general system now would violate the preference for native ASP.NET primitives and would require explicit review under draft framework §2.24.

**Alternatives considered**:

- Adopt snapshots for every first-party endpoint immediately. Rejected as unnecessary complexity until stable contract separation is empirically insufficient.
- Use snapshots only for individual schemas while retaining API Explorer. Rejected because operation contexts can retain other module metadata; a partial boundary creates false confidence.

## Decision 5: Produce an independent upstream reproduction

**Decision**: Retain a minimal framework-only test/reproduction with one collectible typed endpoint. If it confirms retention without Elsa or module transformers, report it upstream as a request for a supported generation-aware lifetime boundary. Elsa's solution does not wait for that outcome.

**Rationale**: The installed implementation has no public eviction/generation seam for its operation-context or schema-ID caches. A small reproduction benefits the ecosystem and can validate future framework versions, but an upstream fix has an unknown schedule and cannot be the program gate.

**Alternatives considered**:

- File an issue with only Elsa evidence. Rejected: the report should be independently reproducible and free of CShells/Nuplane complexity.
- Treat framework retention as an Elsa bug only. Rejected: the causal path is inside the built-in API-description/document services, even though Elsa must still own its safe boundary.
