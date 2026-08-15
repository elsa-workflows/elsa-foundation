# Secrets API Collectibility Contract

## Lifecycle

1. Load an isolated Secrets API/module graph into a collectible `AssemblyLoadContext`.
2. Register feature services and map the complete Minimal API surface.
3. Materialize the route data source; execute representative query and JSON-body requests; generate the consumed OpenAPI projection.
4. Retain and release route, service-provider, serializer/documentation, and harness owners in classified stages.
5. Return only weak references to the load context, assembly, and representative API type.
6. Request unload and perform bounded forced collection.

## Required evidence

- Repeated clean cycles collect after all owned references are released.
- A retained materialized route keeps the context alive until route-owner release, then collects.
- A retained service provider keeps the context alive until disposal/release, then collects.
- Serializer or OpenAPI-documentation retention is identified as its own stage with an actionable owner.
- Harness diagnostics contain identifiers and stage names but no strong reference to a collectible object.
- The fixture invokes the production mapper and does not substitute reflection-only route counting.

Process memory, host disposal alone, successful route registration, or absence of an exception is not collectibility evidence.
