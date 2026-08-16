# Wave 1 Minimal API migration report

Status: evidence complete for the bounded Wave 1 migration; recommendation is ready for review.

This report records the bounded implementation evidence for issue #1367, Wave 1. The scope is
six first-party owners and exactly eight FastEndpoints registrations:

| Owner | Routes | Result |
| --- | ---: | --- |
| `Elsa.Api.Capabilities` | 1 | Explicit Minimal API mapper and owner metadata |
| `Elsa.Attention.Api` | 1 | Explicit Minimal API mapper and owner metadata |
| `Elsa.Expressions.Api` | 2 | Explicit Minimal API mapper and owner metadata |
| `Elsa.Expressions.JavaScript.Rendering` | 1 | Explicit Minimal API mapper and owner metadata |
| `Elsa.Workflows.Runtime.JavaScript` | 1 | Explicit Minimal API mapper and owner metadata |
| `Elsa.Workflows.Dashboard` | 2 | Explicit Minimal API mapper and owner metadata |
| **Total** | **8** | **Eight Wave 0 baseline registrations removed** |

## Evidence completed

- Existing HTTP method, route, binding, JSON/error status, content-type, response metadata, and
  runtime behavior were frozen in `Wave1MinimalApiContractTests` and the owner test suites. The
  table-driven OpenAPI regression identifies the two deliberate JavaScript metadata corrections
  instead of treating them as silent parity.
- Minimal API and FastEndpoints routes run concurrently through the same Foundation Identity policy
  provider and evaluator. The real-host matrix covers anonymous `401`, authenticated denied `403`,
  exact permission, implied permission, wildcard permission, and normalized authentication claims.
- Every migrated route carries module owner, Minimal API authoring, permission, exact legacy
  operation-id, and host-application tag metadata. A table-driven generated-OpenAPI regression
  verifies all eight paths/methods, operation IDs, `testhost` tag preservation, response statuses,
  response content types/schemas, and the runtime-JS `RequestModel` request schema.
- Feature-local FastEndpoints registrations and the eight matching transition-baseline entries were
  removed. The executable transition ratchet is reduced from 164 to 156 entries across 12 owners.
- Each owner is loaded and mapped three times through a collectible `AssemblyLoadContext`. The
  lifecycle probe invokes the real feature `ConfigureServices`, builds and disposes its provider,
  publishes and clears route data sources, and weak-references feature, endpoint, provider, and
  serializer objects.

## Serializer ownership finding and resolution

The lifecycle probe first reproduced a real retention hazard: `JsonSerializer` reflection metadata
retained the collectible load context, owner assembly, and mapper type after route data sources, the
feature instance, the DI provider, and serializer options were released. This is not a test-only
artifact; dynamically loaded owner types must not enter a process-global reflection resolver cache.

Each migrated owner now declares a module-owned source-generated `JsonSerializerContext`, and every
Minimal API response/request path uses its generated `JsonTypeInfo` instead of anonymous payloads or
default reflection serialization. JavaScript execution uses explicit request and response contracts;
the rendering success/failure envelopes are explicit contracts as well. The lifecycle probe invokes
each owner feature's real `ConfigureServices`, publishes routes, touches that production context's
`Default` options/type metadata, deserializes a non-null production request/response DTO sample, and
executes the actual `Results.Json(payload, typeInfo)` writer against a response stream. It then
clears routes, disposes the provider, and verifies weak references for the ALC, assembly, mapper,
feature, endpoints, provider, payload, result, HTTP context, context, options, and type metadata
across three cycles for all six owners. All cycles pass.

The non-collectible OpenAPI contract test separately verifies all eight operations, including the
JavaScript execution request schema, content type, and response statuses. OpenAPI generation/cache
unloadability is intentionally not claimed by the collectible route/DI/serializer gate; dynamic
OpenAPI lifetime is a follow-up concern for the broader publication track.

## OpenAPI regression and compatibility exceptions

The legacy operation IDs are preserved with standard `IEndpointNameMetadata` (`WithName`), and the
legacy host-application tag is preserved with standard `ITagsMetadata` resolved from
`IHostEnvironment.ApplicationName`. The eight-operation regression runs against a real ASP.NET Core
OpenAPI document and verifies the complete response status/content matrix. The JavaScript execution
request retains the legacy `RequestModel` schema identifier while using an explicit source-generated
request contract.

The baseline probe also found two inaccurate legacy metadata declarations. JavaScript rendering and
execution advertised `204` instead of their actual successful `200`, and omitted response statuses
that the handlers can produce (`500` for rendering; `400` and `500` for execution). The migrated
metadata intentionally advertises the truthful `200`/`400`/`500` matrix. These are explicit
compatibility exceptions requiring reviewer approval; they are not silently counted as parity.

## Validation commands

The focused Capabilities (9), Attention (4), Expressions (7), and Dashboard (30) suites pass. The
Wave 1 route/OpenAPI contract suite, shared-evaluator coexistence matrix, and collectible lifecycle
suite also pass. The lifecycle suite covers all six owners across three repeated cycles and fails if
any owner, assembly, endpoint, provider, payload, result, HTTP context, serializer context/options,
or generated type metadata remains rooted.

The collectible suite also passes four isolated `--no-build` repetitions. It snapshots each owner
assembly before loading and runs in a non-parallel xUnit collection so concurrent feature builds or
other architecture hosts cannot race the unload evidence.

The full `Elsa.Server.slnx` build passes after restore with zero errors. It retains repository-wide
warnings (including the existing SSH.NET advisory, analyzer/nullable/obsolete warnings, and the
source-generated enum-converter warnings noted below).

The follow-up production work should apply the same source-generated-context ownership rule to future
dynamic modules and add an integration gate against accidental default global caching before enabling
dynamic unload for additional modules. The current contexts surface existing non-AOT generic enum
converter warnings in the affected core contracts; those warnings are advisory follow-up work and do
not change the unload result.

## Recommendation and follow-up boundary

**Recommendation: coexist, then migrate incrementally.** Keep FastEndpoints available for existing
feature APIs while making explicit Minimal API mappers the preferred boundary for new and migrated
routes. This wave proves that both authoring models can contribute ordinary ASP.NET Core endpoints in
one host and can share Foundation Identity authorization. It does not justify a repository-wide
FastEndpoints removal or a public route-contract redesign.

The production follow-up should be split into independently reviewable issues:

1. Adopt the explicit mapper, owner metadata, catalog-owned permission, and module-owned
   source-generated serializer-context pattern for the next bounded API wave.
2. Add the dynamic publication manifest/collision rejection and atomic generation swap described by
   the parent spike before enabling broader collectible feature unload.
3. Track the existing generic enum-converter AOT warnings and extend the source-generated-context
   pattern to future dynamic modules; do not solve those unrelated warnings in this migration.
