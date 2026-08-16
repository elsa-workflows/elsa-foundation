# Studio Preferences Collectibility Contract

## Lifecycle

1. Load an isolated endpoint/module assembly into a collectible `AssemblyLoadContext`.
2. Map the Studio Preferences Minimal API surface and, separately, build the service graph.
3. Capture only weak references to the load context, assembly, and endpoint type.
4. Release the route data source, service provider, and every harness-owned strong reference.
5. Request unload and perform bounded forced collection.

## Required evidence

- Repeated clean cycles collect.
- A deliberately retained route keeps the context alive until released, then collects.
- A deliberately retained service provider keeps the context alive until disposed/released, then collects.
- Diagnostics identify the retaining stage without returning a collectible object strongly.
- Serializer-cache retention, if observed, is a separate classification and does not weaken route/service assertions.

Process memory, host disposal alone, or the absence of an exception is not collectibility evidence.
