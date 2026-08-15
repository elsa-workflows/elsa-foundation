# Structured Logs Collectibility and OpenAPI Contract

## Lifecycle

1. Load an isolated Structured Logs feature/API graph into a collectible `AssemblyLoadContext` while stable framework/Core contracts remain host-owned where the production boundary allows.
2. Register feature services and map the complete option-driven Minimal API surface.
3. Materialize endpoints, execute recent/source traffic, open and cancel a live SSE exchange, serialize entries, and generate the actual ASP.NET Core OpenAPI document.
4. Inspect the keyed OpenAPI document service's operation-context cache and record module-owned `Type`, `MethodInfo`, or delegate metadata without returning those objects.
5. Retain and release route, service-provider, stream/writer, serializer, API-document/provider, and harness owners in classified stages.
6. Return only weak references and value/string diagnostics, request unload, and perform bounded forced collection.

## Required evidence

- Repeated clean cycles collect after all owned references are released, or the retained root is reported honestly.
- A retained materialized route keeps the context alive until route-owner release, then collects.
- A retained service provider or active stream keeps the context alive until cancellation/disposal/release, then collects.
- The live stream is actually started and cancelled; reflection-only mapping is insufficient.
- Actual document generation occurs through `IOpenApiDocumentProvider`.
- Cached operation contexts contain no module-owned transformer delegate, `MethodInfo`, or response `Type` after the stable-metadata design.
- Harness evidence contains identifiers, counts and stage names but no strong collectible handles.

## Failure protocol

If collection fails after owner release:

1. repeat with operation-transformer metadata absent;
2. repeat with only host/framework/Core response metadata;
3. inspect `_operationTransformerContextCache` contents;
4. capture `dotnet-dump gcroot` evidence for a collectible type/context;
5. identify whether the owner is module code, harness code, API Explorer/OpenAPI cache, routing, DI, serializer, or stream lifecycle.

Do not clear private framework caches in production. If the OpenAPI cache remains the root, keep dynamic OpenAPI support disabled or project documentation to host-owned immutable metadata/serialized OpenAPI outside the collectible generation. A shared adapter or framework-lifetime change requires a separate reviewed work unit.

Process memory, host disposal alone, successful endpoint registration, reflection-only counting, or a test that omits actual document generation is not collectibility evidence.
